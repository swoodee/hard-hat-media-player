# Renders the app mark (an orange hard hat) at the standard Windows icon sizes and packs them into a
# PNG-based .ico. The shapes match the "AppMark" drawing in MainWindow.xaml (24x24 design grid).
#
#   powershell -File scripts/make-icon.ps1 -Out src/VlcRedesign/Assets/app.ico
param([Parameter(Mandatory)][string]$Out)

Add-Type -AssemblyName System.Drawing

$dome  = [System.Drawing.Color]::FromArgb(255, 0xFF, 0x88, 0x00)  # interface accent
$ridge = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xAA, 0x4D)
$brim  = [System.Drawing.Color]::FromArgb(255, 0xD0, 0x6A, 0x00)

function Pt([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($size / 24.0, $size / 24.0)

    # Dome: top half of a circle centred at (12, 15), radius 8.
    $b = New-Object System.Drawing.SolidBrush $dome
    $g.FillPie($b, 4, 7, 16, 16, 180, 180)

    # Ridge down the middle, slightly raised above the dome.
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier((Pt 10.2 7.25), (Pt 11.4 6.8), (Pt 12.6 6.8), (Pt 13.8 7.25))
    $path.AddLine((Pt 13.8 7.25), (Pt 14.6 15))
    $path.AddLine((Pt 14.6 15), (Pt 9.4 15))
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $ridge), $path)

    # Brim: rounded bar from x 1.5 to 22.5, y 15 to 17.5.
    $r = 1.25
    $brimPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $brimPath.AddArc(1.5, 15, 2 * $r, 2.5, 90, 180)
    $brimPath.AddArc(22.5 - 2 * $r, 15, 2 * $r, 2.5, 270, 180)
    $brimPath.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $brim), $brimPath)

    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) { Render $s }

$ico = [IO.MemoryStream]::new()
$w = [IO.BinaryWriter]::new($ico)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                   # ICONDIRENTRY per size
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $Out -Parent)).Path + '\' + (Split-Path $Out -Leaf), $ico.ToArray())
"wrote $Out ($($ico.Length) bytes)"
