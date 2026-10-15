using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VlcRedesign.Services;

namespace VlcRedesign.ViewModels;

// The pages shown in the library panel. Each method builds a fresh page from the current library state.
public sealed partial class MainViewModel
{
    private const int HomeSectionSize = 3;

    // ---- Home ----

    public PanelPage HomePage()
    {
        var rows = new List<PanelRow>();

        var watching = ContinueWatching().Take(HomeSectionSize).ToList();
        if (watching.Count > 0)
        {
            rows.Add(new SectionRow { Title = "Continue Watching", Open = () => Panel.Navigate(ContinueWatchingPage) });
            rows.AddRange(watching.Select(HistoryRow));
        }

        var listened = ListenAgain().Take(HomeSectionSize).ToList();
        if (listened.Count > 0)
        {
            rows.Add(new SectionRow { Title = "Listen Again", Open = () => Panel.Navigate(ListenAgainPage) });
            rows.AddRange(listened.Select(HistoryRow));
        }

        var folders = FrequentFolders().Take(HomeSectionSize).ToList();
        if (folders.Count > 0)
        {
            rows.Add(new SectionRow { Title = "Frequently Visited Folders", Open = () => Panel.Navigate(FrequentFoldersPage) });
            rows.AddRange(folders.Select(d => FolderRow(d)));
        }

        var playlists = FrequentPlaylists().Take(HomeSectionSize).ToList();
        if (playlists.Count > 0)
        {
            rows.Add(new SectionRow { Title = "Frequently Visited Playlists", Open = () => Panel.Navigate(FrequentPlaylistsPage) });
            rows.AddRange(playlists.Select(PlaylistRow));
        }

        if (rows.Count == 0)
        {
            rows.Add(new SectionRow { Title = "Get started" });
            rows.Add(ActionRow("Open media…", "Film", OpenFiles));
            rows.Add(ActionRow("Add a video folder", "Video", AddVideoFolder));
            rows.Add(ActionRow("Add a music folder", "Music", AddMusicFolder));
            rows.Add(ActionRow("Open network stream…", "Network", OpenNetworkStream));
        }

        return new PanelPage { Id = "root:home", Title = "Home", RailKey = "home", Rows = rows, CanSort = false };
    }

    private IEnumerable<HistoryEntry> ContinueWatching() =>
        Store.Data.History.Where(h => h.Kind == MediaKind.Video && h.IsUnfinished && File.Exists(h.Path));

    private IEnumerable<HistoryEntry> ListenAgain() =>
        Store.Data.History.Where(h => h.Kind == MediaKind.Audio && File.Exists(h.Path));

    private IEnumerable<string> FrequentFolders() =>
        Store.Data.FolderVisits.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Where(Directory.Exists);

    private IEnumerable<Playlist> FrequentPlaylists() =>
        Store.Data.Playlists.Where(p => p.Opens > 0).OrderByDescending(p => p.Opens).ThenByDescending(p => p.LastOpened);

    private PanelPage ContinueWatchingPage() => new()
    {
        Title = "Continue Watching", RailKey = "home",
        Rows = ContinueWatching().Select(HistoryRow).ToList<PanelRow>(),
        EmptyText = "Videos you stop part-way through show up here.",
    };

    private PanelPage ListenAgainPage() => new()
    {
        Title = "Listen Again", RailKey = "home",
        Rows = ListenAgain().Select(HistoryRow).ToList<PanelRow>(),
        EmptyText = "Music you play shows up here.",
    };

    private PanelPage FrequentFoldersPage() => new()
    {
        Title = "Frequently Visited Folders", RailKey = "home",
        Rows = FrequentFolders().Select(d => (PanelRow)FolderRow(d)).ToList(),
    };

    private PanelPage FrequentPlaylistsPage() => new()
    {
        Title = "Frequently Visited Playlists", RailKey = "home",
        Rows = FrequentPlaylists().Select(p => (PanelRow)PlaylistRow(p)).ToList(),
    };

    // ---- Videos & folders ----

    public PanelPage VideosPage()
    {
        var rows = new List<PanelRow> { ActionRow("Add", "Plus", AddVideoFolder) };
        rows.AddRange(Store.Data.VideoFolders.Select(d => FolderRow(d,
            new MenuAction("Remove from library", () => RemoveRoot(Store.Data.VideoFolders, d)))));
        return new PanelPage
        {
            Id = "root:videos", Title = "Videos", RailKey = "videos", Rows = rows,
            EmptyText = "Add a folder to start your video library.",
        };
    }

    private PanelPage FolderPage(string dir)
    {
        var rail = IsUnder(dir, Store.Data.MusicFolders) ? "music" : "videos";
        var title = FolderTitle(dir);

        if (!Directory.Exists(dir))
            return new PanelPage { Title = title, RailKey = rail, EmptyText = "This folder isn't available right now." };

        var files = MediaScanner.MediaFiles(dir);
        var rows = new List<PanelRow>();
        rows.AddRange(MediaScanner.SubFolders(dir).Select(d => FolderRow(d)));
        rows.AddRange(files.Select((f, i) => MediaRow(f, () => Player.PlayList(files, i, title, () => FolderPage(dir)))));

        return new PanelPage
        {
            Title = title, RailKey = rail, Rows = rows,
            EmptyText = "No media in this folder.",
            Menu = () => CollectionMenu(() => MediaScanner.Expand(new[] { dir }), title, () => FolderPage(dir))
                .Append(MenuAction.Separator)
                .Append(new MenuAction("Show in Explorer", () => ShowInExplorer(dir)))
                .ToList(),
        };
    }

    private ItemRow FolderRow(string dir, params MenuAction[] extra)
    {
        var name = FolderTitle(dir);
        return new ItemRow
        {
            Label = name,
            Icon = Ico("Folder"),
            Invoke = () =>
            {
                Store.VisitFolder(dir);
                Store.Save();
                Panel.Navigate(() => FolderPage(dir));
            },
            Menu = () => CollectionMenu(() => MediaScanner.Expand(new[] { dir }), name, () => FolderPage(dir))
                .Append(MenuAction.Separator)
                .Append(new MenuAction("Show in Explorer", () => ShowInExplorer(dir)))
                .Concat(extra)
                .ToList(),
        };
    }

    // ---- Music ----

    public PanelPage MusicPage()
    {
        var rows = new List<PanelRow>
        {
            ActionRow("Add", "Plus", AddMusicFolder),
            NavRow("Songs", "Note", SongsPage),
            NavRow("Albums", "Album", () => GroupsPage(MusicGroup.Album)),
            NavRow("Artists", "Artist", () => GroupsPage(MusicGroup.Artist)),
            NavRow("Genres", "Genre", () => GroupsPage(MusicGroup.Genre)),
        };
        if (Store.Data.MusicFolders.Count > 0)
        {
            rows.Add(new SectionRow { Title = "Folders" });
            rows.AddRange(Store.Data.MusicFolders.Select(d => FolderRow(d,
                new MenuAction("Remove from library", () => RemoveRoot(Store.Data.MusicFolders, d)))));
        }
        return new PanelPage { Id = "root:music", Title = "Music", RailKey = "music", Rows = rows, CanSort = false };
    }

    private enum MusicGroup { Album, Artist, Genre }

    private string? GroupValue(string path, MusicGroup group)
    {
        var m = Store.MetaFor(path);
        return group switch { MusicGroup.Album => m?.Album, MusicGroup.Artist => m?.Artist, _ => m?.Genre };
    }

    private List<string> SortedSongs(IEnumerable<string> files, bool byAlbum) =>
        (byAlbum
            ? files.OrderBy(f => Store.MetaFor(f)?.Album ?? "￿", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(f => Store.MetaFor(f)?.Track ?? 0)
                .ThenBy(SongTitle, NaturalComparer.Instance)
            : files.OrderBy(SongTitle, NaturalComparer.Instance))
        .ToList();

    private string SongTitle(string path) => Store.MetaFor(path)?.Title ?? MediaScanner.DisplayName(path);

    private string? MusicEmptyText => _scanningMusic
        ? "Scanning your music…"
        : Store.Data.MusicFolders.Count == 0 ? "Add a music folder to see your songs." : "No songs found in your music folders.";

    private PanelPage SongsPage()
    {
        var songs = SortedSongs(_audioFiles, byAlbum: false);
        return new PanelPage
        {
            Title = "Songs", RailKey = "music",
            Rows = songs.Select((f, i) => (PanelRow)MediaRow(f, () => Player.PlayList(songs, i, "Songs", SongsPage), Store.MetaFor(f)?.Artist)).ToList(),
            EmptyText = MusicEmptyText,
            Menu = () => CollectionMenu(() => songs, "Songs", SongsPage)
                .Append(MenuAction.Separator)
                .Append(new MenuAction("Rescan music folders", RescanMusic))
                .ToList(),
        };
    }

    private PanelPage GroupsPage(MusicGroup group)
    {
        var (title, unknown, icon) = group switch
        {
            MusicGroup.Album => ("Albums", "Unknown album", "Album"),
            MusicGroup.Artist => ("Artists", "Unknown artist", "Artist"),
            _ => ("Genres", "Unknown genre", "Genre"),
        };

        var rows = _audioFiles
            .GroupBy(f => GroupValue(f, group) ?? unknown, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g.Key == unknown ? 1 : 0).ThenBy(g => g.Key, NaturalComparer.Instance)
            .Select(g =>
            {
                var name = g.Key;
                var songs = g.ToList();
                var artist = group == MusicGroup.Album ? songs.Select(f => Store.MetaFor(f)?.Artist).FirstOrDefault(a => a != null) : null;
                return (PanelRow)new ItemRow
                {
                    Label = name,
                    Icon = Ico(icon),
                    Thumbnail = group == MusicGroup.Album ? songs.Select(f => Store.MetaFor(f)?.Artwork).FirstOrDefault(a => a != null) : null,
                    Detail = artist != null ? $"{artist} · {songs.Count}" : songs.Count == 1 ? "1 song" : $"{songs.Count} songs",
                    Invoke = () => Panel.Navigate(() => GroupPage(group, name, unknown)),
                    Menu = () => CollectionMenu(() => SortedSongs(GroupFiles(group, name, unknown), true), name, () => GroupPage(group, name, unknown)).ToList(),
                };
            })
            .ToList();

        return new PanelPage { Title = title, RailKey = "music", Rows = rows, EmptyText = MusicEmptyText };
    }

    private IEnumerable<string> GroupFiles(MusicGroup group, string name, string unknown) =>
        _audioFiles.Where(f => string.Equals(GroupValue(f, group) ?? unknown, name, StringComparison.CurrentCultureIgnoreCase));

    private PanelPage GroupPage(MusicGroup group, string name, string unknown)
    {
        var songs = SortedSongs(GroupFiles(group, name, unknown), byAlbum: true);
        Func<PanelPage> self = () => GroupPage(group, name, unknown);
        return new PanelPage
        {
            Title = name, RailKey = "music",
            Rows = songs.Select((f, i) => (PanelRow)MediaRow(f, () => Player.PlayList(songs, i, name, self),
                group == MusicGroup.Artist ? Store.MetaFor(f)?.Album : Store.MetaFor(f)?.Artist)).ToList(),
            Menu = () => CollectionMenu(() => songs, name, self).ToList(),
        };
    }

    // ---- Network ----

    public PanelPage NetworkPage()
    {
        var rows = new List<PanelRow> { ActionRow("Open network stream…", "Plus", OpenNetworkStream) };
        var streams = Store.Data.History.Where(h => h.Kind == MediaKind.Stream).ToList();
        if (streams.Count > 0)
        {
            rows.Add(new SectionRow { Title = "Recent streams" });
            rows.AddRange(streams.Select(h => MediaRow(h.Path, () => Player.PlayList(new[] { h.Path }, 0, "Network", NetworkPage),
                RelativeTime(h.LastPlayed), new MenuAction("Remove from history", () => RemoveHistory(h)))));
        }
        return new PanelPage { Id = "root:network", Title = "Network", RailKey = "network", Rows = rows, CanSort = false };
    }

    // ---- Playlists ----

    public PanelPage PlaylistsPage()
    {
        var rows = new List<PanelRow> { ActionRow("Create playlist", "Plus", () => CreatePlaylist()) };
        rows.AddRange(Store.Data.Playlists.Select(PlaylistRow));
        return new PanelPage { Id = "root:playlists", Title = "Playlists", RailKey = "playlists", Rows = rows };
    }

    private ItemRow PlaylistRow(Playlist p) => new()
    {
        Label = p.Name,
        Icon = Ico("Playlist"),
        Detail = p.Items.Count == 1 ? "1 item" : $"{p.Items.Count} items",
        Invoke = () => Panel.Navigate(() => PlaylistPage(p.Id)),
        Menu = () => PlaylistMenu(p),
    };

    private PanelPage PlaylistPage(string id)
    {
        var p = Store.Data.Playlists.FirstOrDefault(x => x.Id == id);
        if (p is null) return new PanelPage { Title = "Playlist", RailKey = "playlists", EmptyText = "This playlist was deleted." };

        var rows = p.Items.Select((path, i) => (PanelRow)MediaRow(path, () => PlayPlaylist(p, i), null,
            new MenuAction("Move up", () => MoveInPlaylist(p, i, -1)),
            new MenuAction("Move down", () => MoveInPlaylist(p, i, +1)),
            new MenuAction("Remove from playlist", () =>
            {
                p.Items.RemoveAt(i);
                Store.Save();
                Panel.Refresh();
            }))).ToList();

        return new PanelPage
        {
            Title = p.Name, RailKey = "playlists", Rows = rows,
            EmptyText = "Right-click any song or video and choose “Add to playlist”.",
            Menu = () => PlaylistMenu(p),
        };
    }

    private IReadOnlyList<MenuAction> PlaylistMenu(Playlist p) => new List<MenuAction>
    {
        new("Play", () => PlayPlaylist(p, 0)),
        new("Shuffle play", () => PlayPlaylist(p, 0, shuffle: true)),
        new("Add to queue", () => Player.Enqueue(p.Items)),
        MenuAction.Separator,
        new("Rename…", () =>
        {
            var name = _ui.Prompt("Rename playlist", "Playlist name", p.Name, "Rename")?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            p.Name = name;
            Store.Save();
            Panel.Refresh();
        }),
        new("Delete playlist", () =>
        {
            if (!_ui.Confirm("Delete playlist", $"Delete “{p.Name}”? The media files themselves aren't touched.", "Delete")) return;
            Store.Data.Playlists.Remove(p);
            Store.Save();
            // Leave the deleted playlist's page; Refresh would show "deleted" otherwise.
            if (Panel.Page.RailKey == "playlists" && Panel.Page.Title == p.Name) Panel.Navigate(PlaylistsPage);
            else Panel.Refresh();
        }),
    };

    private void PlayPlaylist(Playlist p, int index, bool shuffle = false)
    {
        var items = p.Items.Where(i => MediaScanner.IsStream(i) || File.Exists(i)).ToList();
        if (items.Count == 0) return;
        p.Opens++;
        p.LastOpened = DateTime.Now;
        if (shuffle)
        {
            var rnd = new Random();
            items = items.OrderBy(_ => rnd.Next()).ToList();
            index = 0;
        }
        else
        {
            index = Math.Max(0, items.IndexOf(p.Items[Math.Clamp(index, 0, p.Items.Count - 1)]));
        }
        Player.PlayList(items, index, p.Name, () => PlaylistPage(p.Id));
    }

    private void MoveInPlaylist(Playlist p, int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= p.Items.Count) return;
        (p.Items[index], p.Items[target]) = (p.Items[target], p.Items[index]);
        Store.Save();
        Panel.Refresh();
    }

    private Playlist? CreatePlaylist(IEnumerable<string>? items = null)
    {
        var name = _ui.Prompt("Create playlist", "Playlist name", "", "Create")?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var p = new Playlist { Name = name };
        if (items != null) p.Items.AddRange(items);
        Store.Data.Playlists.Add(p);
        Store.Save();
        Panel.Refresh();
        return p;
    }

    private void AddToPlaylist(Playlist p, IEnumerable<string> items)
    {
        p.Items.AddRange(items.Where(i => !p.Items.Contains(i, StringComparer.OrdinalIgnoreCase)));
        Store.Save();
        Panel.Refresh();
    }

    private MenuAction AddToPlaylistMenu(Func<IEnumerable<string>> items)
    {
        var children = Store.Data.Playlists
            .Select(p => new MenuAction(p.Name, () => AddToPlaylist(p, items())))
            .ToList();
        if (children.Count > 0) children.Add(MenuAction.Separator);
        children.Add(new MenuAction("New playlist…", () => CreatePlaylist(items())));
        return new MenuAction("Add to playlist", Children: children);
    }

    // ---- History ----

    public PanelPage HistoryPage() => new()
    {
        Id = "root:history", Title = "History", RailKey = "history",
        Rows = Store.Data.History.Select(h => (PanelRow)MediaRow(h.Path, () => PlayInFolder(h.Path),
            RelativeTime(h.LastPlayed), new MenuAction("Remove from history", () => RemoveHistory(h)))).ToList(),
        EmptyText = "Everything you play shows up here.",
        Menu = () => new List<MenuAction>
        {
            new("Clear history", () =>
            {
                if (!_ui.Confirm("Clear history", "Forget everything you've played, including resume points?", "Clear")) return;
                foreach (var h in Store.Data.History) DeleteThumbnail(h);
                Store.Data.History.Clear();
                Store.Save();
                Panel.Refresh();
            }),
        },
    };

    private void RemoveHistory(HistoryEntry h)
    {
        DeleteThumbnail(h);
        Store.Data.History.Remove(h);
        Store.Save();
        Panel.Refresh();
    }

    private ItemRow HistoryRow(HistoryEntry h)
    {
        string? detail = h.Kind == MediaKind.Video && h.IsUnfinished
            ? $"{PlayerViewModel.Format(h.DurationMs - h.PositionMs)} left"
            : null;
        return MediaRow(h.Path, () => PlayInFolder(h.Path), detail);
    }

    // ---- Row and menu builders ----

    private ItemRow ActionRow(string label, string icon, Action run) =>
        new() { Label = label, Icon = Ico(icon), IsAction = true, Invoke = run };

    private ItemRow NavRow(string label, string icon, Func<PanelPage> page) =>
        new() { Label = label, Icon = Ico(icon), Invoke = () => Panel.Navigate(page) };

    private ItemRow MediaRow(string path, Action play, string? detail = null, params MenuAction[] extra)
    {
        var kind = MediaScanner.KindOf(path);
        var meta = Store.MetaFor(path);
        var history = Store.FindHistory(path);
        var duration = meta?.DurationMs ?? history?.DurationMs ?? 0;
        var thumb = history?.Thumbnail;

        return new ItemRow
        {
            // Streams have no tags on disk; use the title libVLC reported while playing.
            Label = meta?.Title ?? (kind == MediaKind.Stream && history?.Title is { Length: > 0 } t ? t : MediaScanner.DisplayName(path)),
            Icon = Ico(kind switch { MediaKind.Audio => "Note", MediaKind.Stream => "Globe", _ => "Film" }),
            Thumbnail = thumb != null && File.Exists(thumb) ? thumb : null,
            Detail = detail ?? (duration > 0 ? PlayerViewModel.Format(duration) : null),
            MediaPath = path,
            Invoke = play,
            Menu = () =>
            {
                var menu = new List<MenuAction>
                {
                    new("Play", play),
                    new("Play next", () => Player.PlayNext(path)),
                    new("Add to queue", () => Player.Enqueue(new[] { path })),
                    AddToPlaylistMenu(() => new[] { path }),
                };
                if (extra.Length > 0)
                {
                    menu.Add(MenuAction.Separator);
                    menu.AddRange(extra);
                }
                if (kind != MediaKind.Stream)
                {
                    menu.Add(MenuAction.Separator);
                    menu.Add(new MenuAction("Show in Explorer", () => ShowInExplorer(path)));
                }
                return menu;
            },
        };
    }

    /// <summary>Play / shuffle / queue / add-to-playlist for a group of media (folder, album, playlist…).</summary>
    private IEnumerable<MenuAction> CollectionMenu(Func<List<string>> items, string label, Func<PanelPage> source) => new[]
    {
        new MenuAction("Play all", () => Player.PlayList(items(), 0, label, source)),
        new MenuAction("Shuffle all", () =>
        {
            var rnd = new Random();
            Player.PlayList(items().OrderBy(_ => rnd.Next()).ToList(), 0, label, source);
        }),
        new MenuAction("Add to queue", () => Player.Enqueue(items())),
        AddToPlaylistMenu(items),
    };

    private static bool IsUnder(string dir, IEnumerable<string> roots) =>
        roots.Select(r => r.TrimEnd(Path.DirectorySeparatorChar)).Any(r =>
            dir.Equals(r, StringComparison.OrdinalIgnoreCase) ||
            dir.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private static void DeleteThumbnail(HistoryEntry h)
    {
        if (h.Thumbnail is null) return;
        try { File.Delete(h.Thumbnail); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string RelativeTime(DateTime when)
    {
        var days = (DateTime.Today - when.Date).Days;
        return days switch
        {
            0 => when.ToString("t"),
            1 => "Yesterday",
            < 7 => when.ToString("dddd"),
            _ => when.ToString("d MMM yyyy"),
        };
    }
}
