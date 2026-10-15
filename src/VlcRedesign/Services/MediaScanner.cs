using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VlcRedesign.Services;

public static class MediaScanner
{
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg",
        ".ts", ".m2ts", ".mts", ".3gp", ".ogv", ".vob", ".divx", ".rmvb", ".asf",
    };

    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".oga", ".opus", ".wma", ".aiff",
        ".aif", ".ape", ".mka", ".alac", ".wv", ".mpc", ".ac3", ".dts",
    };

    private static readonly EnumerationOptions Shallow = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
    private static readonly EnumerationOptions Deep = new() { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };

    public static bool IsStream(string path) =>
        path.Contains("://", StringComparison.Ordinal) && !path.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    public static bool IsVideo(string path) => VideoExt.Contains(Path.GetExtension(path));
    public static bool IsAudio(string path) => AudioExt.Contains(Path.GetExtension(path));
    public static bool IsMedia(string path) => IsVideo(path) || IsAudio(path);

    public static MediaKind KindOf(string path) =>
        IsStream(path) ? MediaKind.Stream : IsAudio(path) ? MediaKind.Audio : MediaKind.Video;

    public static string DisplayName(string path) =>
        IsStream(path) ? path : Path.GetFileNameWithoutExtension(path);

    public static List<string> SubFolders(string dir)
    {
        try { return Directory.EnumerateDirectories(dir, "*", Shallow).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new List<string>(); }
    }

    public static List<string> MediaFiles(string dir) => Files(dir, Shallow, IsMedia);

    public static List<string> AudioFilesDeep(IEnumerable<string> roots) =>
        roots.Where(Directory.Exists).SelectMany(r => Files(r, Deep, IsAudio)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Expands dropped or opened paths: folders become their media files (recursively).</summary>
    public static List<string> Expand(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) result.AddRange(Files(p, Deep, IsMedia));
            else if (File.Exists(p) || IsStream(p)) result.Add(p);
        }
        return result;
    }

    private static List<string> Files(string dir, EnumerationOptions options, Func<string, bool> filter)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", options).Where(filter)
                .OrderBy(f => f, NaturalComparer.Instance).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }
}

/// <summary>Orders "Episode 2" before "Episode 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        if (a is null || b is null) return string.Compare(a, b, StringComparison.Ordinal);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
