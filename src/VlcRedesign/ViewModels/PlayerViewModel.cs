using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VlcRedesign.Services;

namespace VlcRedesign.ViewModels;

public enum RepeatMode { Off, All, One }

/// <summary>Wraps the libVLC media player: queue, transport, tracks and resume points.</summary>
public sealed partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly LibraryStore _store;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();
    private List<string> _queue = new();
    private int _index = -1;
    private HistoryEntry? _entry;
    private DateTime _lastSave;
    private DateTime _lastSnapshot;
    private bool _disposed;

    public LibVLC LibVLC { get; }
    public MediaPlayer Player { get; }

    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string? _artwork;
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private bool _hasVideo;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _position;
    [ObservableProperty] private string _timeText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private int _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private float _rate = 1f;
    [ObservableProperty] private bool _shuffle;
    [ObservableProperty] private RepeatMode _repeat;
    [ObservableProperty] private string? _sourceLabel;
    [ObservableProperty] private string? _error;

    public Func<PanelPage>? SourcePage { get; private set; }
    public bool IsSeeking { get; set; }
    public bool IsIdle => !HasVideo;
    public bool IsAudioView => HasMedia && !HasVideo;

    public event Action? NowPlayingChanged;
    public event Action? ThumbnailUpdated;
    public event Action? OpenRequested;

    public PlayerViewModel(LibraryStore store, Dispatcher ui)
    {
        _store = store;
        _ui = ui;
        LibVLC = new LibVLC("--no-video-title-show", "--no-snapshot-preview");
        Player = new MediaPlayer(LibVLC) { EnableHardwareDecoding = true };

        Player.Playing += (_, _) => _ui.BeginInvoke(() => { IsPlaying = true; Error = null; });
        Player.Paused += (_, _) => _ui.BeginInvoke(() => IsPlaying = false);
        Player.Stopped += (_, _) => _ui.BeginInvoke(() => IsPlaying = false);
        Player.EndReached += (_, _) => _ui.BeginInvoke(OnEnded);
        Player.EncounteredError += (_, _) => _ui.BeginInvoke(() => { IsPlaying = false; Error = "VLC couldn't play this media."; });
        Player.Vout += (_, e) => _ui.BeginInvoke(() => HasVideo = e.Count > 0);

        Volume = Math.Clamp(store.Data.Volume, 0, 125);
        Player.Volume = Volume;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), ui);
        _timer.Start();
    }

    partial void OnHasVideoChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsAudioView));
    }

    partial void OnHasMediaChanged(bool value) => OnPropertyChanged(nameof(IsAudioView));

    partial void OnVolumeChanged(int value)
    {
        Player.Volume = value;
        _store.Data.Volume = value;
        if (value > 0 && IsMuted) IsMuted = false;
    }

    partial void OnIsMutedChanged(bool value) => Player.Mute = value;
    partial void OnRateChanged(float value) => Player.SetRate(value);

    // ---- Queue ----

    public void PlayList(IList<string> items, int index, string? label = null, Func<PanelPage>? source = null)
    {
        if (items.Count == 0) return;
        SaveProgress();
        _queue = items.ToList();
        _index = Math.Clamp(index, 0, _queue.Count - 1);
        SourceLabel = label;
        SourcePage = source;
        PlayCurrent();
    }

    public void Enqueue(IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        if (!HasMedia) PlayList(list, 0, "Queue");
        else _queue.AddRange(list);
    }

    public void PlayNext(string item)
    {
        if (!HasMedia) PlayList(new[] { item }, 0, "Queue");
        else _queue.Insert(_index + 1, item);
    }

    private void PlayCurrent()
    {
        var path = _queue[_index];
        var kind = MediaScanner.KindOf(path);
        var meta = _store.MetaFor(path);
        var previous = _store.FindHistory(path);

        using var media = kind == MediaKind.Stream
            ? new Media(LibVLC, new Uri(path))
            : new Media(LibVLC, path, FromType.FromPath);
        if (kind != MediaKind.Stream && previous is { IsUnfinished: true })
            media.AddOption(FormattableString.Invariant($":start-time={previous.PositionMs / 1000.0:0.0}"));

        Error = null;
        HasVideo = false;
        Position = 0;
        TimeText = "0:00";
        DurationText = "0:00";
        CurrentPath = path;
        HasMedia = true;
        Title = meta?.Title ?? MediaScanner.DisplayName(path);
        Subtitle = kind switch
        {
            MediaKind.Stream => "Network stream",
            MediaKind.Audio => meta?.Artist ?? meta?.Album ?? FolderName(path),
            _ => FolderName(path),
        };
        Artwork = meta?.Artwork;
        _entry = _store.TouchHistory(path, kind, Title);
        _lastSave = DateTime.Now;
        _lastSnapshot = DateTime.Now;

        Player.Play(media);
        _store.Save();
        NowPlayingChanged?.Invoke();
    }

    private bool TryAdvance(int direction, bool wrap)
    {
        if (_queue.Count == 0) return false;
        if (Shuffle && direction > 0 && _queue.Count > 1)
        {
            int next;
            do next = _random.Next(_queue.Count); while (next == _index);
            _index = next;
            return true;
        }

        var n = _index + direction;
        if (n >= _queue.Count) n = wrap ? 0 : -1;
        else if (n < 0) n = wrap ? _queue.Count - 1 : -1;
        if (n < 0) return false;
        _index = n;
        return true;
    }

    private void OnEnded()
    {
        if (_entry != null && _entry.DurationMs > 0) _entry.PositionMs = _entry.DurationMs;
        IsPlaying = false;
        Position = 1;

        if (Repeat == RepeatMode.One) PlayCurrent();
        else if (TryAdvance(1, Repeat == RepeatMode.All)) PlayCurrent();
        else _store.Save();
    }

    // ---- Transport ----

    [RelayCommand]
    public void TogglePlay()
    {
        if (!HasMedia)
        {
            if (_queue.Count > 0) PlayCurrent();
            else OpenRequested?.Invoke();
            return;
        }

        if (Player.State is VLCState.Ended or VLCState.Stopped or VLCState.Error) PlayCurrent();
        else Player.SetPause(Player.IsPlaying);
    }

    [RelayCommand]
    public void Next()
    {
        if (_queue.Count == 0) return;
        SaveProgress();
        if (TryAdvance(1, Repeat != RepeatMode.Off)) PlayCurrent();
    }

    [RelayCommand]
    public void Previous()
    {
        if (_queue.Count == 0) return;
        if (Player.Time > 3000)
        {
            Player.Time = 0;
            return;
        }
        SaveProgress();
        if (TryAdvance(-1, Repeat == RepeatMode.All)) PlayCurrent();
        else Player.Time = 0;
    }

    [RelayCommand]
    public void Stop()
    {
        SaveProgress();
        Player.Stop();
        HasMedia = false;
        HasVideo = false;
        IsPlaying = false;
        CurrentPath = null;
        Title = "";
        Position = 0;
        NowPlayingChanged?.Invoke();
    }

    [RelayCommand] private void ToggleMute() => IsMuted = !IsMuted;
    [RelayCommand] private void ToggleShuffle() => Shuffle = !Shuffle;
    [RelayCommand] private void CycleRepeat() => Repeat = (RepeatMode)(((int)Repeat + 1) % 3);

    public void Seek(double fraction)
    {
        if (!HasMedia || !Player.IsSeekable) return;
        fraction = Math.Clamp(fraction, 0, 1);
        if (Player.State == VLCState.Ended)
        {
            PlayCurrent();
            return;
        }
        Player.Position = (float)fraction;
        Position = fraction;
    }

    public void SeekBy(long ms)
    {
        if (!HasMedia || !Player.IsSeekable) return;
        var length = Player.Length;
        Player.Time = Math.Clamp(Player.Time + ms, 0, length > 0 ? length - 500 : long.MaxValue);
    }

    public void ChangeVolume(int delta) => Volume = Math.Clamp(Volume + delta, 0, 125);

    public string FormatAt(double fraction) => Format((long)(Player.Length * fraction));

    // ---- Tracks ----

    public IReadOnlyList<(int Id, string Name)> AudioTracks =>
        Player.AudioTrackDescription.Select(t => (t.Id, t.Name ?? $"Track {t.Id}")).ToList();

    public IReadOnlyList<(int Id, string Name)> SubtitleTracks =>
        Player.SpuDescription.Select(t => (t.Id, t.Name ?? $"Track {t.Id}")).ToList();

    public int AudioTrack => Player.AudioTrack;
    public int SubtitleTrack => Player.Spu;

    public void SetAudioTrack(int id) => Player.SetAudioTrack(id);
    public void SetSubtitleTrack(int id) => Player.SetSpu(id);

    public void AddSubtitleFile(string file) =>
        Player.AddSlave(MediaSlaveType.Subtitle, new Uri(file).AbsoluteUri, true);

    // ---- Progress, resume points and thumbnails ----

    private void Tick()
    {
        if (!HasMedia || _disposed) return;
        var length = Player.Length;
        var time = Player.Time;

        if (!IsSeeking)
        {
            if (length > 0) Position = Math.Clamp(time / (double)length, 0, 1);
            TimeText = Format(Math.Max(0, time));
        }
        DurationText = length > 0 ? Format(length) : MediaScanner.IsStream(CurrentPath ?? "") ? "Live" : "0:00";

        if (_entry != null && IsPlaying && time >= 0)
        {
            _entry.PositionMs = time;
            if (length > 0) _entry.DurationMs = length;
        }

        var now = DateTime.Now;
        if ((now - _lastSave).TotalSeconds > 15)
        {
            _lastSave = now;
            _store.Save();
        }

        if (HasVideo && IsPlaying && _entry is { Kind: MediaKind.Video } && time > 8000 && (now - _lastSnapshot).TotalSeconds > 45)
            TakeThumbnail(_entry, now);
    }

    private void TakeThumbnail(HistoryEntry entry, DateTime now)
    {
        _lastSnapshot = now;
        var file = LibraryStore.ThumbnailPathFor(entry.Path);
        Directory.CreateDirectory(LibraryStore.ThumbDir);
        Task.Run(() =>
        {
            if (_disposed || !Player.TakeSnapshot(0, file, 320, 0)) return;
            _ui.BeginInvoke(() =>
            {
                entry.Thumbnail = file;
                ThumbnailUpdated?.Invoke();
            });
        });
    }

    public void SaveProgress()
    {
        if (_entry != null && HasMedia && Player.State is VLCState.Playing or VLCState.Paused)
        {
            var time = Player.Time;
            var length = Player.Length;
            if (time > 0) _entry.PositionMs = time;
            if (length > 0) _entry.DurationMs = length;
        }
        _store.Save();
    }

    public static string Format(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static string FolderName(string path) => Path.GetFileName(Path.GetDirectoryName(path)) ?? "";

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        SaveProgress();
        _disposed = true;
        var player = Player;
        Task.Run(player.Stop).Wait(TimeSpan.FromSeconds(2));
        player.Dispose();
        LibVLC.Dispose();
    }
}
