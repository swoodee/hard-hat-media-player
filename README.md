# VLC Redesign (desktop)

A Windows media player that implements the [“VLC Redesign” Figma prototype](https://www.figma.com/community/file/1691465999134370980/vlc-redesign)
by Juxtopposed ([video](https://youtu.be/ERFeOgqk324)), with playback powered by [libVLC](https://www.videolan.org/vlc/libvlc.html)
through LibVLCSharp. It is a standalone app built on VLC's engine, not a patch to the
[videolan/vlc](https://github.com/videolan/vlc) Qt interface.

## Build and run

Requires the .NET 7 SDK (or newer) on Windows.

```bash
dotnet run --project src/VlcRedesign
```

You can pass files or folders on the command line, or drag them onto the window.

## What's in it

- **Title bar**: app mark (an orange hard hat, side view), main menu (`…`), library-panel toggle, a "playing from" breadcrumb that jumps to the source folder or playlist.
- **Icon rail**: Home, Videos, Music, Network, Playlists, History, and `+` to open media. Clicking the active section collapses the panel.
- **Library panel**: back/forward, page menu, expand, item count, sort, details toggle, search. Right-click any row for Play / Play next / Add to queue / Add to playlist / Show in Explorer.
  - *Home*: Continue Watching (with resume points and thumbnails), Listen Again, Frequently Visited Folders and Playlists.
  - *Videos*: library folders, then browse sub-folders and files (episodes ordered naturally, autoplay to the next one).
  - *Music*: Songs, Albums, Artists, Genres, read from tags in the background.
  - *Network*: open a URL (http, rtsp, udp, …) and reopen recent streams.
  - *Playlists*: create, rename, reorder, delete.
  - *History*: everything played, with clear and remove.
- **Player**: auto-hiding floating controls with seek bar, shuffle, previous / play / next, repeat (off/all/one), subtitles and audio tracks, external subtitle files, playback speed, volume, fullscreen. The idle screen shows the prototype's pixel cat. Audio shows album art.

### Subtitles from OpenSubtitles (VLsub-style)

Open **… → Find subtitles online…**, or the subtitles button in the player → **Subtitles → Find subtitles online…**.
You can search **by hash** (subtitles made for that exact file) or **by name**, pick a result, and choose **Download and use**.
The file is saved next to the video as `video.lang.srt`, so VLC loads it automatically next time.

With **Download automatically** on, a video that starts without any subtitles gets the best exact (hash) match in your languages.
Name-only matches are never applied automatically.

This uses the [OpenSubtitles.com REST API](https://opensubtitles.stoplight.io/docs/opensubtitles-api). The classic VLsub
used the old opensubtitles.org XML-RPC API, which has been shut down for third-party apps. Setup:

1. Create a free account at opensubtitles.com and get an API key at <https://www.opensubtitles.com/consumers>.
2. In the Find subtitles window, choose **Account…** and paste the key. Username and password are optional and raise
   the daily download limit. The password is stored encrypted for your Windows account (DPAPI).

### Shortcuts

| Key | Action |
| --- | --- |
| Space | Play / pause |
| ← / → | Seek 10 s (Shift: 3 s) |
| ↑ / ↓, mouse wheel | Volume |
| F, F11, double-click | Fullscreen (Esc to leave) |
| M / N / P / S | Mute / next / previous / stop |
| Ctrl+O, Ctrl+Shift+O, Ctrl+N | Open file / folder / network stream |
| Ctrl+B, Ctrl+F | Toggle library panel / search |
| Alt+← / Alt+→ | Panel back / forward |

Library data (folders, playlists, history, tag cache, thumbnails) lives in `%APPDATA%\VlcRedesign`.
Set the `VLCREDESIGN_DATA` environment variable to use another folder, e.g. for a portable copy or for testing.

## Layout

```
src/VlcRedesign/
  MainWindow.xaml(.cs)      window chrome, rail, panel, video + overlay controls
  Themes/                   colours, control styles, icon geometries
  Controls/                 Icon renderer, pixel cat, converters, context-menu builder
  ViewModels/               player (libVLC), panel navigation, library pages
  Services/                 JSON store, folder scanning, tag reading
  Views/InputDialog         prompt / confirm dialog
```

## Credits and licences

- Interface design: “VLC Redesign” by Juxtopposed, Figma Community, CC BY 4.0.
- libVLC and LibVLCSharp by VideoLAN, LGPL 2.1+.
- Icon shapes follow the Lucide style (ISC).

The app uses its own hard-hat mark, drawn in `src/VlcRedesign/Themes/AppMark.xaml`; after changing it, run `scripts/make-icon.ps1` to regenerate `app.ico`. “VLC” is a VideoLAN trademark, so rename the app before distributing it publicly.
