# Packages the website in site/ for upload to your own server (docs/PUBLISHING.md, step 8).
# Creates dist/site (for scp) and dist/hardhatmediaplayer-site.zip (for a control panel's File Manager).
param(
    [string]$Site = (Join-Path $PSScriptRoot '..\site'),
    [string]$Out = (Join-Path $PSScriptRoot '..\dist')
)
$ErrorActionPreference = 'Stop'

$Site = (Resolve-Path $Site).Path
$index = Join-Path $Site 'index.html'
if (-not (Test-Path $index)) { throw "No index.html in $Site" }

# The GitHub links in the page stay broken until the placeholder is replaced.
$placeholders = @(Select-String -Path $index -Pattern 'YOUR-USERNAME' -SimpleMatch |
    Where-Object { $_.Line -match 'href=' })
if ($placeholders.Count -gt 0) {
    Write-Warning "index.html still links to YOUR-USERNAME on $($placeholders.Count) line(s); replace it with your GitHub username."
}

$Out = (New-Item -ItemType Directory -Force $Out).FullName
$staging = Join-Path $Out 'site'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory $staging | Out-Null

# CNAME is only for GitHub Pages; a web server would serve it as a stray file.
Get-ChildItem $Site -Force | Where-Object { $_.Name -ne 'CNAME' } |
    Copy-Item -Destination $staging -Recurse

$zip = Join-Path $Out 'hardhatmediaplayer-site.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip

Get-ChildItem $staging -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($staging.Length + 1) }
"Ready: $zip"
