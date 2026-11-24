# Build instructions (Windows 10/11 x64)

Builds the whole AstroHybrid runtime from source: AnyPS5 (relinker + system libraries + host module) and Kyty's
GPU stack (`kyty_gpu.dll`), then converts your own dump. Every path is inside this repository (third-party
checkouts, toolchains and SDKs go to gitignored folders under `Tools\`, outputs to `Build\`), except the
`C:\kyty-src19` junction the Kyty PGO profile needs (see step 4).

## 1. Tools to install

| Tool | Version | Where it is looked for |
|---|---|---|
| Git, Python 3.9+, 7-Zip | any recent | `PATH` (installer, scoop or winget) |
| CMake, Ninja | CMake ≥ 3.22 (4.x works) | `PATH` |
| Visual Studio 2022 Build Tools | workload *Desktop development with C++* (`Microsoft.VisualStudio.Workload.VCTools`) | `vswhere` |
| Vulkan SDK | 1.4.363.0 tested | `VULKAN_SDK` (set by the installer) |
| LLVM | **exactly 22.1.3** (the PGO profile was merged with it) | `Tools\llvm-22.1.3` (extract `clang+llvm-22.1.3-x86_64-pc-windows-msvc.tar.xz` there), `ASTROHYBRID_LLVM`, `C:\LLVM-22.1.3` |
| WinLibs GCC | 15.2.0 posix-seh UCRT r7 | downloaded by `Tools\build.ps1` to `Tools\winlibs` |
| NVIDIA Streamline SDK | 2.14.16 (DLSS, DLSS-G, Reflex) | `Tools\sdk\streamline-v2.14.16` or `ASTROHYBRID_STREAMLINE_DIR` |
| AMD FSR SDK | 2.4.0 (`git clone --depth 1 --branch v2.4.0` of FidelityFX-SDK) | `Tools\sdk\fidelityfx-v2.4.0` or `ASTROHYBRID_FFX_DIR` |

Without the two SDKs `kyty_gpu.dll` still builds, with DLSS / frame generation / FSR left out.

**Line endings.** Git for Windows defaults to `core.autocrlf=true`, which would check the upstream sources out
with CRLF so that no patch applies. The scripts and `.gitattributes` already handle it (patches are `-text`, the
checkouts get `core.autocrlf=false`); if you clone by hand, pass `-c core.autocrlf=false`.

## 2. AnyPS5: relinker, system libraries, host module (~20–60 min)

```powershell
powershell -ExecutionPolicy Bypass -File Tools\build.ps1 -SkipGame
```

Downloads WinLibs, clones AnyPS5 at `319621ba` into `Tools\AnyPS5`, applies `Source\anyps5\*.patch` in filename
order (`git am --3way --keep-cr`), updates submodules, then builds into `Build\anyps5`:
`core\relinker\relinker.exe` and `core\libs\libs\*.prx` (370 files, including `libAstroHybridHost.prx`).

## 3. Relink your dump into `Build\Run` (~1 min)

```powershell
powershell -ExecutionPolicy Bypass -File Tools\build.ps1 -SkipToolchain -SkipBuild -GameDumpPath "D:\path\to\PPSA21567-app"
```

Unwraps `eboot.bin` and the bundled modules (signed, not encrypted: no keys), relinks with
`relinker --windows --registry` to `Build\Run\AstroBot.exe` and copies the libraries. `Build\Run\app0` is a real
folder (one link per dump folder, a copy of `sce_module`): the relinker writes `*.guest.prx` into
`app0\sce_module`, and the dump itself is never written.

## 4. Kyty: `kyty_gpu.dll` (~30–60 min)

Clone and patch once (step 2 of `Source\README.md`):

```powershell
git -c core.autocrlf=false clone https://github.com/Jetsku/KytyPS5.git Tools\kyty-int21
git -C Tools\kyty-int21 checkout -B astrohybrid u59-windows-20261024-int21
git -C Tools\kyty-int21 submodule update --init --recursive
Get-ChildItem Source\kyty-hybrid\*.patch | Sort-Object Name | ForEach-Object { git -C Tools\kyty-int21 am --keep-cr $_.FullName }
```

After patch 0055 the tree is `476fec7f6f61b98c4daf98695455714b5e03a855` (the shipped DLL's source); 0056–0061 follow.
Then:

```powershell
powershell -ExecutionPolicy Bypass -File Tools\hybrid\build-kyty.ps1
```

Imports the Visual Studio x64 environment, configures `kyty_emulator` with clang-cl 22.1.3, ThinLTO, the PGO
profile `tools\pgo\u59-int21-1.profdata` and the SDKs found, builds it into `Build\kyty-pgo` and links
`Build\kyty-gpu\kyty_gpu.dll` with the patched `tools\hybrid\build_kyty_gpu.py`.

PGO needs the sources compiled from `C:\kyty-src19`: clang-cl names anonymous-namespace functions after a hash
of the source path, and the profile was recorded there. The script creates that junction to `Tools\kyty-int21`
(and refuses to touch an existing folder). `-NoPgo -Name kyty-plain` builds without it (`Build\kyty-plain-gpu`).

## 5. Assemble `Build\RunHybrid`

```powershell
python -I Tools\hybrid\make_run_hybrid.py --game "D:\path\to\PPSA21567-app"
```

Links the relinked exe and libraries from `Build\Run`, copies the fresh `libc`/`libkernel`/`libScePad`, builds the
three forwarder shims (`libSceAgc`, `libSceAgcDriver`, `libSceVideoOut` → `kyty_gpu.dll`), copies
`astrohybrid_host.dll`, `kyty_gpu.dll`, Kyty's `u59-preset.json` and the NVIDIA/AMD runtime files and licences
from the SDKs.

The game-side patches (cloth 60 Hz clamp, command-buffer table, ultrawide / NonRT variants, real-time game step)
are applied by the app's setup (`AstroHybrid.App\Services\GameSetup.cs`), not by these scripts.

## Rebuilding after a change

- AnyPS5 library: `cmake --build Build\anyps5 --target libs --parallel`, then step 5.
- Kyty: `Tools\hybrid\build-kyty.ps1 -SkipConfigure` (add `-Deploy` to copy the DLL into `Build\RunHybrid`
  with a backup of the previous one).

Antivirus note (from AnyPS5 docs): exclude `Build\` if tests or the relinked exe get quarantined.
