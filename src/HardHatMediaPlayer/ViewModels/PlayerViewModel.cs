using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using HardHatMediaPlayer.Services;

namespace HardHatMediaPlayer.ViewModels;

public enum RepeatMode { Off, All, One }

/// <summary>Wraps the libVLC media player: queue, transport, tracks and resume points.</summary>
public sealed partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly LibraryStore _store;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _messageTimer;
    private readonly Random _random = new();
    private int _index = -1;
    /// <summary>The playing item was removed from the queue; <see cref="_index"/> is then the item that followed it.</summary>
    private bool _detached;
    private HistoryEntry? _entry;
    private DateTime _lastSave;
    private DateTime _lastSnapshot;
    private DateTime _seekSettles;
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
    [ObservableProperty] private bool _isEnded;
    [ObservableProperty] private MediaKind _kind;

    /// <summary>Positive: subtitles appear later. Applied to libVLC in microseconds.</summary>
    [ObservableProperty] private long _subtitleDelayMs;
    [ObservableProperty] private long _audioDelayMs;

    public Func<PanelPage>? SourcePage { get; private set; }
    public bool IsSeeking { get; set; }

    /// <summary>The library-coloured backdrop: shown unless video is on screen, or a video is still opening (black then).</summary>
    public bool ShowBackdrop => !HasVideo && !(HasMedia && Kind == MediaKind.Video && !IsEnded && Error is null);

    /// <summary>The "now playing" card: audio and streams without video, or a video that ended or failed.</summary>
    public bool ShowNowPlayingCard => HasMedia && ShowBackdrop;

    public bool IsVideoCard => Kind == MediaKind.Video;

    public event Action? NowPlayingChanged;
    public event Action? ThumbnailUpdated;
    public event Action? OpenRequested;
    public event Action? PlaybackStopped;

    /// <summary>Raised once per item, when it actually starts playing.</summary>
    public event Action<string>? MediaStarted;
    private bool _startedRaised;

    public PlayerViewModel(LibraryStore store, Dispatcher ui)
    {
        _store = store;
        _ui = ui;
        // --no-osd: libVLC would otherwise print messages on the video, e.g. the file path of every
        // thumbnail snapshot. The app shows its own feedback; subtitles are unaffected.
        LibVLC = new LibVLC("--no-video-title-show", "--no-snapshot-preview", "--no-osd");
        Player = new MediaPlayer(LibVLC) { EnableHardwareDecoding = true };

        Player.Playing += (_, _) => _ui.BeginInvoke(OnPlaying);
        Player.Paused += (_, _) => _ui.BeginInvoke(() =>
        {
            IsPlaying = false;
            SaveProgress();
            PlaybackStopped?.Invoke();
        });
        Player.Stopped += (_, _) => _ui.BeginInvoke(() => IsPlaying = false);
        Player.EndReached += (_, _) => _ui.BeginInvoke(OnEnded);
        Player.EncounteredError += (_, _) => _ui.BeginInvoke(OnError);
        Player.Vout += (_, e) => _ui.BeginInvoke(() => HasVideo = e.Count > 0);
        // Tracks keep arriving after playback starts (embedded ones, then "video.lang.srt" next to it).
        Player.ESAdded += (_, e) =>
        {
            if (e.Type is TrackType.Audio or TrackType.Text) _ui.BeginInvoke(ApplyTrackPreferences);
        };

        _volume = Math.Clamp(store.Data.Volume, 0, 125);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), ui);
        _timer.Start();
        _messageTimer = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Normal, (_, _) =>
        {
            _messageTimer!.Stop();
            Error = null;
        }, ui);
        _messageTimer.Stop();
    }

    partial void OnHasVideoChanged(bool value) => RaiseCoverState();
    partial void OnHasMediaChanged(bool value) => RaiseCoverState();
    partial void OnIsEndedChanged(bool value) => RaiseCoverState();
    partial void OnErrorChanged(string? value) => RaiseCoverState();
    partial void OnKindChanged(MediaKind value) => RaiseCoverState();

    private void RaiseCoverState()
    {
        OnPropertyChanged(nameof(ShowBackdrop));
        OnPropertyChanged(nameof(ShowNowPlayingCard));
        OnPropertyChanged(nameof(IsVideoCard));
    }

    /// <summary>Shows a short message over the player that clears itself.</summary>
    public void ShowMessage(string message)
    {
        Error = message;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void OnPlaying()
    {
        IsPlaying = true;
        IsEnded = false;
        // libVLC ignores volume, mute and rate until an audio output exists, so apply them once playback starts.
        Player.Volume = Volume;
        Player.Mute = IsMuted;
        if (Math.Abs(Player.Rate - Rate) > 0.001f) Player.SetRate(Rate);
        ReadPlayingMetadata();
        if (!_startedRaised && CurrentPath != null)
        {
            _startedRaised = true;
            ApplyPreset();
            MediaStarted?.Invoke(CurrentPath);
        }
    }

    /// <summary>Files opened outside the music library and streams only get their tags once libVLC has opened them.</summary>
    private void ReadPlayingMetadata()
    {
        if (Kind == MediaKind.Video || _store.MetaFor(CurrentPath ?? "") != null) return;
        using var media = Player.Media;
        if (media is null) return;

        var title = media.Meta(MetadataType.NowPlaying) is { Length: > 0 } np && Kind == MediaKind.Stream ? np : media.Meta(MetadataType.Title);
        var artist = media.Meta(MetadataType.Artist);
        var artwork = media.Meta(MetadataType.ArtworkURL);
        // Untagged files report their file name as the title; keep the cleaner display name then.
        var fileName = Kind == MediaKind.Stream ? null : Path.GetFileName(CurrentPath);
        if (!string.IsNullOrWhiteSpace(title) && title != CurrentPath && !string.Equals(title, fileName, StringComparison.OrdinalIgnoreCase))
        {
            Title = title;
            if (_entry != null) _entry.Title = title;
            if (CurrentItem is { } item) item.Title = title;
        }
        if (!string.IsNullOrWhiteSpace(artist)) Subtitle = artist;
        if (!string.IsNullOrWhiteSpace(artwork)) Artwork = artwork;
    }

    private void OnError()
    {
        IsPlaying = false;
        var failed = Title;
        if (TryAdvance(1, wrap: false))
        {
            PlayCurrent();
            ShowMessage($"Skipped “{failed}” — it couldn't be played.");
        }
        else
        {
            _messageTimer.Stop();
            Error = $"Couldn't play “{failed}”.";
        }
    }

    partial void OnVolumeChanged(int value)
    {
        Player.Volume = value;
        _store.Data.Volume = value;
        if (value > 0 && IsMuted) IsMuted = false;
        ShowOsd($"Volume {value}%", "\u2191 \u2193 or mouse wheel", VolumeIcon(value), value / 125.0);
    }

    partial void OnIsMutedChanged(bool value)
    {
        Player.Mute = value;
        ShowOsd(value ? "Muted" : $"Sound on \u00b7 {Volume}%", "M", value ? "Mute" : VolumeIcon(Volume), value ? 0 : Volume / 125.0);
    }

    partial void OnRateChanged(float value)
    {
        Player.SetRate(value);
        ShowOsd(Math.Abs(value - 1f) < 0.001f ? "Normal speed" : $"Speed {value:0.##}\u00d7", "[ slower  \u00b7  ] faster  \u00b7  = normal", "Speed");
    }

    partial void OnShuffleChanged(bool value) => ShowOsd(value ? "Shuffle on" : "Shuffle off", null, "Shuffle");

    partial void OnRepeatChanged(RepeatMode value) =>
        ShowOsd(value switch { RepeatMode.All => "Repeat all", RepeatMode.One => "Repeat this one", _ => "Repeat off" }, null, "Repeat");

    private static string VolumeIcon(int volume) => volume == 0 ? "Mute" : volume < 50 ? "VolumeLow" : "Volume";

    // ---- On-screen feedback ----

    /// <summary>Short on-screen message: what just happened, the shortcut for it, an icon and an optional level bar.</summary>
    public event Action<OsdMessage>? Osd;

    /// <summary>Play/pause was toggled by the user; the view flashes a big icon. True = now playing.</summary>
    public event Action<bool>? PlayToggled;

    public void ShowOsd(string text, string? hint = null, string? icon = null, double? level = null) =>
        Osd?.Invoke(new OsdMessage(text, hint, icon, level));

    private static readonly float[] Speeds = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 3f, 4f };

    public void Faster() => Rate = Speeds.FirstOrDefault(s => s > Rate + 0.001f, Speeds[^1]);
    public void Slower() => Rate = Speeds.LastOrDefault(s => s < Rate - 0.001f, Speeds[0]);
    public void NormalSpeed() => Rate = 1f;

    public void CycleSubtitles()
    {
        var tracks = SubtitleTracks.ToList();
        if (tracks.Count == 0)
        {
            ShowOsd("No subtitles", "Find some: subtitles button \u2192 Find subtitles online", "Subtitles");
            return;
        }
        var i = tracks.FindIndex(t => t.Id == SubtitleTrack);
        SetSubtitleTrack(tracks[(i + 1) % tracks.Count].Id);
    }

    public void CycleAudioTracks()
    {
        var tracks = AudioTracks.Where(t => t.Id != -1).ToList();
        if (tracks.Count < 2)
        {
            ShowOsd(tracks.Count == 1 ? $"Audio: {tracks[0].Name}" : "No audio tracks", "Only one track", "Audio");
            return;
        }
        var i = tracks.FindIndex(t => t.Id == AudioTrack);
        SetAudioTrack(tracks[(i + 1) % tracks.Count].Id);
    }

    // ---- Queue ----

    /// <summary>What plays next, in order. Separate from playlists, which are saved collections.</summary>
    public QueueCollection Queue { get; } = new();

    public string QueueSummary
    {
        get
        {
            var count = Queue.Count == 1 ? "1 item" : $"{Queue.Count} items";
            var total = Queue.Sum(q => q.DurationMs);
            return total > 0 ? $"{count} · {Format(total)}" : count;
        }
    }

    private QueueItem NewQueueItem(string path)
    {
        var meta = _store.MetaFor(path);
        var history = _store.FindHistory(path);
        var title = meta?.Title
            ?? (MediaScanner.IsStream(path) && history?.Title is { Length: > 0 } t ? t : MediaScanner.DisplayName(path));
        var item = new QueueItem(path, title, meta?.DurationMs ?? history?.DurationMs ?? 0);
        item.Play = () => PlayAt(item);
        return item;
    }

    private void QueueChanged()
    {
        for (var i = 0; i < Queue.Count; i++) Queue[i].IsCurrent = !_detached && HasMedia && i == _index;
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(HasQueue));
    }

    public bool HasQueue => Queue.Count > 0;

    public void PlayList(IList<string> items, int index, string? label = null, Func<PanelPage>? source = null)
    {
        if (items.Count == 0) return;
        SaveProgress();
        Queue.ReplaceAll(items.Select(NewQueueItem));
        _index = Math.Clamp(index, 0, Queue.Count - 1);
        SourceLabel = label;
        SourcePage = source;
        PlayCurrent();
    }

    /// <summary>Adds to the end of the queue. Starts playing when nothing is queued, or when the queue had already finished.</summary>
    public void Enqueue(IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        if (!HasMedia && Queue.Count == 0)
        {
            PlayList(list, 0, "Queue");
            return;
        }
        var finished = IsEnded && NextIndex(wrap: false) < 0;
        var first = Queue.Count;
        foreach (var path in list) Queue.Add(NewQueueItem(path));
        QueueChanged();
        ShowOsd(list.Count == 1 ? $"Added “{Queue[first].Title}” to the queue" : $"Added {list.Count} items to the queue",
            "Q shows the queue", "Queue");
        if (finished)
        {
            _index = first;
            PlayCurrent();
        }
    }

    public void PlayNext(string path)
    {
        if (!HasMedia && Queue.Count == 0)
        {
            PlayList(new[] { path }, 0, "Queue");
            return;
        }
        var item = NewQueueItem(path);
        var slot = _detached ? _index : _index + 1;
        Queue.Insert(slot, item);
        QueueChanged();
        ShowOsd($"Playing next: {item.Title}", "Q shows the queue", "Queue");
    }

    /// <summary>Plays a queued item now, keeping the rest of the queue as it is.</summary>
    public void PlayAt(QueueItem item)
    {
        var i = Queue.IndexOf(item);
        if (i < 0) return;
        SaveProgress();
        _index = i;
        PlayCurrent();
    }

    /// <summary>Moves a queued item right after the one playing.</summary>
    public void PlayNext(QueueItem item)
    {
        var from = Queue.IndexOf(item);
        if (from < 0 || (!_detached && from == _index)) return;
        // Target index after the item has been taken out of the list.
        var to = _detached ? (from < _index ? _index - 1 : _index) : (from < _index ? _index : _index + 1);
        MoveInQueue(from, to);
        ShowOsd($"Playing next: {item.Title}", null, "Queue");
    }

    /// <summary>Moves an item; <paramref name="to"/> is its index once moved (as <see cref="ObservableCollection{T}.Move"/>).</summary>
    public void MoveInQueue(int from, int to)
    {
        if (from < 0 || from >= Queue.Count) return;
        to = Math.Clamp(to, 0, Queue.Count - 1);
        if (from == to) return;
        Queue.Move(from, to);

        if (!_detached && from == _index) _index = to;
        else if (_index >= 0)
        {
            // The playing item (or, when detached, the gap it left) shifts as items cross it.
            var g = _index;
            if (from < g) g--;
            if (_detached ? to < g : to <= g) g++;
            _index = g;
        }
        QueueChanged();
    }

    /// <summary>Removes an item. Removing the one playing lets it finish; the queue then continues with the item after it.</summary>
    public void RemoveFromQueue(QueueItem item)
    {
        var i = Queue.IndexOf(item);
        if (i < 0) return;
        Queue.RemoveAt(i);

        if (i < _index) _index--;
        else if (i == _index && !_detached)
        {
            if (HasMedia && !IsEnded) _detached = true;
            else _index = Math.Min(i, Queue.Count - 1);
        }
        if (Queue.Count == 0 && !_detached) _index = -1;
        QueueChanged();
    }

    /// <summary>Empties the queue except for the item playing.</summary>
    public void ClearQueue()
    {
        if (Queue.Count == 0) return;
        var keep = HasMedia && !_detached && _index >= 0 && _index < Queue.Count ? Queue[_index] : null;
        Queue.ReplaceAll(keep is null ? Array.Empty<QueueItem>() : new[] { keep });
        _index = keep is null ? (HasMedia ? 0 : -1) : 0;
        if (keep is null && HasMedia) _detached = true;
        QueueChanged();
        ShowOsd("Queue cleared", null, "Queue");
    }

    /// <param name="startMs">Where to start; by default the saved resume point, if any.</param>
    private void PlayCurrent(long? startMs = null)
    {
        _detached = false;
        _index = Math.Clamp(_index, 0, Queue.Count - 1);
        var path = Queue[_index].Path;
        var kind = MediaScanner.KindOf(path);
        var meta = _store.MetaFor(path);
        var previous = _store.FindHistory(path);

        using var media = kind == MediaKind.Stream
            ? new Media(LibVLC, new Uri(path))
            : new Media(LibVLC, path, FromType.FromPath);
        startMs ??= kind != MediaKind.Stream && previous is { IsUnfinished: true } ? previous.PositionMs : null;
        if (startMs > 0)
            media.AddOption(FormattableString.Invariant($":start-time={startMs.Value / 1000.0:0.0}"));

        _messageTimer.Stop();
        Error = null;
        IsEnded = false;
        _startedRaised = false;
        _tracksSettleBy = DateTime.MaxValue;
        _tracksChosenByUser = false;
        _presetAnnounced = false;
        SetDelaysQuietly(0, 0);
        Kind = kind;
        HasVideo = false;
        Position = 0;
        TimeText = "0:00";
        DurationText = "0:00";
        CurrentPath = path;
        HasMedia = true;
        QueueChanged();
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
        _lastSnapshot = DateTime.MinValue;

        Player.Play(media);
        _store.Save();
        NowPlayingChanged?.Invoke();
    }

    /// <summary>The queued item playing, unless it was removed from the queue.</summary>
    private QueueItem? CurrentItem => HasMedia && !_detached && _index >= 0 && _index < Queue.Count ? Queue[_index] : null;

    /// <summary>The index the queue goes to next (or back to), or -1 at the end.</summary>
    private int NextIndex(bool wrap, int direction = 1)
    {
        if (Queue.Count == 0) return -1;
        // Detached: _index is already the item that followed the removed one.
        var n = _detached && direction > 0 ? _index : _index + direction;
        if (n >= Queue.Count) n = wrap ? 0 : -1;
        else if (n < 0) n = wrap ? Queue.Count - 1 : -1;
        return n;
    }

    private bool TryAdvance(int direction, bool wrap)
    {
        if (Queue.Count == 0) return false;
        if (Shuffle && direction > 0 && Queue.Count > 1)
        {
            int next;
            do next = _random.Next(Queue.Count); while (next == _index && !_detached);
            _index = next;
            return true;
        }

        var n = NextIndex(wrap, direction);
        if (n < 0) return false;
        _index = n;
        return true;
    }

    private void OnEnded()
    {
        if (_entry != null && _entry.DurationMs > 0) _entry.PositionMs = _entry.DurationMs;
        IsPlaying = false;
        Position = 1;
        TimeText = DurationText;

        if (Repeat == RepeatMode.One && !_detached) PlayCurrent();
        else if (TryAdvance(1, Repeat == RepeatMode.All)) PlayCurrent();
        else
        {
            IsEnded = true;
            _store.Save();
            PlaybackStopped?.Invoke();
        }
    }

    // ---- Transport ----

    [RelayCommand]
    public void TogglePlay()
    {
        if (!HasMedia)
        {
            if (Queue.Count > 0) PlayCurrent();
            else OpenRequested?.Invoke();
            return;
        }

        if (Player.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
        {
            PlayCurrent();
            PlayToggled?.Invoke(true);
        }
        else
        {
            var pausing = Player.IsPlaying;
            Player.SetPause(pausing);
            PlayToggled?.Invoke(!pausing);
        }
    }

    [RelayCommand]
    public void Next()
    {
        if (Queue.Count == 0) return;
        SaveProgress();
        if (TryAdvance(1, Repeat != RepeatMode.Off))
        {
            PlayCurrent();
            ShowOsd(Title, "N next  \u00b7  P previous", "Next");
        }
        else ShowOsd("End of the queue", null, "Next");
    }

    [RelayCommand]
    public void Previous()
    {
        if (Queue.Count == 0) return;
        if (Player.Time > 3000 && !IsEnded)
        {
            Player.Time = 0;
            return;
        }
        SaveProgress();
        // At the start of the queue (and not repeating) this restarts the current item.
        TryAdvance(-1, Repeat == RepeatMode.All);
        PlayCurrent(0);
        ShowOsd(Title, "N next  \u00b7  P previous", "Prev");
    }

    [RelayCommand]
    public void Stop()
    {
        if (!HasMedia) return;
        SaveProgress();
        Player.Stop();
        HasMedia = false;
        HasVideo = false;
        IsPlaying = false;
        IsEnded = false;
        CurrentPath = null;
        Title = "";
        Subtitle = "";
        Artwork = null;
        Position = 0;
        _messageTimer.Stop();
        Error = null;
        QueueChanged();
        NowPlayingChanged?.Invoke();
        PlaybackStopped?.Invoke();
    }

    [RelayCommand] private void ToggleMute() => IsMuted = !IsMuted;
    [RelayCommand] private void ToggleShuffle() => Shuffle = !Shuffle;
    [RelayCommand] private void CycleRepeat() => Repeat = (RepeatMode)(((int)Repeat + 1) % 3);

    public void Seek(double fraction)
    {
        if (!HasMedia) return;
        fraction = Math.Clamp(fraction, 0, 1);
        // Checked before IsSeekable: once a file has ended libVLC reports it as not seekable.
        if (IsEnded || Player.State is VLCState.Ended or VLCState.Stopped)
        {
            // Seeking after the end restarts the item at the chosen point (if it's still queued).
            if (_detached) return;
            var length = _entry?.DurationMs ?? 0;
            PlayCurrent((long)(length * fraction));
            return;
        }
        if (!Player.IsSeekable)
        {
            OnPropertyChanged(nameof(Position)); // live streams: put the bar back where playback really is
            return;
        }
        Player.Position = (float)fraction;
        Position = fraction;
        TimeText = FormatAt(fraction);
        ShowOsd($"{TimeText} / {DurationText}", "\u2190 \u2192 jump 10 s  \u00b7  Shift for 3 s", "Seek", fraction);
        // libVLC seeks asynchronously; don't let the next ticks pull the bar back to the old time.
        _seekSettles = DateTime.Now.AddMilliseconds(700);
    }

    public void SeekBy(long ms)
    {
        if (!HasMedia || !Player.IsSeekable) return;
        var length = Player.Length;
        var target = Math.Clamp(Player.Time + ms, 0, length > 0 ? length - 500 : long.MaxValue);
        Player.Time = target;
        if (length > 0) Position = target / (double)length;
        TimeText = Format(target);
        var step = Math.Abs(ms) / 1000;
        ShowOsd($"{(ms < 0 ? "\u2212" : "+")}{step} s  \u00b7  {TimeText} / {DurationText}", "\u2190 \u2192 jump 10 s  \u00b7  Shift for 3 s",
            ms < 0 ? "Rewind" : "Forward", length > 0 ? target / (double)length : null);
        _seekSettles = DateTime.Now.AddMilliseconds(700);
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

    /// <summary>The user picked an audio track: switch to it and remember its language for this folder.</summary>
    public void SetAudioTrack(int id)
    {
        Player.SetAudioTrack(id);
        _tracksChosenByUser = true;
        var name = AudioTracks.FirstOrDefault(t => t.Id == id).Name;
        if (id != -1)
            Remember(p =>
            {
                p.AudioLanguage = LanguageOf(TrackType.Audio, id, name);
                p.AudioTrackName = name;
            });
        ShowOsd(id == -1 ? "Audio off" : $"Audio: {name}", "B to switch", "Audio");
    }

    /// <summary>The user picked a subtitle track (or off): switch and remember it for this folder.</summary>
    public void SetSubtitleTrack(int id)
    {
        Player.SetSpu(id);
        _tracksChosenByUser = true;
        var name = SubtitleTracks.FirstOrDefault(t => t.Id == id).Name;
        Remember(p =>
        {
            p.SubtitleLanguage = id == -1 ? Off : LanguageOf(TrackType.Text, id, name);
            p.SubtitleTrackName = id == -1 ? null : name;
        });
        ShowOsd(id == -1 ? "Subtitles off" : $"Subtitles: {name}", "V to switch", "Subtitles");
    }

    public IReadOnlyList<(string Id, string Name)> AudioDevices =>
        Player.AudioOutputDeviceEnum.Select(d => (d.DeviceIdentifier ?? "", d.Description ?? d.DeviceIdentifier ?? "Device")).ToList();

    public string? AudioDevice => Player.OutputDevice;

    public void SetAudioDevice(string id, string name)
    {
        Player.SetOutputDevice(id);
        _store.Data.AudioDevice = id;
        ShowOsd($"Audio output: {name}", null, "Audio");
    }

    // ---- Subtitle and audio delay ----

    public const long MaxDelayMs = 5000;
    public const long DelayStepMs = 50;
    private bool _quietDelay;

    public string SubtitleDelayText => FormatDelay(SubtitleDelayMs);
    public string AudioDelayText => FormatDelay(AudioDelayMs);

    public static string FormatDelay(long ms) =>
        ms == 0 ? "0 ms" : Math.Abs(ms) >= 1000
            ? FormattableString.Invariant($"{(ms < 0 ? "−" : "+")}{Math.Abs(ms) / 1000.0:0.00} s")
            : $"{(ms < 0 ? "−" : "+")}{Math.Abs(ms)} ms";

    public void NudgeSubtitleDelay(long ms) { if (HasMedia) SubtitleDelayMs += ms; }
    public void NudgeAudioDelay(long ms) { if (HasMedia) AudioDelayMs += ms; }

    partial void OnSubtitleDelayMsChanged(long value)
    {
        var clamped = Math.Clamp(value, -MaxDelayMs, MaxDelayMs);
        if (clamped != value) { SubtitleDelayMs = clamped; return; }
        Player.SetSpuDelay(value * 1000);
        OnPropertyChanged(nameof(SubtitleDelayText));
        if (_quietDelay) return;
        Remember(p => p.SubtitleDelayMs = value);
        ShowOsd($"Subtitle delay {SubtitleDelayText}", "G earlier  ·  H later", "Subtitles", (value + MaxDelayMs) / (2.0 * MaxDelayMs));
    }

    partial void OnAudioDelayMsChanged(long value)
    {
        var clamped = Math.Clamp(value, -MaxDelayMs, MaxDelayMs);
        if (clamped != value) { AudioDelayMs = clamped; return; }
        Player.SetAudioDelay(value * 1000);
        OnPropertyChanged(nameof(AudioDelayText));
        if (_quietDelay) return;
        Remember(p => p.AudioDelayMs = value);
        ShowOsd($"Audio delay {AudioDelayText}", "J earlier  ·  K later", "Audio", (value + MaxDelayMs) / (2.0 * MaxDelayMs));
    }

    private void SetDelaysQuietly(long subtitleMs, long audioMs)
    {
        _quietDelay = true;
        try
        {
            SubtitleDelayMs = subtitleMs;
            AudioDelayMs = audioMs;
            // Set again even when unchanged: libVLC starts every item at 0.
            Player.SetSpuDelay(subtitleMs * 1000);
            Player.SetAudioDelay(audioMs * 1000);
        }
        finally { _quietDelay = false; }
    }

    // ---- Per-folder presets ----

    public const string Off = "off";
    private DateTime _tracksSettleBy;
    private bool _tracksChosenByUser;
    private bool _presetAnnounced;

    /// <summary>The folder whose preset applies to what's playing; null for streams.</summary>
    public string? PresetFolder => Kind != MediaKind.Stream && CurrentPath is { } p && Path.IsPathRooted(p) ? Path.GetDirectoryName(p) : null;

    public FolderPreset? CurrentPreset => PresetFolder is { } dir ? _store.PresetFor(dir) : null;

    public void ForgetPreset()
    {
        if (PresetFolder is not { } dir) return;
        _store.ForgetPreset(dir);
        _store.Save();
        ShowOsd("Forgot this folder's subtitle and audio settings", null, "Subtitles");
    }

    private void Remember(Action<FolderPreset> change)
    {
        if (PresetFolder is not { } dir) return;
        var preset = _store.EnsurePreset(dir);
        // A new preset starts from what's on screen now, so one change doesn't drop the others.
        if (preset.SubtitleLanguage is null && preset.SubtitleTrackName is null && preset.AudioLanguage is null && preset.AudioTrackName is null
            && preset.SubtitleDelayMs == 0 && preset.AudioDelayMs == 0)
        {
            preset.SubtitleDelayMs = SubtitleDelayMs;
            preset.AudioDelayMs = AudioDelayMs;
        }
        change(preset);
    }

    /// <summary>Called once the item plays: delays from the folder's preset, then its tracks.</summary>
    private void ApplyPreset()
    {
        var preset = CurrentPreset;
        SetDelaysQuietly(preset?.SubtitleDelayMs ?? 0, preset?.AudioDelayMs ?? 0);
        if (_store.Data.AudioDevice is { Length: > 0 } device) Player.SetOutputDevice(device);
        _tracksSettleBy = DateTime.Now.AddSeconds(8);
        ApplyTrackPreferences();
    }

    /// <summary>Picks subtitle and audio tracks by language: the folder's choice first, then the default languages.</summary>
    private void ApplyTrackPreferences()
    {
        if (!HasMedia || _tracksChosenByUser || DateTime.Now > _tracksSettleBy) return;
        var preset = CurrentPreset;
        var announce = new List<string>();

        if (preset?.SubtitleLanguage == Off)
        {
            if (Player.Spu != -1)
            {
                Player.SetSpu(-1);
                announce.Add("Subtitles off");
            }
        }
        else
        {
            var langs = Languages.ParseList(_store.Data.SubtitleLanguages);
            if (preset?.SubtitleLanguage is { } lang) langs.Insert(0, lang);
            var pick = PickTrack(TrackType.Text, SubtitleTracks, langs, preset?.SubtitleTrackName);
            if (pick is { } s && s.Id != Player.Spu)
            {
                Player.SetSpu(s.Id);
                announce.Add($"Subtitles: {s.Name}");
            }
        }

        var audioLangs = Languages.ParseList(_store.Data.AudioLanguages);
        if (preset?.AudioLanguage is { } alang) audioLangs.Insert(0, alang);
        var audio = PickTrack(TrackType.Audio, AudioTracks.Where(t => t.Id != -1).ToList(), audioLangs, preset?.AudioTrackName);
        if (audio is { } a && a.Id != Player.AudioTrack)
        {
            Player.SetAudioTrack(a.Id);
            announce.Add($"Audio: {a.Name}");
        }

        if (announce.Count > 0 && !_presetAnnounced)
        {
            _presetAnnounced = true;
            ShowOsd(string.Join("  ·  ", announce), preset != null ? "Remembered for this folder" : "Your default languages", "Subtitles");
        }
    }

    private (int Id, string Name)? PickTrack(TrackType type, IReadOnlyList<(int Id, string Name)> tracks, IList<string> langs, string? name)
    {
        var real = tracks.Where(t => t.Id != -1).ToList();
        if (real.Count == 0) return null;
        var withLang = real.Select(t => (Track: t, Lang: LanguageOf(type, t.Id, t.Name))).ToList();
        foreach (var lang in langs)
        {
            var i = withLang.FindIndex(t => t.Lang == lang);
            if (i >= 0) return withLang[i].Track;
        }
        var named = name is null ? -1 : real.FindIndex(t => t.Name == name);
        return named >= 0 ? real[named] : null;
    }

    /// <summary>The track's two-letter language: from the container's tags, else from its name.</summary>
    private string? LanguageOf(TrackType type, int id, string? name)
    {
        using var media = Player.Media;
        var tagged = media?.Tracks.FirstOrDefault(t => t.Id == id && t.TrackType == type).Language;
        return Languages.Normalize(tagged) ?? Languages.FromTrackName(name);
    }

    public void AddSubtitleFile(string file)
    {
        if (!HasMedia) return;
        if (!Player.AddSlave(MediaSlaveType.Subtitle, new Uri(file).AbsoluteUri, true))
            ShowMessage("Couldn't load that subtitle file.");
    }

    // ---- Progress, resume points and thumbnails ----

    private void Tick()
    {
        if (!HasMedia || _disposed || IsEnded) return;
        var length = Player.Length;
        var time = Player.Time;

        if (!IsSeeking && DateTime.Now >= _seekSettles)
        {
            if (length > 0) Position = Math.Clamp(time / (double)length, 0, 1);
            TimeText = Format(Math.Max(0, time));
        }
        DurationText = length > 0 ? Format(length) : MediaScanner.IsStream(CurrentPath ?? "") ? "Live" : "0:00";
        if (length > 0 && CurrentItem is { DurationMs: 0 } current)
        {
            current.DurationMs = length;
            OnPropertyChanged(nameof(QueueSummary));
        }

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

        // First thumbnail a few seconds in (past black intros), then refresh it every 45 s.
        if (HasVideo && IsPlaying && _entry is { Kind: MediaKind.Video } && time > 5000 && (now - _lastSnapshot).TotalSeconds > 45)
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

public sealed record OsdMessage(string Text, string? Hint, string? Icon, double? Level);
