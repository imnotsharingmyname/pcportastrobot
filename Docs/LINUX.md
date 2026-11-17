# Linux build and run

Status: the native Linux hybrid runtime boots to the title screen, reaches the
worldmap and the Crash Site tutorial, and renders through Kyty's Vulkan stack.
The test machine is a CachyOS/Arch desktop with an AMD Radeon RX 9070 XT
(RADV) and a Valve Steam Controller. The Windows build remains untouched.

## Requirements

- x86-64 Linux with Vulkan 1.3+ (RADV or the NVIDIA driver).
- GCC/Clang with C++20, CMake 3.22+, Ninja, Python 3.9+.
- Clang is required for the Kyty U59 tree; its Vulkan-Hpp code does not compile
  with GCC 16.
- SDL's X11/Wayland/audio development files if the bundled dependencies are not
  used. AnyPS5's default build uses its submodules.
- Your own decrypted Astro Bot PPSA21567 dump, version 01.019.000 is tested.
  Extract it as `Game/PPSA21567-app`; game files are never committed.
- About 200 GB free for the dump plus both build trees.

## One-shot hybrid build

```sh
Tools/linux/build.sh hybrid
```

This applies the pinned AnyPS5 + Kyty patches, builds the patched AnyPS5 system
libraries, builds `kyty_gpu.so`, relinks the title (if `Game/PPSA21567-app` is
present) and lays out `Build/RunHybrid`.

Then launch:

```sh
Tools/linux/run.sh run --hybrid
# or the GUI
Tools/linux/launch-gui.sh
```

## Baseline (AnyPS5's own renderer)

```sh
Tools/linux/build.sh stock
Tools/linux/run.sh setup --game Game/PPSA21567-app
Tools/linux/run.sh run
```

The baseline confirms the dump, relinker, Vulkan driver and input before the
Kyty bridge is added. It is much slower than the hybrid and is not the playable
target.

## Manual stages

```sh
Tools/linux/prepare-anyps5.sh patched
Tools/linux/build-anyps5.sh Build/anyps5-hybrid
Tools/linux/prepare-kyty.sh patched
Tools/linux/build-kyty.sh
Tools/linux/run.sh setup --game Game/PPSA21567-app
Tools/linux/run.sh setup --hybrid --game Game/PPSA21567-app
```

`prepare-anyps5.sh` applies `Source/anyps5/*.patch` and then
`Source/anyps5-linux/*.patch`. `prepare-kyty.sh` applies
`Source/kyty-hybrid/*.patch` and `Source/kyty-hybrid-linux/*.patch`.

## Layout

```text
Build/anyps5/                 stock AnyPS5 build tree
Build/anyps5-hybrid/          patched AnyPS5 build tree
Build/kyty-clang/             Kyty build tree
Build/kyty-gpu/kyty_gpu.so    hybrid GPU shared library
Build/elf-input/              unwrapped eboot and bundled modules
Build/Run/                    base runtime (AstroBot, libs/, app0/)
Build/RunHybrid/              hybrid runtime (Kyty graphics, patched kernel)
Tools/AnyPS5/                 pinned AnyPS5 checkout (ignored)
Tools/KytyPS5-jetsku/         pinned Kyty checkout (ignored)
Game/PPSA21567-app/           extracted dump (ignored)
```

## How the hybrid is wired

- `kyty_gpu.so` exports the 82 graphics NIDs (plus the two AnyPS5 HDR calls) that
  the game imports from `libSceAgc`, `libSceAgcDriver` and `libSceVideoOut`.
  Those three run-directory libraries are symlinks to it.
- The kernel memory NIDs are not in the ELF global scope: `libkernel.prx` reaches
  them through `KytyGpuGetNid`, so they cannot shadow libkernel's own exports.
- `libkernel.prx` is moved ahead of the graphics libraries in the executable's
  `DT_NEEDED` order for the same reason.
- `libkernel.prx` receives the game's guest memory through the AnyPS5 donation
  API; Kyty adopts the ranges as free space before it starts its renderer.
- AnyPS5's APR reads use a staging buffer because a `read(2)` into a
  Kyty-protected page returns `EFAULT` instead of taking the fault handler.
- `libAstroHybridHost.prx` supplies the event-queue, guest-memory and pad
  callbacks to Kyty.

## Audio routing

PipeWire can restore the game's stream into a desktop filter sink such as JamesDSP or a virtual
surround chain. Those filters add latency and make the audio sound late even when the game's own
audio queue is small (the AudioOut2 trace shows about 53 ms unread in the tested build).

`astrohybrid.py run --direct-audio` (and the GUI's Graphics page, which enables it by default) starts
`Tools/linux/route-audio.py`. That helper waits for the game's PipeWire client, disconnects it from
the filter sink and links `AstroBot:output_FL/FR` directly to the default sink. Pass
`--audio-sink <name>` (or fill the sink field in the GUI) to select a specific sink. The helper is a
no-op when `pactl`/`pw-link` are absent.

For a quick manual check while the game is running:

```sh
pactl list short sink-inputs
pw-link -l | grep -A2 AstroBot
```

`ASTROHYBRID_FPS_LOG=1` prints the window's per-second `frame=… fps=…` line to stderr, which is useful
for finding frame-rate drops in a long run.

## What is not in the Linux build

- NVIDIA DLSS Super Resolution, DLSS Frame Generation and Reflex. Those are
  Windows/NVIDIA runtime features; the Linux build uses the game's own temporal
  upscale.
- The WPF launcher. `AstroHybrid.App` stays the Windows app; the Linux launcher
  is `AstroHybrid.App.Avalonia`.
- The Windows-only game byte patches (fixed 1080p etc.) can be applied later
  through the relinker's `--patches` option.

## Troubleshooting

- `kyty_gpu.so ... cannot allocate memory in static TLS block`: libkernel loads
  the already-mapped shim through `dl_iterate_phdr`; if you launch the game from a
  wrapper that clears the global scope, keep `libs/libSceVideoOut.prx` as a
  normal `DT_NEEDED` symlink.
- `APR: read failed`: the Linux APR path stages reads; make sure the patched
  `libkernel.prx` from `Build/anyps5-hybrid` is installed, not the stock one.
- `APS5_GPU_ENGINE=kyty` is set by `astrohybrid.py run --hybrid`; launching
  `Build/RunHybrid/AstroBot` by hand needs it and `KYTY_GPU_HOST_MODULE`.
- Audio sounds late: check `pw-link -l` for `AstroBot -> jamesdsp_sink` or another filter sink and
  use `--direct-audio` (or the GUI's Audio option). `route-audio.py --sink <name>` can move a running
  stream.

## Upstream work

The Linux changes are prepared as three PR tracks:

- `pcportastrobot-linux` — build scripts, runtime layout, setup, Avalonia
  launcher and documentation.
- `AnyPS5` — guest-arena donation support, the Linux `dlopen` host-module path,
  `KytyGpuGetNid`, and the APR staging fix.
- `KytyPS5` — optional `kyty_gpu.so` embedding target, Linux guest-range
  adoption, `dlopen` host loading and the build fixes.

No game files or user-specific paths are part of any patch series.
