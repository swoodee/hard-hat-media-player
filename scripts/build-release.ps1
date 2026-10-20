# Builds the release downloads into artifacts/:
#   HardHatMediaPlayer-<version>-setup-win-x64.exe     installer (Inno Setup)
#   HardHatMediaPlayer-<version>-portable-win-x64.zip  no-install copy
# The version comes from src/HardHatMediaPlayer/HardHatMediaPlayer.csproj unless -Version is given.
param(
    [string]$Version,
    [string]$Iscc
)
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\HardHatMediaPlayer\HardHatMediaPlayer.csproj'
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'

if (-not $Version) { $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' should look like 1.2.3" }

if (-not $Iscc) {
    $Iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $Iscc) { throw 'Inno Setup (ISCC.exe) not found; install it from https://jrsoftware.org or pass -Iscc' }
}

if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }

dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
# The license and notices travel with every copy of the app (installed and portable).
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $publish 'LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $publish

$zip = Join-Path $artifacts "HardHatMediaPlayer-$Version-portable-win-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip

& $Iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$artifacts" (Join-Path $root 'installer\HardHatMediaPlayer.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }

Get-ChildItem $artifacts -File | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length / 1MB) }
