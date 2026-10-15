using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace HardHatMediaPlayer.Services;

/// <summary>Reads audio tags (title, artist, album, genre, artwork) with libVLC in the background.</summary>
public sealed class MetadataService
{
    private readonly LibVLC _vlc;
    private readonly LibraryStore _store;
    private readonly Dispatcher _ui;
    private CancellationTokenSource? _cts;

    public event Action? Updated;

    public MetadataService(LibVLC vlc, LibraryStore store, Dispatcher ui)
    {
        _vlc = vlc;
        _store = store;
        _ui = ui;
    }

    public void Scan(IEnumerable<string> audioFiles)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var pending = audioFiles.Where(NeedsParse).ToList();
        if (pending.Count == 0) return;

        _ = Task.Run(async () =>
        {
            var batch = new List<(string, AudioMeta)>();
            foreach (var path in pending)
            {
                if (cts.IsCancellationRequested) return;
                var meta = await ParseAsync(path);
                if (meta != null) batch.Add((path, meta));
                if (batch.Count >= 25) Flush(batch);
            }
            Flush(batch);
            _ = _ui.BeginInvoke(_store.Save);
        });
    }

    private void Flush(List<(string Path, AudioMeta Meta)> batch)
    {
        if (batch.Count == 0) return;
        var items = batch.ToArray();
        batch.Clear();
        _ui.BeginInvoke(() =>
        {
            foreach (var (path, meta) in items) _store.Data.Meta[path] = meta;
            Updated?.Invoke();
        });
    }

    private bool NeedsParse(string path)
    {
        var meta = _store.MetaFor(path);
        return meta is null || meta.Stamp != Stamp(path);
    }

    private static long Stamp(string path)
    {
        try { return File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    private async Task<AudioMeta?> ParseAsync(string path)
    {
        try
        {
            using var media = new Media(_vlc, path, FromType.FromPath);
            await media.Parse(MediaParseOptions.ParseLocal | MediaParseOptions.FetchLocal, 5000);
            int.TryParse(media.Meta(MetadataType.TrackNumber)?.Split('/')[0], out var track);
            return new AudioMeta
            {
                Title = Clean(media.Meta(MetadataType.Title)),
                Artist = Clean(media.Meta(MetadataType.Artist)) ?? Clean(media.Meta(MetadataType.AlbumArtist)),
                Album = Clean(media.Meta(MetadataType.Album)),
                Genre = Clean(media.Meta(MetadataType.Genre)),
                Track = track,
                Artwork = Clean(media.Meta(MetadataType.ArtworkURL)),
                DurationMs = Math.Max(0, media.Duration),
                Stamp = Stamp(path),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
