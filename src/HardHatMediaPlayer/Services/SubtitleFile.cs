using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HardHatMediaPlayer.Services;

/// <summary>One subtitle line on screen from <see cref="StartMs"/> to <see cref="EndMs"/>.</summary>
public sealed record SubtitleCue(long StartMs, long EndMs, string Text);

/// <summary>
/// Reads .srt, .vtt and .ass/.ssa files into timed lines. libVLC 3 draws only one subtitle track, so the
/// second (translation) subtitle and the line-by-line tools use these instead.
/// </summary>
public static class SubtitleFile
{
    public static readonly string[] Extensions = { ".srt", ".vtt", ".ass", ".ssa" };

    private static readonly string FallbackDir = Path.Combine(LibraryStore.AppDir, "subtitles");

    /// <summary>Subtitle files for a video: "video*.srt" etc. beside it, and downloads saved in app data.</summary>
    public static List<string> FindFor(string videoPath)
    {
        var name = Path.GetFileNameWithoutExtension(videoPath);
        var found = new List<string>();
        foreach (var dir in new[] { Path.GetDirectoryName(videoPath), FallbackDir })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                found.AddRange(Directory.EnumerateFiles(dir, name + "*")
                    .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The language in a name like "video.it.srt" or "video.Italian.forced.srt", or null.</summary>
    public static string? LanguageOf(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('.', '_', '-', ' ');
        foreach (var part in parts.Skip(1).Reverse())
            if (Languages.Normalize(part) is { } code) return code;
        return null;
    }

    /// <summary>"Italian (video.it.srt)" or just the file name.</summary>
    public static string Label(string file) =>
        LanguageOf(file) is { } code ? $"{Languages.DisplayName(code)} ({Path.GetFileName(file)})" : Path.GetFileName(file);

    /// <summary>Lines in time order; empty when the file can't be read.</summary>
    public static List<SubtitleCue> Load(string file)
    {
        string text;
        try { text = Decode(File.ReadAllBytes(file)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new List<SubtitleCue>(); }

        var ext = Path.GetExtension(file).ToLowerInvariant();
        var cues = ext is ".ass" or ".ssa" ? ParseAss(text) : ParseSrtOrVtt(text);
        return cues.Where(c => c.EndMs > c.StartMs && c.Text.Length > 0).OrderBy(c => c.StartMs).ToList();
    }

    /// <summary>The line showing at <paramref name="ms"/>, or -1.</summary>
    public static int IndexAt(IReadOnlyList<SubtitleCue> cues, long ms)
    {
        int lo = 0, hi = cues.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cues[mid].StartMs <= ms) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        // Overlapping lines: the latest one that started and hasn't ended.
        for (var i = found; i >= 0 && i > found - 4; i--)
            if (cues[i].EndMs > ms) return i;
        return -1;
    }

    /// <summary>The last line that started at or before <paramref name="ms"/> (showing or already over), or -1.</summary>
    public static int IndexBefore(IReadOnlyList<SubtitleCue> cues, long ms)
    {
        int lo = 0, hi = cues.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cues[mid].StartMs <= ms) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    private static string Decode(byte[] bytes)
    {
        // UTF-16 (either byte order) or UTF-8 with a byte order mark.
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF || bytes[0] == 0xEF && bytes[1] == 0xBB))
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            // Older subtitles are often in the Windows code page for their language.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }

    private static readonly Regex TimeLine = new(
        @"^\s*(?<s>(\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})\s*-->\s*(?<e>(\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})", RegexOptions.Compiled);

    private static IEnumerable<SubtitleCue> ParseSrtOrVtt(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var m = TimeLine.Match(lines[i]);
            if (!m.Success) continue;
            var body = new List<string>();
            while (++i < lines.Length && lines[i].Trim().Length > 0) body.Add(lines[i]);
            yield return new SubtitleCue(ParseTime(m.Groups["s"].Value), ParseTime(m.Groups["e"].Value), Clean(string.Join("\n", body)));
        }
    }

    private static IEnumerable<SubtitleCue> ParseAss(string text)
    {
        int start = 1, end = 2, textField = 9, fields = 10;
        var inEvents = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inEvents) continue;
            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                var names = line[7..].Split(',').Select(n => n.Trim().ToLowerInvariant()).ToList();
                start = names.IndexOf("start"); end = names.IndexOf("end"); textField = names.IndexOf("text"); fields = names.Count;
                continue;
            }
            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase) || start < 0 || end < 0 || textField < 0) continue;
            // The text is the last field and may itself contain commas.
            var parts = line[9..].Split(',', fields);
            if (parts.Length < fields) continue;
            var body = Regex.Replace(parts[textField], @"\{[^}]*\}", "").Replace(@"\N", "\n").Replace(@"\n", "\n").Replace(@"\h", " ");
            yield return new SubtitleCue(ParseTime(parts[start].Trim()), ParseTime(parts[end].Trim()), Clean(body));
        }
    }

    /// <summary>"01:02:03,456", "02:03.456" or ASS "1:02:03.45" in milliseconds.</summary>
    private static long ParseTime(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        double seconds = 0;
        foreach (var p in parts) seconds = seconds * 60 + double.Parse(p, CultureInfo.InvariantCulture);
        return (long)Math.Round(seconds * 1000);
    }

    /// <summary>Drops HTML-style tags, positioning codes and blank lines.</summary>
    private static string Clean(string text)
    {
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = Regex.Replace(text, @"\{\\[^}]*\}", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return string.Join("\n", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
    }
}
