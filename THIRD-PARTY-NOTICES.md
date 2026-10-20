# Third-party notices

Hard Hat Media Player is licensed under GPL-3.0-or-later. It uses or is based on the following
third-party work, each under its own license. All of these licenses are compatible with GPL-3.0.

| Component | Use | License | Source |
| --- | --- | --- | --- |
| libVLC (VideoLAN.LibVLC.Windows) | Media playback engine, shipped as DLLs in `libvlc/` | LGPL-2.1-or-later | <https://code.videolan.org/videolan/vlc> |
| LibVLCSharp, LibVLCSharp.WPF | .NET bindings for libVLC | LGPL-2.1-or-later | <https://code.videolan.org/videolan/LibVLCSharp> |
| CommunityToolkit.Mvvm | MVVM helpers | MIT | <https://github.com/CommunityToolkit/dotnet> |
| System.Security.Cryptography.ProtectedData (part of the .NET Windows Desktop runtime) | Encrypting the saved OpenSubtitles password | MIT | <https://github.com/dotnet/runtime> |
| .NET / WPF | Runtime and UI framework | MIT | <https://github.com/dotnet> |
| “VLC Redesign” Figma prototype by Juxtopposed | Interface design the UI is based on | CC BY 4.0 | <https://www.figma.com/community/file/1691465999134370980/vlc-redesign> |
| Lucide-style icon shapes | Interface icons (redrawn) | ISC | <https://lucide.dev> |
| “Hard Hat” by sentya irma from Noun Project | App icon (recoloured; original SVG in `src/HardHatMediaPlayer/Assets/hard-hat-7630923.svg`) | CC BY 3.0 | <https://thenounproject.com/icon/hard-hat-7630923/> |

## Notes for redistributors

- **libVLC (LGPL)**: release builds ship the unmodified libVLC DLLs from the `VideoLAN.LibVLC.Windows`
  package, dynamically loaded. If you distribute binaries, include this notice and the LGPL text, and point
  to VideoLAN's source (links above). Users must remain able to replace those DLLs with their own build.
- **Design (CC BY 4.0)**: keep the credit to Juxtopposed in the README and the About box.
- **App icon (CC BY 3.0)**: keep the credit to sentya irma / Noun Project in the README and the About box.
  The icon is a separate asset under its own license, not covered by the GPL.
- **Trademarks**: "VLC" and the traffic-cone logo are VideoLAN trademarks and are not used for this app's
  name or icon.
