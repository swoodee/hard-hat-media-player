using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HardHatMediaPlayer.Services;

public sealed class SubtitleSettings
{
    public string ApiKey { get; set; } = "";
    public string Username { get; set; } = "";

    /// <summary>The password, encrypted for the current Windows user (DPAPI). Never stored in clear text.</summary>
    public string? PasswordProtected { get; set; }

    public string Languages { get; set; } = DefaultLanguages();

    /// <summary>Fetch subtitles automatically when a video starts without any.</summary>
    public bool AutoDownload { get; set; } = true;

    private static string DefaultLanguages()
    {
        var mine = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return mine is "en" or "iv" ? "en" : $"{mine},en";
    }
}

/// <summary>Encrypts secrets for the current Windows user.</summary>
public static class Secret
{
    // Kept from the app's earlier name: changing it would make already-saved passwords unreadable.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VlcRedesign.OpenSubtitles");

    public static string? Protect(string? value) =>
        string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }
    }
}

/// <summary>Finds, downloads and saves subtitles next to the video, VLsub-style.</summary>
public sealed class SubtitleService : IDisposable
{
    private static readonly string FallbackDir = Path.Combine(LibraryStore.AppDir, "subtitles");
    private readonly HashSet<string> _autoTried = new(StringComparer.OrdinalIgnoreCase);

    public SubtitleSettings Settings { get; }
    public OpenSubtitlesClient Client { get; }

    public SubtitleService(SubtitleSettings settings)
    {
        Settings = settings;
        Client = new OpenSubtitlesClient(settings);
    }

    public bool IsConfigured => Client.IsConfigured;

    /// <summary>A search query from the file name: "The.Wand.S01E05.1080p.WEB" → "The Wand S01E05".</summary>
    public static string GuessQuery(string videoPath)
    {
        var name = Path.GetFileNameWithoutExtension(videoPath).Replace('.', ' ').Replace('_', ' ');
        var junk = new[] { "1080p", "720p", "2160p", "480p", "4k", "web", "webrip", "web-dl", "bluray", "brrip", "hdtv", "x264", "x265", "h264", "hevc", "aac" };
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cut = Array.FindIndex(words, w => junk.Contains(w.ToLowerInvariant()) || w.StartsWith('['));
        return string.Join(" ", cut > 0 ? words.Take(cut) : words);
    }

    public Task<IReadOnlyList<SubtitleResult>> SearchAsync(string videoPath, string? query, bool byHash, CancellationToken ct = default)
    {
        string? hash = null;
        if (byHash)
        {
            hash = OpenSubtitlesClient.ComputeMovieHash(videoPath)
                   ?? throw new OpenSubtitlesException("This file is too small or can't be read, so it can't be matched by hash. Try searching by name.");
        }
        return Client.SearchAsync(hash, byHash ? null : query, Settings.Languages, ct);
    }

    /// <summary>Downloads a result and saves it beside the video so it loads automatically next time too. Returns the saved path.</summary>
    public async Task<(string Path, int? Remaining)> DownloadAsync(SubtitleResult result, string videoPath, CancellationToken ct = default)
    {
        var (data, fileName, remaining) = await Client.DownloadAsync(result.FileId, ct);
        var ext = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        if (ext is not (".srt" or ".ass" or ".ssa" or ".sub" or ".vtt" or ".smi" or ".txt")) ext = ".srt";
        var lang = new string(result.Language.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        var baseName = Path.GetFileNameWithoutExtension(videoPath) + (lang.Length > 0 ? "." + lang : "");

        // Next to the video (libVLC autoloads "video.lang.srt"); fall back to app data if that folder is read-only.
        foreach (var dir in new[] { Path.GetDirectoryName(videoPath), FallbackDir })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                Directory.CreateDirectory(dir);
                var path = UniquePath(Path.Combine(dir, baseName + ext));
                await File.WriteAllBytesAsync(path, data, ct);
                return (path, remaining);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        throw new OpenSubtitlesException("Couldn't save the subtitle file.");
    }

    /// <summary>
    /// For a video that started without subtitles: fetch the best exact (hash) match in the preferred
    /// languages. Name-only matches are skipped here because they're often for a different release.
    /// </summary>
    public async Task<(string Path, SubtitleResult Result)?> AutoDownloadAsync(string videoPath, CancellationToken ct = default)
    {
        if (!Settings.AutoDownload || !IsConfigured || !_autoTried.Add(videoPath)) return null;
        var hash = OpenSubtitlesClient.ComputeMovieHash(videoPath);
        if (hash is null) return null;

        var results = await Client.SearchAsync(hash, null, Settings.Languages, ct);
        var order = Settings.Languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.ToLowerInvariant()).ToList();
        var best = results
            .Where(r => r.HashMatch)
            .OrderBy(r => order.IndexOf(r.Language.ToLowerInvariant()) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(r => r.HearingImpaired)
            .ThenByDescending(r => r.Downloads)
            .FirstOrDefault();
        if (best is null) return null;

        var (path, _) = await DownloadAsync(best, videoPath, ct);
        return (path, best);
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}.{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public void Dispose() => Client.Dispose();
}
