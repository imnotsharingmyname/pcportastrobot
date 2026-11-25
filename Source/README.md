# Source of the files in the AstroHybrid download

AstroHybrid's binaries are built from two GPL-2.0 projects plus this repository. This page lists, for
every program file in the download, the exact public base it starts from and the patch series used to
build it. The original release provenance is recorded below. The canonical AnyPS5 series now also
contains two ZAR compatibility patches; these require rebuilding the runtime and are not supplied by
rerunning game setup with an older download. See [the ZAR guide](../Docs/ZAR.md).

| File in the download | Built from |
|---|---|
| `Build\RunHybrid\kyty_gpu.dll` | KytyPS5 (Jetsku), tag `u59-windows-20261024-int21` (commit `744705ee1df4b0acb1af382ff5c32ca4a20aa5f7`) + `Source/kyty-hybrid/*.patch` (64 patches; 0062 is not in this release line) |
| `Build\RunHybrid\libs\*.prx`, `astrohybrid_host.dll`, `tools\relinker.exe` | AnyPS5, upstream `main` commit `319621ba2e578bd053b0baba2877fe70520e0737` + `Source/anyps5/*.patch` (32 patches) |
| `AstroHybrid.exe` | `AstroHybrid.App/` in this repository |
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
git rev-parse HEAD^{tree}     # 766ea2d05fe7a907e7e649ee9db83ed83e4d4b6e
```

Build (Windows, Visual Studio 2022 Build Tools x64 environment, LLVM 22.1.3 clang-cl, Ninja, Vulkan SDK):

1. Configure as `Tools/hybrid/build-kyty-pgo.cmd` does (Release, clang-cl, IPO on, PGO profile
   `tools/pgo/u59-int21-1.profdata` from the int21 tag, launcher off).
2. For DLSS and frame generation, also pass `-DKYTY_NGX_SDK_DIR=<streamline>/external/ngx-sdk` and
   `-DKYTY_STREAMLINE_DIR=<streamline>`, where `<streamline>` is NVIDIA's Streamline SDK 2.14.16
   (https://github.com/NVIDIA-RTX/Streamline). Without them the DLL builds with DLSS and frame
   generation left out. For AMD FSR, pass `-DKYTY_FFX_SDK_DIR=<fsr>/Kits/FidelityFX`, where `<fsr>` is
   AMD's FSR SDK 2.4.0 (https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, tag `v2.4.0`); the
   release ships the SDK's signed `amd_fidelityfx_loader_vk.dll`, `amd_fidelityfx_upscaler_vk.dll` (FSR upscaling)
   and `amd_fidelityfx_framegeneration_vk.dll` (FSR frame generation).
   Intel XeSS (patch 0064) needs nothing at build time: the XeSS 3 SDK's public headers (MIT) are in
   `3rdparty/xess`, and `libxess.dll` from the SDK's `bin` folder (https://github.com/intel/xess) is
   loaded at run time from next to the game; the release ships it with its licence as
   `xess.license.txt`.
3. `cmake --build <build> --target kyty_emulator`
4. `python -I tools/hybrid/build_kyty_gpu.py <build> <out>` links `<out>\kyty_gpu.dll` from the
   emulator's objects and the bridge.

## AnyPS5 system libraries and relinker

Base: https://github.com/boykopovar/AnyPS5, commit `319621ba2e578bd053b0baba2877fe70520e0737` (on `main`).
The original download used patches 0001–0027. Build the current runtime with all 29 patches in
`Source/anyps5`, including:

- `0028-fix-app0-archive-odx-fallback.patch`: root-relative archive lookup and exact-first `.odxb` fallback.
- `0029-feat-libc-archive-stdio-streams.patch`: read-only libc archive streams and regression tests.

```
git clone https://github.com/boykopovar/AnyPS5.git anyps5
cd anyps5
git checkout -b astrobot-zar 319621ba2e578bd053b0baba2877fe70520e0737
git submodule update --init --recursive
git am --keep-cr <this repo>/Source/anyps5/*.patch
git rev-parse HEAD^{tree}
```

Apply patches in filename order. `Tools/build.ps1` uses this canonical series, not the older
`Source/patches` subset. The build script fetches sources directly from public GitHub.

Build with WinLibs GCC 15.2.0 (posix-seh UCRT) as `Tools/build.ps1` does: configure with Ninja in
Release, build everything and the `libs` target. `core/relinker/relinker.exe` is the relinker; the
`libs` target produces the `.prx` system libraries, `libAstroHybridHost.prx` is shipped as
`astrohybrid_host.dll`. `Tools/hybrid/make_run_hybrid.py` shows how the game folder is laid out.
`libSceAgc.prx`, `libSceAgcDriver.prx` and `libSceVideoOut.prx` in the download are not AnyPS5's own
builds: they are small forwarder shims made by `make_run_hybrid.py` that hand graphics to `kyty_gpu.dll`.
Installing AnyPS5's real ones makes the game stop with `DisplayWindow: Couldn't create window`.
The older `Source/patches` folder holds a historical 26-patch subset; it does not include the new ZAR fixes.

Focused regression tests after a configured build:

```
ctest --test-dir Build/anyps5 -R "app_archive|archive_stdio" --output-on-failure
python -m unittest discover -s Tests -p "test_zar_layout.py" -v
```

For a focused Linux build using actual libc/kernel implementations, GCC 13+ and system `libzstd`:

```
python3 Tests/run_zar_native.py
python3 Tests/run_zar_native.py --zarchive-source ../ZArchive-master --work-dir work/zar-native-supplied
```

This runner does not stub archive or stdio behavior. It runs lookup, stdio and kernel regressions
against either the pinned dependency or the supplied ZArchive checkout. It omits the kernel `mmap`
mode on Linux: that mode tests an explicit refusal using the guest terminate handler, but the
standalone native exception bridge aborts instead of reaching that handler. The original CTest
mode remains available in the full runtime build; it has not been validated here.

A full Windows build and a game smoke test remain necessary to validate the deployed libraries.
