using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace HardHatMediaPlayer.Services;

/// <summary>
/// libVLC's plugin cache (libvlc\&lt;arch&gt;\plugins\plugins.dat). Without it libVLC opens all ~320 plugin DLLs
/// every time it starts, and Microsoft Defender scans each one, which can hold up the first file for many
/// seconds (the "33-second MP3" problem VLC hit in 2026). With it, libVLC reads one file and loads only the
/// plugins a file actually needs.
/// </summary>
public static class PluginCache
{
    /// <summary>Command-line switch that writes the cache and exits; release builds run it so the cache ships.</summary>
    public const string GenerateSwitch = "--generate-plugin-cache";

    private static string Arch => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => "win-x64",
    };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "libvlc", Arch, "plugins", "plugins.dat");

    public static bool Exists => File.Exists(FilePath);

    /// <summary>
    /// The cache records each plugin's size and date; a plugin with a different date is opened anyway, and
    /// libVLC never refreshes the file by itself. So it's only useful when no plugin is newer than it
    /// (unzip tools that reset dates, or a dev build that updated libVLC, make every entry stale).
    /// </summary>
    private static bool IsCurrent()
    {
        var cache = new FileInfo(FilePath);
        if (!cache.Exists) return false;
        try
        {
            return !Directory.EnumerateFiles(cache.DirectoryName!, "*.dll", SearchOption.AllDirectories)
                .Any(dll => File.GetLastWriteTimeUtc(dll) > cache.LastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>
    /// libVLC options that rebuild the cache when it's missing or stale (first run of a dev build, or after
    /// Defender removed it). libVLC writes it after scanning, if the folder is writable; otherwise nothing changes.
    /// </summary>
    public static string[] Options => IsCurrent() ? Array.Empty<string>() : new[] { "--reset-plugins-cache" };
}
