# Draws AstroHybrid's own icon (a white star on a blue rounded square, the app's sidebar logo) so the public
# app and launcher carry no game artwork. Writes AstroHybrid.App\Resources\astrohybrid.ico and astrohybrid.png.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root 'AstroHybrid.App\Resources'

function Draw([int]$n) {
    $bmp = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $r = [Math]::Max(2, [int]($n * 0.18))
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($n - 1 - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($n - 1 - 2 * $r, $n - 1 - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $n - 1 - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 0, 102, 204))), $path)
    $cx = $n / 2.0; $cy = $n / 2.0 + $n * 0.02; $outer = $n * 0.34; $inner = $outer * 0.42
    $points = New-Object 'Drawing.PointF[]' 10
    for ($i = 0; $i -lt 10; $i++) {
        $radius = if ($i % 2 -eq 0) { $outer } else { $inner }
        $angle = -[Math]::PI / 2 + $i * [Math]::PI / 5
        $points[$i] = New-Object Drawing.PointF ([float]($cx + $radius * [Math]::Cos($angle))), ([float]($cy + $radius * [Math]::Sin($angle)))
    }
    $g.FillPolygon([Drawing.Brushes]::White, $points)
    $g.Dispose()
    return $bmp
}

function Png($bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$big = Draw 192
$big.Save((Join-Path $out 'astrohybrid.png'), [Drawing.Imaging.ImageFormat]::Png)
$sizes = @(256, 64, 48, 32, 16)
$pngs = @($sizes | ForEach-Object { ,(Png (Draw $_)) })
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $b = if ($n -ge 256) { 0 } else { $n }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $out 'astrohybrid.ico'), $ico.ToArray())
"wrote astrohybrid.ico and astrohybrid.png"
