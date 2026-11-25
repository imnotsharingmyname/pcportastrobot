# Assembles the public download of AstroHybrid (AstroBot PC port) from this PC's build: the app and the
# classic launcher, the start script, AnyPS5's relinker, and the runtime that goes next to the game
# (AnyPS5 system libraries, kyty_gpu.dll, the host module, NVIDIA runtime files with their licences).
# Nothing from the game is included: no executable, no game files, no saves, no pipeline cache. Each
# player's own dump is converted by the app's setup (Services\GameSetup.cs).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File Tools\release\make_release.ps1 [-Out E:\...] [-Version 1.0.0]
param(
    [string]$Out = 'E:\AstroHybrid-Work\release',
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$name = "AstroBotPC-AstroHybrid-$Version"
$stage = Join-Path $Out $name
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$run = Join-Path $stage 'Build\RunHybrid'
New-Item -ItemType Directory -Force $run, (Join-Path $stage 'tools'), (Join-Path $stage 'licenses') | Out-Null

# The app (built with MSBuild, Release) and its libraries.
$app = Join-Path $root 'AstroHybrid.App\bin\Release'
Get-ChildItem $app -File | Where-Object { $_.Extension -in '.exe', '.dll' } | Copy-Item -Destination $stage
# The build's generated config: it carries the assembly binding redirects the app needs.
$config = Join-Path $root 'AstroHybrid.App\obj\Release\AstroHybrid.exe.config'
if (-not (Select-String -Path $config -Pattern 'bindingRedirect' -Quiet)) { throw 'generated config has no binding redirects' }
Copy-Item $config (Join-Path $stage 'AstroHybrid.exe.config')
Copy-Item (Join-Path $root 'Run-AstroHybrid.bat') $stage
# RecompLauncher reads it from the top of the zip, next to AstroHybrid.exe.
Copy-Item (Join-Path $root 'recomplauncher.json') $stage
$noticePath = Join-Path $root 'NOTICE'
if (Test-Path $noticePath) { Copy-Item $noticePath $stage }
$zstdLicense = Join-Path $root 'licenses\ZstdSharp.license.txt'
if (Test-Path $zstdLicense) { Copy-Item $zstdLicense (Join-Path $stage 'licenses') }
foreach ($extraBat in 'Run-AstroHybrid-DLSS.bat', 'Run-AstroHybrid-FG.bat') {
    $batPath = Join-Path $root $extraBat
    if (Test-Path $batPath) { Copy-Item $batPath $stage }
}
# The Linux (Wine) start script the README's Linux section uses (#123, #124), with LF line endings:
# bash fails on CRLF, which a Windows checkout may have.
$linuxSh = Join-Path $root 'Run-AstroHybrid-Linux.sh'
if (Test-Path $linuxSh) {
    $text = [IO.File]::ReadAllText($linuxSh) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $stage 'Run-AstroHybrid-Linux.sh'), $text, (New-Object Text.UTF8Encoding $false))
}

# AnyPS5's relinker (needs only the Universal C runtime that ships with Windows 10/11).
Copy-Item (Join-Path $root 'Build\anyps5\core\relinker\relinker.exe') (Join-Path $stage 'tools')

# Runtime next to the game, taken from the working install (hard links resolve to real copies).
$src = Join-Path $root 'Build\RunHybrid'
New-Item -ItemType Directory -Force (Join-Path $run 'libs') | Out-Null
Get-ChildItem (Join-Path $src 'libs') -File | ForEach-Object {
    $bytes = [IO.File]::ReadAllBytes($_.FullName)
    if ($bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "not a Windows module: $($_.Name)" }
    Copy-Item $_.FullName (Join-Path $run 'libs')
}
$runtime = 'kyty_gpu.dll', 'astrohybrid_host.dll', 'u59-preset.cmd', 'u59-preset.json',
           'nvngx_dlss.dll', 'nvngx_dlssg.dll', 'NvLowLatencyVk.dll',
           'sl.interposer.dll', 'sl.common.dll', 'sl.dlss_g.dll', 'sl.reflex.dll', 'sl.pcl.dll',
           'amd_fidelityfx_loader_vk.dll', 'amd_fidelityfx_upscaler_vk.dll', 'amd_fidelityfx_framegeneration_vk.dll'
foreach ($f in $runtime) { Copy-Item (Join-Path $src $f) $run }
foreach ($f in 'nvngx_dlss.license.txt', 'streamline.license.txt', 'reflex.license.txt', 'amd_fsr.license.txt') {
    Copy-Item (Join-Path $src $f) $run
    Copy-Item (Join-Path $src $f) (Join-Path $stage 'licenses')
}
$pl = Join-Path $src 'PipelineLists'
if (Test-Path $pl) {
    New-Item -ItemType Directory -Force (Join-Path $run 'PipelineLists') | Out-Null
    Copy-Item (Join-Path $pl '*.kyplst') (Join-Path $run 'PipelineLists') -ErrorAction SilentlyContinue
}
# Intel XeSS Super Resolution (ASTROHYBRID_XESS): libxess.dll from the XeSS SDK's bin folder
# (https://github.com/intel/xess), redistributable unmodified under the Intel Simplified Software
# License; put it and that licence (as xess.license.txt) in Build\RunHybrid. Optional: without it
# the XeSS choice keeps the game's own upscaling.
if (Test-Path (Join-Path $src 'libxess.dll')) {
    if (-not (Test-Path (Join-Path $src 'xess.license.txt'))) { throw 'libxess.dll without xess.license.txt' }
    Copy-Item (Join-Path $src 'libxess.dll') $run
    Copy-Item (Join-Path $src 'xess.license.txt') $run
    Copy-Item (Join-Path $src 'xess.license.txt') (Join-Path $stage 'licenses')
} else {
    Write-Warning 'Build\RunHybrid\libxess.dll missing: the download will have no XeSS runtime'
}

# System fonts: the game lays out its menu and prompt text in the console's system fonts. The Noto
# substitutes AnyPS5 accepts (SIL Open Font License, Tools\release\get_fonts.ps1 downloads them) go in
# anyps5-fonts next to the game; without them button prompts and settings have no text.
$fonts = Join-Path (Split-Path $Out -Parent) 'fonts\noto'
$fontFiles = Get-ChildItem $fonts -File | Where-Object { $_.Extension -in '.ttf', '.ttc' }
if ($fontFiles.Count -lt 20) { throw "fonts missing in $fonts (run Tools\release\get_fonts.ps1)" }
New-Item -ItemType Directory -Force (Join-Path $run 'anyps5-fonts') | Out-Null
$fontFiles | Copy-Item -Destination (Join-Path $run 'anyps5-fonts')
Get-ChildItem $fonts -Filter 'OFL-*.txt' | Copy-Item -Destination (Join-Path $stage 'licenses')

# Readme for players.
@"
AstroHybrid $Version - Astro Bot (PPSA21567) native PC port
==========================================================

You need your own decrypted PS5 dump of Astro Bot (a dump folder or standalone .zar).
No game files are included here.

1. Extract the zip into a new, empty folder with about 1 GB free (the game itself stays where your
   source is). To update an existing install, use the app's Update button or extract over that folder.
2. Start AstroHybrid.exe. Select your dump folder or any standalone .zar and click Check source.
   The quick check validates the index and required startup files with byte progress; optional game-data
   blocks are not all decompressed before Install. Setup builds the game for Windows in about 2 minutes
   and only extracts the startup files it needs; your source is only read, never changed.
3. The app picks the best settings for your PC once; change them on the Graphics and Controller pages.
4. Press Play.

Tested version: 01.019.000. Other versions are accepted: faster lighting and real-time game speed turn
themselves off when the version's code differs, everything else works the same way.
NVIDIA: DLSS needs an RTX card, frame generation an RTX 40 or 50.
Intel XeSS (Graphics page, Upscaling): Intel Arc (XMX) and most other GPUs (Iris Xe, GTX 10+, Radeon RX).
Controllers: DualSense, DualShock 4, Xbox, Switch Pro and compatible pads, generic pads.
In game: F2 performance overlay, F11 or Alt+Enter fullscreen.
Logs: Logs\hybrid-run.*.log, Logs\setup.log.

Source and full results log: https://github.com/imnotsharingmyname/pcportastrobot
Intel processors: setup converts the game's AMD-only instructions for you (updating from 1.0.2 or older:
the app offers to convert the game again at its first start).
Language and VSync: Advanced and Graphics pages.

Not affiliated with Sony Interactive Entertainment, Team ASOBI, NVIDIA or AMD. NVIDIA and AMD FSR runtime
files and the Noto fonts (SIL Open Font License) are redistributed under the licences in the licenses folder.
"@ | Set-Content -Encoding UTF8 (Join-Path $stage 'README.txt')

# Guard: nothing from a game dump or a play session.
$bad = Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -match '^AstroBot.*\.exe$|\.elf$|\.guest\.prx$|eboot|param\.(json|sfo|sfx)$|slot\d+\.bin$' -or
    $_.FullName -match '\\(app0|_sd_mem|_trophy|_PipelineCache|_HangTrace)\\'
}
$bad = $bad | Where-Object { $_.Name -ne 'AstroBot Launcher.exe' }
if ($bad) { $bad.FullName; throw 'game or session files found in the release' }

# System and processor in the name, so launchers pick the file for the PC (RecompLauncher).
$zip = Join-Path $Out "$name-Windows-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Files at the zip's root, no wrapper folder: the launcher's updater (1.2.2 and older) copies the archive's
# paths as they are into the install, so a wrapper folder put the update in a subfolder and the launcher
# stayed on the old version.
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
"{0}: {1:N0} files, {2:N1} MB staged, zip {3:N1} MB" -f $name, (Get-ChildItem $stage -Recurse -File).Count,
    ((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), ((Get-Item $zip).Length / 1MB)
