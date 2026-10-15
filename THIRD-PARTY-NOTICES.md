# Third-party notices

Hard Hat Media Player is licensed under GPL-3.0-or-later. It uses or is based on the following
third-party work, each under its own license. All of these licenses are compatible with GPL-3.0.

| Component | Use | License | Source |
| --- | --- | --- | --- |
| libVLC (VideoLAN.LibVLC.Windows) | Media playback engine, shipped as DLLs in `libvlc/` | LGPL-2.1-or-later | <https://code.videolan.org/videolan/vlc> |
| LibVLCSharp, LibVLCSharp.WPF | .NET bindings for libVLC | LGPL-2.1-or-later | <https://code.videolan.org/videolan/LibVLCSharp> |
| CommunityToolkit.Mvvm | MVVM helpers | MIT | <https://github.com/CommunityToolkit/dotnet> |
| System.Security.Cryptography.ProtectedData | Encrypting the saved OpenSubtitles password | MIT | <https://github.com/dotnet/runtime> |
| .NET / WPF | Runtime and UI framework | MIT | <https://github.com/dotnet> |
| “VLC Redesign” Figma prototype by Juxtopposed | Interface design the UI is based on | CC BY 4.0 | <https://www.figma.com/community/file/1691465999134370980/vlc-redesign> |
| Lucide-style icon shapes | Interface icons (redrawn) | ISC | <https://lucide.dev> |

## Notes for redistributors

- **libVLC (LGPL)**: release builds ship the unmodified libVLC DLLs from the `VideoLAN.LibVLC.Windows`
  package, dynamically loaded. If you distribute binaries, include this notice and the LGPL text, and point
  to VideoLAN's source (links above). Users must remain able to replace those DLLs with their own build.
- **Design (CC BY 4.0)**: keep the credit to Juxtopposed in the README and the About box.
- **Trademarks**: "VLC" and the traffic-cone logo are VideoLAN trademarks and are not used for this app's
  name or icon.
