# Reproducible build of the Astro Bot Windows runtime from the project root.
# Steps: toolchain -> AnyPS5 (pinned) -> configure/build -> unwrap SELFs -> relink -> runtime layout.
# Usage: powershell -ExecutionPolicy Bypass -File Tools\build.ps1 [-SkipToolchain] [-SkipBuild]
param([switch]$SkipToolchain, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Tools = "$Root\Tools"
$Game = "$Root\Game\PPSA21567-app"
$AnyPS5Commit = '319621ba2'        # tested revision of boykopovar/AnyPS5 main; Source/patches/*.patch go on top
$WinLibs = 'https://github.com/brechtsanders/winlibs_mingw/releases/download/15.2.0posix-14.0.0-ucrt-r7/winlibs-x86_64-posix-seh-gcc-15.2.0-mingw-w64ucrt-14.0.0-r7.zip'
$SevenZip = 'C:\Program Files\7-Zip\7z.exe'

function Step($m) { Write-Host "== $m" -ForegroundColor Cyan }
function Check { if ($LASTEXITCODE -ne 0) { throw "failed with exit code $LASTEXITCODE" } }

if (-not $SkipToolchain) {
    if (-not (Test-Path "$Tools\winlibs\mingw64\bin\g++.exe")) {
        Step 'Downloading WinLibs GCC 15.2.0 (UCRT, the toolchain AnyPS5 CI uses)'
        New-Item -ItemType Directory -Force "$Tools\dl" | Out-Null
        curl.exe -sSfL -o "$Tools\dl\winlibs.zip" $WinLibs; Check
        & $SevenZip x "$Tools\dl\winlibs.zip" "-o$Tools\winlibs" -y | Out-Null; Check
    }
    if (-not (Test-Path "$Tools\AnyPS5\.git")) {
        Step 'Cloning AnyPS5'
        git clone https://github.com/boykopovar/AnyPS5.git "$Tools\AnyPS5"; Check
    }
    git -C "$Tools\AnyPS5" fetch --filter=blob:none --deepen 200 origin main; Check
    git -C "$Tools\AnyPS5" checkout -B astrobot $AnyPS5Commit; Check
    foreach ($patch in Get-ChildItem "$Root\Source\patches\*.patch" | Sort-Object Name) {
        Step "Applying $($patch.Name)"
        git -C "$Tools\AnyPS5" -c user.name=local -c user.email=local@localhost am --3way $patch.FullName; Check
    }
    git -C "$Tools\AnyPS5" submodule update --init --recursive --depth 1 -j 8; Check
}

$env:PATH = "$Tools\winlibs\mingw64\bin;C:\Program Files\Git\cmd;C:\Program Files\Git\usr\bin;$env:PATH"
$env:SSL_CERT_FILE = 'C:\Program Files\Git\mingw64\etc\ssl\certs\ca-bundle.crt'
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
# app0 is a junction to the game folder so the 160 GB of assets are never copied.
if (-not (Test-Path "$Run\app0")) { cmd /c mklink /J "$Run\app0" "$Game" | Out-Null }
& "$B\core\relinker\relinker.exe" --windows --registry "$In\eboot.elf" "$Run\AstroBot.exe"; Check

Step 'Assembling runtime libraries'
Copy-Item "$B\core\libs\libs\*.prx", "$B\core\libs\libs\*.dll" "$Run\libs\" -Force
foreach ($d in 'libgcc_s_seh-1.dll', 'libstdc++-6.dll', 'libwinpthread-1.dll') {
    Copy-Item "$Tools\winlibs\mingw64\bin\$d" "$Run\libs\" -Force
}
Step "Done: $Run\AstroBot.exe"
