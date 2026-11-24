# Reproducible build of the Astro Bot Windows runtime from the project root.
# Steps: toolchain -> AnyPS5 (pinned) -> configure/build -> unwrap SELFs -> relink -> runtime layout.
# Usage: powershell -ExecutionPolicy Bypass -File Tools\build.ps1 [-SkipToolchain] [-SkipBuild] [-SkipGame] [-GameDumpPath DUMP]
# -SkipGame stops after the build (relinker, system libraries, libAstroHybridHost): no game dump needed.
param(
    [switch]$SkipToolchain,
    [switch]$SkipBuild,
    [switch]$SkipGame,
    [string]$GameDumpPath = $env:ASTROHYBRID_GAME_DUMP
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Tools = "$Root\Tools"
$Game = if ($GameDumpPath) { (Resolve-Path -LiteralPath $GameDumpPath).Path } else { "$Root\Game\PPSA21567-app" }
if (-not $SkipGame -and -not (Test-Path "$Game\eboot.bin")) { throw "No game dump at $Game (pass -GameDumpPath, or -SkipGame to only build)" }
$AnyPS5Commit = '319621ba2e578bd053b0baba2877fe70520e0737' # Source/anyps5/*.patch is the canonical series
$AnyPS5Url = 'https://github.com/boykopovar/AnyPS5.git'
$WinLibs = 'https://github.com/brechtsanders/winlibs_mingw/releases/download/15.2.0posix-14.0.0-ucrt-r7/winlibs-x86_64-posix-seh-gcc-15.2.0-mingw-w64ucrt-14.0.0-r7.zip'

function Step($m) { Write-Host "== $m" -ForegroundColor Cyan }
function Check { if ($LASTEXITCODE -ne 0) { throw "failed with exit code $LASTEXITCODE" } }

# 7-Zip and Git may be installed anywhere (installer, scoop, winget): find them instead of assuming
# C:\Program Files. Git's root is three levels above its exec path (<root>\mingw64\libexec\git-core).
$SevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source
if (-not $SevenZip) { $SevenZip = 'C:\Program Files\7-Zip\7z.exe' }
if (-not (Test-Path $SevenZip)) { throw '7-Zip (7z.exe) not found: install it or put it on PATH.' }
$GitRoot = (Resolve-Path ((git --exec-path) + '/../../..')).Path

if (-not $SkipToolchain) {
    if (-not (Test-Path "$Tools\winlibs\mingw64\bin\g++.exe")) {
        Step 'Downloading WinLibs GCC 15.2.0 (UCRT, the toolchain AnyPS5 CI uses)'
        New-Item -ItemType Directory -Force "$Tools\dl" | Out-Null
        curl.exe -sSfL -o "$Tools\dl\winlibs.zip" $WinLibs; Check
        & $SevenZip x "$Tools\dl\winlibs.zip" "-o$Tools\winlibs" -y | Out-Null; Check
    }
    $Patches = @(Get-ChildItem -LiteralPath "$Root\Source\anyps5" -Filter '*.patch' -File | Sort-Object Name)
    if ($Patches.Count -eq 0) { throw 'The canonical Source\anyps5 patch series is missing.' }
    if (-not (Test-Path "$Tools\AnyPS5\.git")) {
        Step 'Cloning AnyPS5'
        git -c core.autocrlf=false clone --no-checkout --filter=blob:none $AnyPS5Url "$Tools\AnyPS5"; Check
    }
    # The patches are byte-exact against LF sources: core.autocrlf=true (Git for Windows' default) would
    # check the sources out with CRLF and no patch would apply.
    git -C "$Tools\AnyPS5" config core.autocrlf false; Check
    # Fetch the exact revision, not a moving main branch or an arbitrary shallow-history window.
    git -C "$Tools\AnyPS5" fetch --filter=blob:none $AnyPS5Url $AnyPS5Commit; Check
    git -C "$Tools\AnyPS5" checkout -B astrobot $AnyPS5Commit; Check
    foreach ($patch in $Patches) {
        Step "Applying $($patch.Name)"
        git -C "$Tools\AnyPS5" -c user.name=local -c user.email=local@localhost am --3way --keep-cr $patch.FullName; Check
    }
    git -C "$Tools\AnyPS5" submodule update --init --recursive --depth 1 -j 8; Check
}

$env:PATH = "$Tools\winlibs\mingw64\bin;$GitRoot\cmd;$GitRoot\usr\bin;$env:PATH"
# Git's CA bundle lets FFmpeg's prebuilt download verify TLS (mingw64 in older Git for Windows, ucrt64 in newer).
$env:SSL_CERT_FILE = @("$GitRoot\ucrt64\etc\ssl\certs\ca-bundle.crt", "$GitRoot\mingw64\etc\ssl\certs\ca-bundle.crt",
                       "$GitRoot\usr\ssl\certs\ca-bundle.crt") | Where-Object { Test-Path $_ } | Select-Object -First 1
$B = "$Root\Build\anyps5"
if (-not $SkipBuild) {
    Step 'Configuring AnyPS5'
    $py = (Get-Command python).Source -replace '\\', '/'
    cmake -S "$Tools\AnyPS5" -B $B -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=gcc -DCMAKE_CXX_COMPILER=g++ "-DPython3_EXECUTABLE=$py"; Check
    Step 'Building relinker and runtime'
    cmake --build $B --parallel; Check
    Step 'Building system libraries'
    cmake --build $B --target libs --parallel; Check
}
if ($SkipGame) {
    Step "Done (no game step): $B\core\relinker\relinker.exe and $B\core\libs\libs"
    return
}

Step 'Unwrapping SELF containers (segments are unencrypted; no keys involved)'
$In = "$Root\Build\elf-input"
New-Item -ItemType Directory -Force "$In\sce_module" | Out-Null
python -I "$Tools\AnyPS5\tools\unwrap_self.py" "$Game\eboot.bin" "$In\eboot.elf"; Check
foreach ($m in 'libc', 'libSceNpCppWebApi') {
    python -I "$Tools\AnyPS5\tools\unwrap_self.py" "$Game\sce_module\$m.prx" "$In\sce_module\$m.prx"; Check
}

Step 'Relinking to a Windows PE'
$Run = "$Root\Build\Run"
New-Item -ItemType Directory -Force "$Run\libs" | Out-Null
# Share the selected dump as app0; app0.zar is linked/copied beside it and stale archives are removed.
# --split-modules: the relinker writes *.guest.prx into app0\sce_module, so that folder is a real copy
# (one link per other dump folder) and the source dump is never written.
python -I "$Tools\hybrid\make_run_hybrid.py" --layout-only --split-modules --game "$Game" --output "$Run"; Check
& "$B\core\relinker\relinker.exe" --windows --registry "$In\eboot.elf" "$Run\AstroBot.exe"; Check

Step 'Assembling runtime libraries'
Copy-Item "$B\core\libs\libs\*.prx", "$B\core\libs\libs\*.dll" "$Run\libs\" -Force
foreach ($d in 'libgcc_s_seh-1.dll', 'libstdc++-6.dll', 'libwinpthread-1.dll') {
    Copy-Item "$Tools\winlibs\mingw64\bin\$d" "$Run\libs\" -Force
}
Step "Done: $Run\AstroBot.exe"
