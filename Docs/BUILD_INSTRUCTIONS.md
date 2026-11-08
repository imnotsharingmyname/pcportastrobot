# Build instructions (Windows 11 x64)

Prerequisites already on this PC: Git for Windows (with Bash), CMake ≥ 3.22, Ninja, Python 3.9+,
7-Zip, a Vulkan driver (NVIDIA 32.0.16.2101 tested). The script downloads the rest.

```powershell
powershell -ExecutionPolicy Bypass -File Tools\build.ps1
```

What it does (each stage verified on 2026-10-28):

1. Downloads WinLibs GCC 15.2.0 posix-seh UCRT r7 (the toolchain AnyPS5's Windows CI pins) to `Tools/winlibs`.
2. Clones AnyPS5 at `2199ee0` + submodules into `Tools/AnyPS5`.
3. Configures (`-G Ninja -DCMAKE_BUILD_TYPE=Release`, gcc/g++) into `Build/anyps5` (~2 min) and builds
   everything plus the `libs` target (~1 h on the Ryzen 5 5500, 2,219 steps). `SSL_CERT_FILE` is set to
   Git's CA bundle so FFmpeg's prebuilt download works.
4. Unwraps `Game/PPSA21567-app/eboot.bin` and the two bundled `sce_module/*.prx` with AnyPS5's
   `tools/unwrap_self.py`. The SELF segments are signed but **not encrypted** in this dump, so no keys
   are needed or used.
5. Relinks with `relinker --windows --registry` into `Build/Run/AstroBot.exe` (~60 s).
6. Lays out `Build/Run/`: `libs/` (368 `.prx` + MinGW runtime DLLs + mimalloc), and `app0` as an NTFS
   junction to `Game/PPSA21567-app` (no 160 GB copy). The relinker writes
   `libc.prx.guest.prx` and `libSceNpCppWebApi.prx.guest.prx` beside the originals in `sce_module/`;
   the original files are untouched.

Re-run only stages 4–6 after changing nothing in AnyPS5: `Tools\build.ps1 -SkipToolchain -SkipBuild`.
After editing AnyPS5 libraries: `cmake --build Build\anyps5 --target libs --parallel`, then the
`-SkipToolchain -SkipBuild` run to recopy them.

Run: `Run-AstroBot.bat` (logs to `Logs/last-run.*.log`), or the test harness:
`powershell -File Tests\run_test.ps1 -Seconds 420 -Env @{APS5_HOST_IMPORT_MIB='11264'}`.
The harness saves stdout/stderr, periodic window screenshots and the exit code under `Logs/run-<time>/`;
`-Trigger 'StartLevel title' -Keys @('45:0x0D')` presses Return 45 s after the title loads (only when the
game window has focus).

Antivirus note (from AnyPS5 docs): exclude `Build/` if tests or the relinked exe get quarantined.
