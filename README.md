# Hard Hat Media Player

A free, open-source media player for Windows, with a clean modern interface and playback powered by
[libVLC](https://www.videolan.org/vlc/libvlc.html), the engine behind VLC.

**Website:** [hardhatmediaplayer.com](https://hardhatmediaplayer.com) · **License:** [GPL-3.0-or-later](LICENSE)

> Hard Hat Media Player is an independent project. It uses VideoLAN's libVLC library but is not made by,
> affiliated with, or endorsed by VideoLAN. "VLC" is a trademark of VideoLAN.

## Features

- **Plays almost anything** libVLC plays: local files, folders, network streams (http, rtsp, udp, …).
- **Library**: Home (Continue Watching with resume points and thumbnails, Listen Again, frequent folders and
  playlists; right-click an item to remove it from these lists), Videos (folders, episodes in natural order with
  autoplay), Music (songs, albums, artists, genres from tags), Network, Playlists, History. Search, sort and
  right-click menus everywhere.
- **Player**: auto-hiding floating controls, seek and volume bars you can click or drag, shuffle, repeat,
  speed, subtitle and audio tracks, external subtitle files, true fullscreen (the taskbar gets out of the way).
- **Subtitles and audio buttons** in the controls: tracks, subtitle and audio delay sliders (−5 s to +5 s), the
  audio output device, and **per-folder presets**: pick a track or fix the sync once and the next episode in that
  folder starts the same way. See [Subtitles](#subtitles).
- **On-screen feedback** for every action (volume, seeking, speed, tracks…) with the matching shortcut,
  and a keyboard cheat sheet on **F1** or **?**.
- **For language learners**: a second subtitle at the top (e.g. English over Italian), optionally blurred until you
  point at it, plus replay / previous / next line and pause after each line. See [Learning a language](#learning-a-language-two-subtitles-at-once).
- **Online subtitles** (VLsub-style) from OpenSubtitles.com: search by file hash or by name, download, and
  optionally fetch exact matches automatically when a video has none. See [Subtitles](#subtitles).

## Download and run

Get the latest version from the [Releases page](../../releases):

- **`HardHatMediaPlayer-<version>-setup-win-x64.exe`**: the installer (Start menu shortcut, uninstaller, no admin
  rights needed).
- `HardHatMediaPlayer-<version>-portable-win-x64.zip`: unzip anywhere and run `HardHatMediaPlayer.exe`.

Windows 10 or 11, 64-bit; .NET and libVLC are included.

### Build from source

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows 10/11.
2. Clone the repository and run:

   ```bash
   dotnet run --project src/HardHatMediaPlayer
   ```

You can pass files or folders on the command line, or drag them onto the window.

## Keyboard shortcuts

| Key | Action |
| --- | --- |
| Space | Play / pause |
| ← / → | Jump 10 s (Shift: 3 s) |
| ↑ / ↓, mouse wheel | Volume |
| M | Mute |
| F, F11, double-click | Fullscreen (Esc to leave) |
| N / P / S | Next / previous / stop |
| [ / ] / = | Slower / faster (0.1× steps, 0.3×–3×) / normal speed |
| V / B | Next subtitle track / next audio track |
| Shift+V | Second subtitle (translation) on / next file |
| C | Blur the translation (point at it to read it) |
| R / A / D | Replay the current subtitle line / previous line / next line |
| E | Pause after each subtitle line |
| G / H | Subtitle delay −50 ms / +50 ms |
| J / K | Audio delay −50 ms / +50 ms |
| Q | Show or hide the play queue |
| Ctrl+O, Ctrl+Shift+O, Ctrl+N | Open file / folder / network stream |
| Ctrl+B, Ctrl+F | Show or hide the library / search it |
| Alt+← / Alt+→, Backspace | Library back / forward |
| F1 or ? | Show all shortcuts |

## Subtitles

Open **… → Find subtitles online…**, or the subtitles button in the player → **Find subtitles online…**.
Search **by hash** (subtitles made for that exact file) or **by name**, pick a result, and choose **Download and use**.
The file is saved next to the video as `video.lang.srt`, so it loads automatically next time.

With **Download automatically** on, a video that starts without subtitles gets the best exact (hash) match in your
languages. Name-only matches are never applied automatically.

This uses the [OpenSubtitles.com REST API](https://opensubtitles.stoplight.io/docs/opensubtitles-api), which needs
your own free API key:

1. Create a free account at opensubtitles.com and get an API key at <https://www.opensubtitles.com/consumers>.
2. In the Find subtitles window, choose **Account…** and paste the key. Username and password are optional and raise
   the daily download limit. The password is stored encrypted for your Windows account (DPAPI).

### Learning a language: two subtitles at once

Show the language you're learning at the bottom and a translation at the top, for example Italian below and English
above. Pick the main subtitle as usual, then **subtitles button → Second subtitle (translation)** and choose a file
(or press **Shift+V**). The second subtitle comes from a subtitle file (`.srt`, `.vtt`, `.ass` or `.ssa`): one next
to the video (`video.en.srt`), one you choose, or one downloaded with **Find subtitles online**. Subtitles built into
the video file can be the main subtitle but not the second one, because libVLC draws only one subtitle track.

- **Blur the translation** (**C**): the top line stays blurred until you point at it, so you try the other language first.
- **Line by line**: **R** replays the current line, **A** / **D** jump to the previous / next line, and
  **E** pauses at the end of every line so you can read and repeat it (Space continues).
- The second subtitle's language is remembered per folder, like the other subtitle choices. The subtitle delay
  (**G / H**) moves both subtitles.

### Sync and presets

The subtitles and audio buttons on the right of the controls each have a delay slider (−5 s to +5 s in 50 ms steps;
**G / H** and **J / K** do the same from the keyboard). Delays, and the subtitle and audio track you pick, are
remembered **per folder**: the next file from the same folder (the next episode) starts with the same delays and
the tracks in the same languages. **Forget this folder's choices** in either menu clears them.

In folders without a choice, **Default languages…** (e.g. `ro, en`) picks the first subtitle and audio track in
one of those languages; leave it empty to let the file decide.

## Your data

Library folders, playlists, history, the tag cache and thumbnails live in `%APPDATA%\HardHatMediaPlayer`.
Set the `HARDHAT_DATA` environment variable to use another folder (portable copies, testing).
Data from the project's earlier name (`%APPDATA%\VlcRedesign`) is copied over automatically on first start.

## Contributing

Contributions are welcome: bug reports, ideas, translations, code. See [CONTRIBUTING.md](CONTRIBUTING.md).

### Project layout

```
src/HardHatMediaPlayer/
  MainWindow.xaml(.cs)   window chrome, rail, library panel, video + overlay controls, fullscreen, OSD
  Themes/                colours, control styles, icon geometries, the app mark (AppMark.xaml)
  Controls/              icon renderer, pixel cat, converters, menus, accessible library list
  ViewModels/            player (libVLC), library panel navigation, library pages
  Services/              JSON store, folder scanning, tag reading, OpenSubtitles client
  Views/                 dialogs (prompt/confirm, subtitle search)
scripts/make-icon.ps1    renders Assets/app.ico from Themes/AppMark.xaml
scripts/build-release.ps1 builds the installer and portable zip into artifacts/
installer/               Inno Setup script for the Windows installer
```

## Credits and licenses

Hard Hat Media Player is licensed under the **GNU General Public License v3.0 or later**; see [LICENSE](LICENSE).

- Interface based on [“VLC Redesign”](https://www.figma.com/community/file/1691465999134370980/vlc-redesign) by
  Juxtopposed (Figma Community, CC BY 4.0), shown in [this video](https://youtu.be/ERFeOgqk324).
- Playback by libVLC and LibVLCSharp from VideoLAN (LGPL 2.1+).
- App icon: [“Hard Hat”](https://thenounproject.com/icon/hard-hat-7630923/) by sentya irma from Noun Project
  (CC BY 3.0), recoloured.

Full details of third-party components are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
