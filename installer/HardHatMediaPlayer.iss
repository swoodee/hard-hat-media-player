; Inno Setup script for the Windows installer. Built by scripts\build-release.ps1, which passes:
;   /DAppVersion=0.1.0  /DPublishDir=<dotnet publish output>  /DOutputDir=<folder for the setup .exe>
; Installs per user (no admin prompt) into %LOCALAPPDATA%\Programs\Hard Hat Media Player.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "Hard Hat Media Player"
#define AppExe "HardHatMediaPlayer.exe"
#define ProgId "HardHatMediaPlayer.Media"

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstaller.
AppId={{7D3F2A61-4C8E-4B9A-9E1D-2F6B5C0A8E47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Hard Hat Media Player contributors
AppPublisherURL=https://hardhatmediaplayer.com
AppSupportURL=https://github.com/swoodee/hard-hat-media-player/issues
AppUpdatesURL=https://github.com/swoodee/hard-hat-media-player/releases
AppCopyright=Copyright © 2026 Hard Hat Media Player contributors
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; No license page: the GPL doesn't ask users to accept anything to run the program.
; build-release.ps1 puts LICENSE.txt and THIRD-PARTY-NOTICES.md next to the app instead.
SetupIconFile=..\src\HardHatMediaPlayer\Assets\app.ico
WizardSmallImageFile=wizard-small.png,wizard-small@2x.png
WizardImageFile=wizard-large.png,wizard-large@2x.png
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=HardHatMediaPlayer-{#AppVersion}-setup-win-x64
ChangesAssociations=yes
CloseApplications=yes

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Open with" entries only: existing default apps are left alone. Windows can offer
; Hard Hat as a default through Settings > Apps > Default apps.
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "Media file"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\HardHatMediaPlayer\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\HardHatMediaPlayer\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "A free, open-source media player built on libVLC."
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\HardHatMediaPlayer\Capabilities"; Flags: uninsdeletevalue
#define Ext(E) \
  "Root: HKA; Subkey: ""Software\Classes\" + E + "\OpenWithProgids""; ValueType: string; ValueName: """ + ProgId + """; ValueData: """"; Flags: uninsdeletevalue" + NewLine + \
  "Root: HKA; Subkey: ""Software\Classes\Applications\" + AppExe + "\SupportedTypes""; ValueType: string; ValueName: """ + E + """; ValueData: """"" + NewLine + \
  "Root: HKA; Subkey: ""Software\HardHatMediaPlayer\Capabilities\FileAssociations""; ValueType: string; ValueName: """ + E + """; ValueData: """ + ProgId + """"
; Same lists as Services\MediaScanner.cs.
#emit Ext(".mp4")
#emit Ext(".mkv")
#emit Ext(".avi")
#emit Ext(".mov")
#emit Ext(".wmv")
#emit Ext(".flv")
#emit Ext(".webm")
#emit Ext(".m4v")
#emit Ext(".mpg")
#emit Ext(".mpeg")
#emit Ext(".ts")
#emit Ext(".m2ts")
#emit Ext(".mts")
#emit Ext(".3gp")
#emit Ext(".ogv")
#emit Ext(".vob")
#emit Ext(".divx")
#emit Ext(".rmvb")
#emit Ext(".asf")
#emit Ext(".mp3")
#emit Ext(".flac")
#emit Ext(".wav")
#emit Ext(".aac")
#emit Ext(".m4a")
#emit Ext(".ogg")
#emit Ext(".oga")
#emit Ext(".opus")
#emit Ext(".wma")
#emit Ext(".aiff")
#emit Ext(".aif")
#emit Ext(".ape")
#emit Ext(".mka")
#emit Ext(".alac")
#emit Ext(".wv")
#emit Ext(".mpc")
#emit Ext(".ac3")
#emit Ext(".dts")

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; The library, playlists and settings in %APPDATA%\HardHatMediaPlayer are kept on uninstall,
; so reinstalling or upgrading doesn't lose them.
