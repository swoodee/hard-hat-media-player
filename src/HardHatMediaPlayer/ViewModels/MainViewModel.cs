using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardHatMediaPlayer.Services;

namespace HardHatMediaPlayer.ViewModels;

/// <summary>Dialogs the view model needs from the window.</summary>
public interface IUiServices
{
    string? PickFolder(string title);
    string[] PickFiles(string filter, bool multiple);
    string? Prompt(string title, string hint, string initial, string okText);
    bool Confirm(string title, string message, string okText);
    void ShowSubtitleSearch(string videoPath);
}

public sealed partial class RailItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public Geometry? Icon { get; init; }
    public Func<PanelPage> Page { get; init; } = () => new PanelPage();
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IUiServices _ui;
    private readonly MetadataService _meta;
    private List<string> _audioFiles = new();
    private bool _scanningMusic;

    public LibraryStore Store { get; }
    public PlayerViewModel Player { get; }
    public PanelViewModel Panel { get; } = new();
    public SubtitleService Subtitles { get; }
    public ObservableCollection<RailItem> Rail { get; } = new();

    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private bool _isPanelWide;
    [ObservableProperty] private bool _isFullscreen;

    public event Action? FullscreenToggled;
    public event Action? ShortcutsRequested;

    public MainViewModel(IUiServices ui, Dispatcher dispatcher)
    {
        _ui = ui;
        Store = new LibraryStore();
        Store.Load();
        Player = new PlayerViewModel(Store, dispatcher);
        _meta = new MetadataService(Player.LibVLC, Store, dispatcher);
        Subtitles = new SubtitleService(Store.Data.Subtitles);
        Player.MediaStarted += path => _ = AutoSubtitlesAsync(path);
        _isPanelOpen = Store.Data.PanelOpen;

        Rail.Add(new RailItem { Key = "home", Label = "Home", Icon = Ico("Home"), Page = HomePage });
        Rail.Add(new RailItem { Key = "videos", Label = "Videos", Icon = Ico("Video"), Page = VideosPage });
        Rail.Add(new RailItem { Key = "music", Label = "Music", Icon = Ico("Music"), Page = MusicPage });
        Rail.Add(new RailItem { Key = "network", Label = "Network", Icon = Ico("Network"), Page = NetworkPage });
        Rail.Add(new RailItem { Key = "playlists", Label = "Playlists", Icon = Ico("Playlist"), Page = PlaylistsPage });
        Rail.Add(new RailItem { Key = "history", Label = "History", Icon = Ico("History"), Page = HistoryPage });

        Panel.RailChanged += key => { foreach (var r in Rail) r.IsSelected = r.Key == key; };
        Player.NowPlayingChanged += () =>
        {
            Panel.CurrentMediaPath = Player.CurrentPath;
            RefreshIf("home", "history", "network");
        };
        Player.ThumbnailUpdated += () => RefreshIf("home", "history");
        // Resume points change when playback pauses or stops; Continue Watching should reflect that.
        Player.PlaybackStopped += () => RefreshIf("home", "history");
        Player.OpenRequested += () => OpenFiles();

        // Tag reading reports in batches; rebuilding the list on every batch makes long lists jump.
        _metaRefresh = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) =>
        {
            _metaRefresh!.Stop();
            RefreshIf("music");
        }, dispatcher);
        _metaRefresh.Stop();
        _meta.Updated += () => { if (!_metaRefresh.IsEnabled) _metaRefresh.Start(); };

        Panel.Navigate(HomePage);
        RescanMusic();
    }

    private readonly DispatcherTimer _metaRefresh;

    partial void OnIsPanelOpenChanged(bool value) => Store.Data.PanelOpen = value;

    private void RefreshIf(params string[] railKeys)
    {
        if (railKeys.Contains(Panel.Page.RailKey)) Panel.Refresh();
    }

    // ---- Commands used by the chrome ----

    [RelayCommand]
    private void SelectRail(RailItem item)
    {
        // Clicking the section that's already showing collapses the panel, like an activity bar.
        if (IsPanelOpen && Panel.Page.Id == "root:" + item.Key)
        {
            IsPanelOpen = false;
            return;
        }
        IsPanelOpen = true;
        // Re-selecting the page that's already showing shouldn't add a duplicate history entry.
        if (Panel.Page.Id == "root:" + item.Key) return;
        Panel.Navigate(item.Page);
    }

    [RelayCommand] private void TogglePanel() => IsPanelOpen = !IsPanelOpen;
    [RelayCommand] private void TogglePanelWide() => IsPanelWide = !IsPanelWide;
    [RelayCommand] private void ToggleFullscreen() => FullscreenToggled?.Invoke();

    [RelayCommand]
    private void OpenSource()
    {
        IsPanelOpen = true;
        Panel.Navigate(Player.SourcePage ?? HomePage);
    }

    [RelayCommand]
    public void OpenFiles()
    {
        var files = _ui.PickFiles(MediaFilter, true);
        if (files.Length > 0) Play(files);
    }

    [RelayCommand]
    public void OpenFolder()
    {
        var dir = _ui.PickFolder("Choose a folder to play");
        if (dir is null) return;
        var files = MediaScanner.Expand(new[] { dir });
        if (files.Count == 0)
        {
            Player.ShowMessage($"No playable media in “{FolderTitle(dir)}”.");
            return;
        }
        Player.PlayList(files, 0, FolderTitle(dir), () => FolderPage(dir));
    }

    [RelayCommand]
    public void OpenNetworkStream()
    {
        var url = _ui.Prompt("Open network stream", "https://, rtsp://, udp://, …", "", "Play")?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        // Accept "example.com/live.m3u8" without a scheme.
        if (!url.Contains("://") && !Path.IsPathRooted(url) && url.Contains('.')) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            _ui.Confirm("Open network stream", $"“{url}” isn't a valid address.", "OK");
            return;
        }
        Player.PlayList(new[] { url }, 0, "Network", NetworkPage);
    }

    /// <summary>Plays files or folders (from the command line, drag and drop or the open dialog).</summary>
    public void Play(IEnumerable<string> paths)
    {
        var files = MediaScanner.Expand(paths);
        if (files.Count == 0)
        {
            Player.ShowMessage("Nothing playable there.");
            return;
        }
        if (files.Count == 1 && File.Exists(files[0])) PlayInFolder(files[0]);
        else Player.PlayList(files, 0, "Opened files");
    }

    public void Enqueue(IEnumerable<string> paths)
    {
        var files = MediaScanner.Expand(paths);
        Player.Enqueue(files);
        if (files.Count > 0) Player.ShowMessage(files.Count == 1 ? "Added to queue." : $"Added {files.Count} items to the queue.");
    }

    private static string FolderTitle(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? dir : name;
    }

    public IReadOnlyList<MenuAction> MainMenu() => new List<MenuAction>
    {
        new("Open file…", OpenFiles, Gesture: "Ctrl+O"),
        new("Open folder…", OpenFolder, Gesture: "Ctrl+Shift+O"),
        new("Open network stream…", OpenNetworkStream, Gesture: "Ctrl+N"),
        new("Find subtitles online…", FindSubtitles),
        MenuAction.Separator,
        new("Playback", Children: PlaybackMenu()),
        MenuAction.Separator,
        new("Show library panel", TogglePanel, IsChecked: IsPanelOpen, Gesture: "Ctrl+B"),
        new("Fullscreen", ToggleFullscreen, Gesture: "F11"),
        MenuAction.Separator,
        new("Keyboard shortcuts", () => ShortcutsRequested?.Invoke(), Gesture: "F1"),
        new("About", ShowAbout),
        new("Exit", () => Application.Current.MainWindow?.Close(), Gesture: "Alt+F4"),
    };

    public IReadOnlyList<MenuAction> PlaybackMenu()
    {
        var speeds = new[] { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
        return new List<MenuAction>
        {
            new(Player.IsPlaying ? "Pause" : "Play", Player.TogglePlay, Gesture: "Space"),
            new("Stop", Player.Stop, Gesture: "S"),
            new("Previous", Player.Previous, Gesture: "P"),
            new("Next", Player.Next, Gesture: "N"),
            MenuAction.Separator,
            new("Speed", Children: speeds.Select(s => new MenuAction(s == 1f ? "Normal" : $"{s:0.##}×", () => Player.Rate = s, IsChecked: Math.Abs(Player.Rate - s) < 0.01)).ToList()),
            new("Shuffle", () => Player.Shuffle = !Player.Shuffle, IsChecked: Player.Shuffle),
            new("Repeat", Children: new[]
            {
                new MenuAction("Off", () => Player.Repeat = RepeatMode.Off, IsChecked: Player.Repeat == RepeatMode.Off),
                new MenuAction("Repeat all", () => Player.Repeat = RepeatMode.All, IsChecked: Player.Repeat == RepeatMode.All),
                new MenuAction("Repeat one", () => Player.Repeat = RepeatMode.One, IsChecked: Player.Repeat == RepeatMode.One),
            }),
        };
    }

    /// <summary>Audio and subtitle tracks for the player's track button.</summary>
    public IReadOnlyList<MenuAction> TracksMenu()
    {
        var items = new List<MenuAction>();
        if (Player.HasMedia)
        {
            var subs = Player.SubtitleTracks
                .Select(t => new MenuAction(t.Id == -1 ? "Off" : t.Name, () => Player.SetSubtitleTrack(t.Id), IsChecked: t.Id == Player.SubtitleTrack))
                .ToList();
            if (subs.Count > 0) subs.Add(MenuAction.Separator);
            subs.Add(new MenuAction("Add subtitle file…", () =>
            {
                var f = _ui.PickFiles("Subtitles|*.srt;*.ass;*.ssa;*.sub;*.vtt;*.idx|All files|*.*", false);
                if (f.Length > 0) Player.AddSubtitleFile(f[0]);
            }));
            items.Add(new MenuAction("Subtitles", Children: subs));
            if (Player.Kind == MediaKind.Video && File.Exists(Player.CurrentPath))
                items.Add(new MenuAction("Find subtitles online…", () => _ui.ShowSubtitleSearch(Player.CurrentPath!)));
        }

        var audio = Player.AudioTracks;
        if (audio.Count > 0)
            items.Add(new MenuAction("Audio track", Children: audio
                .Select(t => new MenuAction(t.Id == -1 ? "Disable" : t.Name, () => Player.SetAudioTrack(t.Id), IsChecked: t.Id == Player.AudioTrack))
                .ToList()));

        if (items.Count > 0) items.Add(MenuAction.Separator);
        items.AddRange(PlaybackMenu().Skip(5)); // speed, shuffle, repeat
        return items;
    }

    private void ShowAbout() => _ui.Confirm("About Hard Hat Media Player",
        $"Hard Hat Media Player {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}\n" +
        "A free, open-source media player for Windows.\n" +
        "hardhatmediaplayer.com\n\n" +
        "Licensed under the GNU GPL v3 or later.\n" +
        "Playback: libVLC and LibVLCSharp by VideoLAN (LGPL 2.1+).\n" +
        "Interface based on “VLC Redesign” by Juxtopposed (Figma Community, CC BY 4.0).\n\n" +
        $"Library data: {LibraryStore.AppDir}", "OK");

    private const string MediaFilter =
        "Media files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.ogv;*.vob;" +
        "*.mp3;*.flac;*.wav;*.aac;*.m4a;*.ogg;*.opus;*.wma;*.aiff;*.ape;*.mka|All files|*.*";

    // ---- Library maintenance ----

    private void AddVideoFolder() => AddFolder(Store.Data.VideoFolders, "Add a video folder");

    private void AddMusicFolder()
    {
        if (AddFolder(Store.Data.MusicFolders, "Add a music folder")) RescanMusic();
    }

    private bool AddFolder(List<string> roots, string title)
    {
        var dir = _ui.PickFolder(title);
        if (dir is null || roots.Contains(dir, StringComparer.OrdinalIgnoreCase)) return false;
        roots.Add(dir);
        Store.Save();
        Panel.Refresh();
        return true;
    }

    private void RemoveRoot(List<string> roots, string dir)
    {
        roots.RemoveAll(r => string.Equals(r, dir, StringComparison.OrdinalIgnoreCase));
        Store.Save();
        if (roots == Store.Data.MusicFolders) RescanMusic();
        Panel.Refresh();
    }

    private void RescanMusic()
    {
        _scanningMusic = true;
        var roots = Store.Data.MusicFolders.ToList();
        Task.Run(() => MediaScanner.AudioFilesDeep(roots)).ContinueWith(t =>
        {
            _scanningMusic = false;
            _audioFiles = t.Result;
            _meta.Scan(_audioFiles);
            RefreshIf("music");
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void PlayInFolder(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is null || !File.Exists(path))
        {
            Player.PlayList(new[] { path }, 0, MediaScanner.IsStream(path) ? "Network" : null, MediaScanner.IsStream(path) ? NetworkPage : null);
            return;
        }
        var files = MediaScanner.MediaFiles(dir);
        var index = files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            files.Insert(0, path);
            index = 0;
        }
        Player.PlayList(files, index, FolderTitle(dir), () => FolderPage(dir));
    }

    private void ShowInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
            else if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void FindSubtitles()
    {
        if (Player.Kind == MediaKind.Video && Player.CurrentPath is { } path && File.Exists(path)) _ui.ShowSubtitleSearch(path);
        else Player.ShowMessage("Play a video file first, then search for its subtitles.");
    }

    /// <summary>VLsub-style automatic subtitles: only for local videos that start without any subtitle track.</summary>
    private async Task AutoSubtitlesAsync(string path)
    {
        if (Player.Kind != MediaKind.Video || !File.Exists(path) || !Subtitles.Settings.AutoDownload || !Subtitles.IsConfigured) return;
        await Task.Delay(1500); // tracks (embedded or "video.lang.srt" next to it) are known shortly after start
        if (!string.Equals(Player.CurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;
        if (Player.SubtitleTracks.Any(t => t.Id != -1)) return;

        try
        {
            var found = await Subtitles.AutoDownloadAsync(path);
            if (found is not { } f || !string.Equals(Player.CurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;
            Player.AddSubtitleFile(f.Path);
            Player.ShowMessage($"Subtitles added automatically ({f.Result.Language}). Change them from the subtitles button.");
        }
        catch (OpenSubtitlesException e)
        {
            Player.ShowMessage("Automatic subtitles: " + e.Message);
        }
    }

    public void Dispose()
    {
        Player.Dispose();
        Subtitles.Dispose();
    }

    private static Geometry Ico(string key) => (Geometry)Application.Current.Resources["I." + key];
}
