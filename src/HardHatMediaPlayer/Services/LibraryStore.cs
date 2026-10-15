using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardHatMediaPlayer.Services;

public enum MediaKind { Video, Audio, Stream }

public sealed class HistoryEntry
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public MediaKind Kind { get; set; }
    public DateTime LastPlayed { get; set; }
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public string? Thumbnail { get; set; }

    /// <summary>Started but not finished: worth offering under "Continue Watching".</summary>
    [JsonIgnore]
    public bool IsUnfinished => DurationMs > 0 && PositionMs > 5000 && PositionMs < DurationMs * 0.95;
}

public sealed class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Items { get; set; } = new();
    public int Opens { get; set; }
    public DateTime LastOpened { get; set; }
}

public sealed class AudioMeta
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public int Track { get; set; }
    public string? Artwork { get; set; }
    public long DurationMs { get; set; }
    public long Stamp { get; set; }
}

public sealed class LibraryData
{
    public bool Initialized { get; set; }
    public List<string> VideoFolders { get; set; } = new();
    public List<string> MusicFolders { get; set; } = new();
    public List<Playlist> Playlists { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();
    public Dictionary<string, int> FolderVisits { get; set; } = new();
    public Dictionary<string, AudioMeta> Meta { get; set; } = new();
    public int Volume { get; set; } = 80;
    public bool PanelOpen { get; set; } = true;
    public double PanelWidth { get; set; } = 280;
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public SubtitleSettings Subtitles { get; set; } = new();
}

/// <summary>Library, playlists and history, persisted as JSON in %APPDATA%\HardHatMediaPlayer.</summary>
public sealed class LibraryStore
{
    /// <summary>%APPDATA%\HardHatMediaPlayer, or the folder in HARDHAT_DATA (portable installs, tests).</summary>
    public static readonly string AppDir =
        Environment.GetEnvironmentVariable("HARDHAT_DATA") is { Length: > 0 } custom
            ? custom
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HardHatMediaPlayer");

    /// <summary>Where the library lived while the app was called "VLC Redesign".</summary>
    private static readonly string LegacyAppDir =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VlcRedesign");
    public static readonly string ThumbDir = System.IO.Path.Combine(AppDir, "thumbs");

    private const int MaxHistory = 500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string FilePath => System.IO.Path.Combine(AppDir, "library.json");

    public LibraryData Data { get; private set; } = new();

    public void Load()
    {
        MigrateLegacyData();
        try
        {
            if (File.Exists(FilePath))
                Data = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(FilePath)) ?? new LibraryData();
        }
        catch (Exception)
        {
            // A corrupt file shouldn't stop the player from starting; keep a copy for inspection.
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch (IOException) { }
            Data = new LibraryData();
        }

        if (!Data.Initialized)
        {
            Data.Initialized = true;
            AddIfExists(Data.VideoFolders, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            AddIfExists(Data.MusicFolders, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        }
    }

    /// <summary>
    /// First run after the rename: copy the old library folder across (the old one is left in place as a
    /// backup) and point saved thumbnail paths at the new folder.
    /// </summary>
    private static void MigrateLegacyData()
    {
        if (Environment.GetEnvironmentVariable("HARDHAT_DATA") is { Length: > 0 }) return;
        if (File.Exists(FilePath) || !File.Exists(System.IO.Path.Combine(LegacyAppDir, "library.json"))) return;
        try
        {
            foreach (var source in Directory.EnumerateFiles(LegacyAppDir, "*", SearchOption.AllDirectories))
            {
                var target = System.IO.Path.Combine(AppDir, System.IO.Path.GetRelativePath(LegacyAppDir, source));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
            }
            // Thumbnails are stored with absolute paths; JSON escapes the backslashes.
            var json = File.ReadAllText(FilePath);
            File.WriteAllText(FilePath, json.Replace(JsonEscape(LegacyAppDir), JsonEscape(AppDir)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the app starts with a fresh library.
        }
    }

    private static string JsonEscape(string path) => path.Replace("\\", "\\\\");

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, JsonOptions));
            File.Move(tmp, FilePath, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public HistoryEntry? FindHistory(string path) =>
        Data.History.FirstOrDefault(h => string.Equals(h.Path, path, StringComparison.OrdinalIgnoreCase));

    public HistoryEntry TouchHistory(string path, MediaKind kind, string title)
    {
        var entry = FindHistory(path);
        if (entry is null)
        {
            entry = new HistoryEntry { Path = path, Kind = kind };
            Data.History.Insert(0, entry);
            if (Data.History.Count > MaxHistory) Data.History.RemoveRange(MaxHistory, Data.History.Count - MaxHistory);
        }
        else
        {
            Data.History.Remove(entry);
            Data.History.Insert(0, entry);
        }
        entry.Title = title;
        entry.LastPlayed = DateTime.Now;
        return entry;
    }

    public void VisitFolder(string dir)
    {
        Data.FolderVisits.TryGetValue(dir, out var n);
        Data.FolderVisits[dir] = n + 1;
    }

    public AudioMeta? MetaFor(string path) => Data.Meta.TryGetValue(path, out var m) ? m : null;

    public static string ThumbnailPathFor(string mediaPath)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(mediaPath.ToLowerInvariant())));
        return System.IO.Path.Combine(ThumbDir, hash + ".png");
    }

    private static void AddIfExists(List<string> list, string dir)
    {
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) list.Add(dir);
    }
}
