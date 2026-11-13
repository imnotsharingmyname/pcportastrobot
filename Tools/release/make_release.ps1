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
# The classic launcher, built with AstroHybrid's own icon (no game artwork in the download).
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$res = Join-Path $root 'AstroHybrid.App\Resources'
& $csc /nologo /target:winexe /optimize+ "/out:$(Join-Path $stage 'AstroBot Launcher.exe')" "/win32icon:$(Join-Path $res 'astrohybrid.ico')" `
    "/resource:$(Join-Path $res 'astrohybrid.png'),logo.png" /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $root 'Tools\hybrid\launcher\AstroBotLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }

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
           'sl.interposer.dll', 'sl.common.dll', 'sl.dlss_g.dll', 'sl.reflex.dll', 'sl.pcl.dll'
foreach ($f in $runtime) { Copy-Item (Join-Path $src $f) $run }
foreach ($f in 'nvngx_dlss.license.txt', 'streamline.license.txt', 'reflex.license.txt') {
    Copy-Item (Join-Path $src $f) $run
    Copy-Item (Join-Path $src $f) (Join-Path $stage 'licenses')
}

# Readme for players.
@"
AstroHybrid $Version - Astro Bot (PPSA21567) native PC port
==========================================================

You need your own decrypted PS5 dump of Astro Bot (the folder with eboot.bin, sce_module and sce_sys).
No game files are included here.

1. Extract this folder somewhere with about 1 GB free (the game itself stays where your dump is).
2. Start AstroHybrid.exe. Setup asks for your dump folder, then builds the game for Windows from it
   (about 2 minutes; your dump is only read, never changed).
3. The app picks the best settings for your PC once; change them on the Graphics and Controller pages.
4. Press Play.

Tested version: 01.019.000. Other versions are accepted: faster lighting and real-time game speed turn
themselves off when the version's code differs, everything else works the same way.
NVIDIA: DLSS needs an RTX card, frame generation an RTX 40 or 50.
Controllers: DualSense, DualShock 4, Xbox, Switch Pro and compatible pads, generic pads.
In game: F2 performance overlay, F11 or Alt+Enter fullscreen.
Logs: Logs\hybrid-run.*.log, Logs\setup.log.

Source and full results log: https://github.com/imnotsharingmyname/pcportastrobot
Not affiliated with Sony Interactive Entertainment, Team ASOBI or NVIDIA. NVIDIA runtime files are
redistributed under the licences in the licenses folder.
"@ | Set-Content -Encoding UTF8 (Join-Path $stage 'README.txt')

# Guard: nothing from a game dump or a play session.
$bad = Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -match '^AstroBot.*\.exe$|\.elf$|\.guest\.prx$|eboot|param\.(json|sfo|sfx)$|slot\d+\.bin$' -or
    $_.FullName -match '\\(app0|_sd_mem|_trophy|_PipelineCache|_HangTrace)\\'
}
$bad = $bad | Where-Object { $_.Name -ne 'AstroBot Launcher.exe' }
if ($bad) { $bad.FullName; throw 'game or session files found in the release' }

$zip = Join-Path $Out "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
"{0}: {1:N0} files, {2:N1} MB staged, zip {3:N1} MB" -f $name, (Get-ChildItem $stage -Recurse -File).Count,
    ((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), ((Get-Item $zip).Length / 1MB)
