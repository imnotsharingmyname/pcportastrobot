# Source of the files in the AstroHybrid download

AstroHybrid's binaries are built from two GPL-2.0 projects plus this repository. This page lists, for
every program file in the download, the exact public base it starts from and the complete patch series
that turns that base into the source it was built from. Both series are verified to reproduce that
source tree exactly (same Git tree hash).

| File in the download | Built from |
|---|---|
| `Build\RunHybrid\kyty_gpu.dll` | KytyPS5 (Jetsku), tag `u59-windows-20261024-int21` (commit `744705ee1df4b0acb1af382ff5c32ca4a20aa5f7`) + `Source/kyty-hybrid/*.patch` (35 patches) |
| `Build\RunHybrid\libs\*.prx`, `astrohybrid_host.dll`, `tools\relinker.exe` | AnyPS5, upstream `main` commit `319621ba2e578bd053b0baba2877fe70520e0737` + `Source/anyps5/*.patch` (26 patches) |
| `AstroHybrid.exe` | `AstroHybrid.App/` in this repository |
| `AstroBot Launcher.exe` | `Tools/hybrid/launcher/AstroBotLauncher.cs` in this repository |
| `nvngx_dlss.dll`, `nvngx_dlssg.dll`, `sl.*.dll`, `NvLowLatencyVk.dll` | NVIDIA runtime files, unmodified (DLSS SDK / Streamline 2.14.16), under the licences in `licenses/` |
| `Build\RunHybrid\anyps5-fonts\*` | Noto fonts, unmodified (SIL Open Font License) |

The game executable (`AstroBot*.exe`) is not in the download: the setup converts each player's own dump.

## Kyty GPU runtime (`kyty_gpu.dll`)

Base: https://github.com/Jetsku/KytyPS5, tag `u59-windows-20261024-int21`.
`Source/kyty-hybrid` holds every commit between that tag and the source of the shipped DLL, including
the bridge (`src/hybrid/kytyGpuBridge.cpp`, `kytyGpuApi.h`, `kyty_gpu.def`, `kytyGpuNids.inc`) and the
DLL link script (`tools/hybrid/build_kyty_gpu.py`). The older `Source/hybrid/patches` folder is a subset
of this series, kept for history.

```
git clone https://github.com/Jetsku/KytyPS5.git kyty
cd kyty
git checkout u59-windows-20261024-int21
git submodule update --init --recursive
git am --keep-cr <this repo>/Source/kyty-hybrid/*.patch
git rev-parse HEAD^{tree}     # daaca7b52eac480bd9dbdcd0975e8cbe64890a7a
```

Build (Windows, Visual Studio 2022 Build Tools x64 environment, LLVM 22.1.3 clang-cl, Ninja, Vulkan SDK):

1. Configure as `Tools/hybrid/build-kyty-pgo.cmd` does (Release, clang-cl, IPO on, PGO profile
   `tools/pgo/u59-int21-1.profdata` from the int21 tag, launcher off).
2. For DLSS and frame generation, also pass `-DKYTY_NGX_SDK_DIR=<streamline>/external/ngx-sdk` and
   `-DKYTY_STREAMLINE_DIR=<streamline>`, where `<streamline>` is NVIDIA's Streamline SDK 2.14.16
   (https://github.com/NVIDIA-RTX/Streamline). Without them the DLL builds with DLSS and frame
   generation left out.
3. `cmake --build <build> --target kyty_emulator`
4. `python -I tools/hybrid/build_kyty_gpu.py <build> <out>` links `<out>\kyty_gpu.dll` from the
   emulator's objects and the bridge.

## AnyPS5 system libraries and relinker

Base: https://github.com/boykopovar/AnyPS5, commit `319621ba2e578bd053b0baba2877fe70520e0737` (on `main`).

```
git clone https://github.com/boykopovar/AnyPS5.git anyps5
cd anyps5
git checkout 319621ba2e578bd053b0baba2877fe70520e0737
git submodule update --init --recursive
git am --keep-cr <this repo>/Source/anyps5/*.patch
git rev-parse HEAD^{tree}     # ab2ee4b0a73ddbf827549df90aff62fa64b35e0c
```

Build with WinLibs GCC 15.2.0 (posix-seh UCRT) as `Tools/build.ps1` does: configure with Ninja in
Release, build everything and the `libs` target. `core/relinker/relinker.exe` is the relinker; the
`libs` target produces the `.prx` system libraries, `libAstroHybridHost.prx` is shipped as
`astrohybrid_host.dll`. `Tools/hybrid/make_run_hybrid.py` shows how the game folder is laid out.
The older `Source/patches` folder holds the same 26 changes under their earlier names.
