# Renders the app mark from src/HardHatMediaPlayer/Themes/AppMark.xaml (the same drawing the title bar uses)
# at the standard Windows icon sizes and packs them into a PNG-based .ico.
#
#   powershell -File scripts/make-icon.ps1
#   powershell -File scripts/make-icon.ps1 -Out some/other/app.ico
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\HardHatMediaPlayer\Themes\AppMark.xaml'),
    [string]$Out = (Join-Path $PSScriptRoot '..\src\HardHatMediaPlayer\Assets\app.ico')
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$dictionary = [System.Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Resolve-Path $Source)))
$mark = $dictionary['AppMark']
if ($mark -isnot [System.Windows.Media.DrawingImage]) { throw "No DrawingImage named AppMark in $Source" }

function Render([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawImage($mark, (New-Object System.Windows.Rect 0, 0, $size, $size))
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) { Render $s }

$ico = [IO.MemoryStream]::new()
$w = [IO.BinaryWriter]::new($ico)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                   # one ICONDIRENTRY per size
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()

$target = [IO.Path]::GetFullPath($Out)
[IO.File]::WriteAllBytes($target, $ico.ToArray())
"wrote $target ($($ico.Length) bytes)"
