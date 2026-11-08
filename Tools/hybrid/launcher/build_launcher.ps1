# Builds "AstroBot Launcher.exe" in the project root with the .NET Framework compiler that ships with Windows.
# The icon and logo are made from the game's own icon (Game\...\sce_sys\icon0.png, read only) into Build\launcher,
# so they never enter git. Usage: powershell -NoProfile -ExecutionPolicy Bypass -File Tools\hybrid\launcher\build_launcher.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$out = Join-Path $root 'Build\launcher'
[void][IO.Directory]::CreateDirectory($out)
$source = Join-Path $root 'Game\PPSA21567-app\sce_sys\icon0.png'

function Resize([Drawing.Image]$img, [int]$n) {
    $bmp = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.DrawImage($img, 0, 0, $n, $n)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$img = [Drawing.Image]::FromFile($source)
[IO.File]::WriteAllBytes((Join-Path $out 'logo.png'), (Resize $img 192))
$sizes = @(256, 64, 48, 32, 16)
$pngs = @($sizes | ForEach-Object { ,(Resize $img $_) })
$img.Dispose()
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
[IO.File]::WriteAllBytes((Join-Path $out 'astrobot.ico'), $ico.ToArray())

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $root 'AstroBot Launcher.exe'
& $csc /nologo /target:winexe /optimize+ "/out:$exe" "/win32icon:$(Join-Path $out 'astrobot.ico')" `
    "/resource:$(Join-Path $out 'logo.png'),logo.png" /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $PSScriptRoot 'AstroBotLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
"built $exe"
