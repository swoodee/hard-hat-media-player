using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>libVLC options that rescan the plugins and write a new cache (if the folder is writable).</summary>
    public static readonly string[] RebuildOptions = { "--reset-plugins-cache" };

    private static string Arch => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => "win-x64",
    };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "libvlc", Arch, "plugins", "plugins.dat");

    /// <summary>What the plugins looked like when the cache was written (see <see cref="Fingerprint"/>).</summary>
    private static string StampPath => FilePath + ".stamp";

    /// <summary>
    /// The cache records every plugin's size and date to the second, and libVLC opens any plugin whose date
    /// differs, without ever refreshing the file. Dates shift more often than you'd think: an installer or zip
    /// built in another time zone, unzip tools that reset them, Defender removing the cache. So the cache only
    /// counts as current when the plugins still match the fingerprint saved next to it.
    /// </summary>
    public static bool NeedsRebuild()
    {
        try { return !File.Exists(FilePath) || File.ReadAllText(StampPath) != Fingerprint(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>Saves the fingerprint after libVLC has written the cache. Read-only folders are left alone.</summary>
    public static void Stamp()
    {
        try { if (File.Exists(FilePath)) File.WriteAllText(StampPath, Fingerprint()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Every plugin's path, size and date (whole seconds, as libVLC compares them).</summary>
    private static string Fingerprint()
    {
        // A stable hash: HashCode is seeded differently in every process.
        var dir = Path.GetDirectoryName(FilePath)!;
        var list = new StringBuilder();
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(dll);
            list.Append(Path.GetRelativePath(dir, dll).ToLowerInvariant()).Append('|').Append(info.Length).Append('|')
                .Append(new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds()).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(list.ToString())));
    }
}
