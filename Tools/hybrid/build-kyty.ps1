# Builds kyty_gpu.dll (Kyty's GPU stack for AstroHybrid) from Tools\kyty-int21 with every path found on this PC.
#
# Steps: Visual Studio x64 environment -> configure kyty_emulator (clang-cl 22.1.3, ThinLTO, PGO, DLSS/FSR SDKs)
#        -> build kyty_emulator -> link kyty_gpu.dll with the patched tools\hybrid\build_kyty_gpu.py.
#
# Source: Jetsku/KytyPS5 tag u59-windows-20261024-int21 + Source\kyty-hybrid\*.patch (see Source\README.md).
# PGO: the profile tools\pgo\u59-int21-1.profdata was recorded compiling from C:\kyty-src19, and clang-cl names
#      functions in anonymous namespaces after a hash of the source path, so the build must compile from that
#      exact path. The script makes C:\kyty-src19 a junction to the checkout (it never overwrites another folder).
#      The profile was merged with LLVM 22.1.3's llvm-profdata, so the compiler must be that release.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File Tools\hybrid\build-kyty.ps1
#          [-SkipConfigure] [-NoPgo] [-March x86-64-v3] [-Name kyty-pgo] [-Deploy]
# Overrides (environment): ASTROHYBRID_KYTY_SRC, ASTROHYBRID_LLVM (folder with clang-cl.exe), VULKAN_SDK,
#   ASTROHYBRID_STREAMLINE_DIR (Streamline SDK 2.14.16), ASTROHYBRID_FFX_DIR (FSR SDK 2.4.0 Kits\FidelityFX).
param(
    [switch]$SkipConfigure,
    [switch]$NoPgo,
    [string]$March = '',
    [string]$Name = 'kyty-pgo',
    [switch]$Deploy
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path "$PSScriptRoot\..\..").Path
$PgoPath = 'C:\kyty-src19'

function Step($m) { Write-Host "== $m" -ForegroundColor Cyan }
function Check { if ($LASTEXITCODE -ne 0) { throw "failed with exit code $LASTEXITCODE" } }
function First-Existing([string[]]$paths) { $paths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1 }

# --- Sources -------------------------------------------------------------------------------------------
$Src = if ($env:ASTROHYBRID_KYTY_SRC) { $env:ASTROHYBRID_KYTY_SRC } else { "$Root\Tools\kyty-int21" }
if (-not (Test-Path "$Src\src\hybrid\kytyGpuBridge.cpp")) {
    throw "$Src is not a patched Kyty checkout (src\hybrid\kytyGpuBridge.cpp missing). See Source\README.md."
}
$Src = (Resolve-Path $Src).Path
$SourceDir = $Src
if (-not $NoPgo -and $Src -ne $PgoPath) {   # a checkout that already lives at C:\kyty-src19 needs no junction
    $item = Get-Item $PgoPath -ErrorAction SilentlyContinue
    if (-not $item) {
        Step "Creating the PGO source path $PgoPath -> $Src"
        cmd /c mklink /J $PgoPath $Src | Out-Null; Check
    } elseif ($item.LinkType -ne 'Junction' -or (Resolve-Path $item.Target).Path -ne $Src) {
        throw "$PgoPath exists and is not a junction to $Src. Remove it, set ASTROHYBRID_KYTY_SRC=$PgoPath, or build with -NoPgo."
    }
}
if (-not $NoPgo) { $SourceDir = $PgoPath }

# --- Toolchain -----------------------------------------------------------------------------------------
$Llvm = First-Existing @($env:ASTROHYBRID_LLVM, "$Root\Tools\llvm-22.1.3\bin", 'C:\LLVM-22.1.3\bin', 'C:\Program Files\LLVM\bin')
if (-not $Llvm) { throw 'clang-cl 22.1.3 not found: extract LLVM 22.1.3 to Tools\llvm-22.1.3 or set ASTROHYBRID_LLVM.' }
$clangVersion = & "$Llvm\clang-cl.exe" --version | Select-Object -First 1
if (-not $NoPgo -and $clangVersion -notmatch 'clang version 22\.1\.3\b') {
    throw "The PGO profile needs clang-cl 22.1.3, found '$clangVersion' in $Llvm (or build with -NoPgo)."
}
$LlvmFwd = $Llvm -replace '\\', '/'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw 'Visual Studio 2022 (Build Tools with the C++ workload) is not installed.' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No Visual Studio install has the x64 C++ tools (workload Microsoft.VisualStudio.Workload.VCTools).' }
Step "Visual Studio x64 environment from $vs"
$env:PATH = "$(Split-Path $vswhere);$env:PATH"   # vcvars64.bat calls vswhere.exe itself
cmd /s /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && set" | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] }
}
if (-not $env:VCToolsVersion) { throw 'vcvars64.bat did not set up the environment' }

$Vulkan = First-Existing @($env:VULKAN_SDK, [Environment]::GetEnvironmentVariable('VULKAN_SDK', 'Machine'))
if (-not $Vulkan) { throw 'Vulkan SDK not found (VULKAN_SDK is not set): install it from https://vulkan.lunarg.com/.' }
$env:VULKAN_SDK = $Vulkan
$env:PATH = "$Llvm;$Vulkan\Bin;$env:PATH"

# --- Optional SDKs: without them the DLL builds with DLSS, frame generation or FSR left out ------------
$Streamline = First-Existing @($env:ASTROHYBRID_STREAMLINE_DIR, "$Root\Tools\sdk\streamline-v2.14.16")
$Ffx = First-Existing @($env:ASTROHYBRID_FFX_DIR, "$Root\Tools\sdk\fidelityfx-v2.4.0\Kits\FidelityFX")
$sdkArgs = @()
if ($Streamline) {
    $sdkArgs += "-DKYTY_NGX_SDK_DIR=$($Streamline -replace '\\', '/')/external/ngx-sdk"
    $sdkArgs += "-DKYTY_STREAMLINE_DIR=$($Streamline -replace '\\', '/')"
} else { Write-Warning 'Streamline SDK 2.14.16 not found: building without DLSS and DLSS frame generation.' }
if ($Ffx) { $sdkArgs += "-DKYTY_FFX_SDK_DIR=$($Ffx -replace '\\', '/')" }
else { Write-Warning 'AMD FSR SDK 2.4.0 not found: building without FSR upscaling and FSR frame generation.' }

$Build = "$Root\Build\$Name"
$Out = if ($Name -eq 'kyty-pgo') { "$Root\Build\kyty-gpu" } else { "$Root\Build\$Name-gpu" }

# --- Configure / build / link --------------------------------------------------------------------------
if (-not $SkipConfigure) {
    Step "Configuring kyty_emulator in $Build (clang: $clangVersion; MSVC tools $env:VCToolsVersion)"
    $pgoArgs = @()
    if (-not $NoPgo) { $pgoArgs += "-DKYTY_PGO_USE=$($PgoPath -replace '\\', '/')/tools/pgo/u59-int21-1.profdata" }
    cmake -S $SourceDir -B $Build -G Ninja `
        -DCMAKE_BUILD_TYPE=Release `
        "-DCMAKE_C_COMPILER=$LlvmFwd/clang-cl.exe" "-DCMAKE_CXX_COMPILER=$LlvmFwd/clang-cl.exe" `
        "-DCMAKE_LINKER=$LlvmFwd/lld-link.exe" "-DCMAKE_AR=$LlvmFwd/llvm-lib.exe" `
        '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' `
        -DKYTY_BUILD_LAUNCHER=OFF -DKYTY_EMULATOR_IPO=ON -DKYTY_CP_DIAGNOSTICS=OFF `
        -DKYTY_BUILD_ORIGIN=Source "-DKYTY_MARCH=$March" `
        @pgoArgs @sdkArgs; Check
    if (-not $NoPgo) {
        # Any other spelling of the source path renames every anonymous-namespace function and loses its profile.
        $ninja = Get-Content -LiteralPath "$Build\build.ninja" -Raw
        if (-not $ninja.Contains('C$:\kyty-src19\src\emulator.cpp')) {
            throw 'build.ninja does not compile C:\kyty-src19\src\emulator.cpp: the PGO profile would only partly apply.'
        }
    }
}
Step 'Building kyty_emulator'
cmake --build $Build --target kyty_emulator --parallel; Check
Step "Linking kyty_gpu.dll into $Out"
python -I "$SourceDir\tools\hybrid\build_kyty_gpu.py" $Build $Out; Check

if ($Deploy) {
    $dst = "$Root\Build\RunHybrid"
    if (-not (Test-Path $dst)) { throw "$dst does not exist (run Tools\hybrid\make_run_hybrid.py first)" }
    $backup = "$Root\Build\kyty-deploy-backups\$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    New-Item -ItemType Directory -Force $backup | Out-Null
    foreach ($f in 'kyty_gpu.dll', 'kyty_gpu.pdb') {
        if (Test-Path "$dst\$f") { Copy-Item "$dst\$f" $backup -Force }
        Copy-Item "$Out\$f" $dst -Force
    }
    Step "Deployed to $dst (previous DLL in $backup)"
}
$hash = (Get-FileHash "$Out\kyty_gpu.dll" -Algorithm SHA256).Hash
Step "Done: $Out\kyty_gpu.dll (SHA256 $hash)"
