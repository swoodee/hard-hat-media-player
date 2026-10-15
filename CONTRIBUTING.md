# Contributing to Hard Hat Media Player

Thanks for helping! All kinds of contributions are welcome: bug reports, ideas, design feedback,
translations and code.

## Reporting a bug or asking for a feature

Open an [issue](../../issues) and include:

- what you did, what you expected, and what happened instead;
- your Windows version and the app version (… menu → About);
- for playback problems, the file type (e.g. MKV, H.265, 4K) or stream URL type. Please don't upload
  copyrighted media.

## Building

1. Install the [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) (or newer) on Windows 10/11.
2. Build and run:

   ```bash
   dotnet build src/HardHatMediaPlayer
   dotnet run --project src/HardHatMediaPlayer
   ```

To test without touching your own library, point the app at a scratch data folder first:

```bash
set HARDHAT_DATA=%TEMP%\hardhat-test
dotnet run --project src/HardHatMediaPlayer
```

## Making a change

1. Fork the repository and create a branch (`fix-seek-on-touch`, `feature-chapters`, …).
2. Keep changes focused; match the style of the surrounding code (MVVM: view models in `ViewModels/`,
   UI in XAML, services in `Services/`).
3. Make sure `dotnet build` passes without warnings and try your change in the app.
4. Open a pull request describing what changed and how you tested it. Screenshots help for UI changes.

The app icon (“Hard Hat” by sentya irma, Noun Project, CC BY 3.0) lives in `src/HardHatMediaPlayer/Themes/AppMark.xaml`. If you change it, regenerate the
icon file with `powershell -File scripts/make-icon.ps1`.

## License of contributions

By contributing, you agree that your contributions are licensed under the project's license,
**GPL-3.0-or-later** (see [LICENSE](LICENSE)).

## Be kind

Be respectful and constructive. Assume good intent, and help newcomers.
