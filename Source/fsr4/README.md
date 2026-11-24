# FSR 4.1.2 upscale (optional, Linux)

This patch adds an opt-in `ASTROHYBRID_FSR4=1` path to Kyty's temporal upscale
pass. It is part of the Linux port and is applied by
`Tools/linux/prepare-kyty.sh patched`; it is inert unless the environment
enables it.

## What it does

- Adds `src/graphics/host_gpu/renderer/fsr4.{h,cpp}` and the FFX API headers
  (`src/fsr4/include/`, MIT) to the Kyty tree.
- Loads `fsr4/libamd_fidelityfx_loader_vk.so` at runtime, creates an FSR 4.1.2
  context with the FFX API and calls `ffxDispatch` with the game's colour,
  depth, motion vectors and output.
- Enables `VK_KHR_cooperative_matrix` + `VK_EXT_shader_float8` on the device only
  when `ASTROHYBRID_FSR4=1`.
- Restricts the hook to the final `3840x2160` output pass with a larger input
  size, once per frame. Intermediate `1920x1080 -> 2432x1368`, 1:1
  `2432x1368 -> 2432x1368` and `3840x2160 -> 3840x2160` passes are skipped.

## Current result (2026-11-14 10:03)

The image is stable. Verified in the title screen, the intro cutscenes and the
Crash Site tutorial: 20-frame screenshot sequences (including camera cuts) are
clean, with one FSR4 dispatch per upscaled frame. Two defects found and fixed
in this pass:

1. **The output was never committed to Kyty's texture cache.** DLSS calls
   `out.NoteContentWrite(); cache.CommitGpuWrite(out);` after its evaluation;
   FSR4 did not. Without it the cache can treat the replaced target as
   unwritten and let stale or aliased bytes reappear, which showed as
   intermittent horizontal bands of noise (seen in `Logs/fsr4-1to1.log`,
   `all-1.png`).
2. **History was not reset on a render-size change.** The game's dynamic
   resolution switches between 4K, 3328x1872, 2432x1368 and other sizes, often
   within the same second. `desc.reset` now also fires when the input size
   changes.

Performance is roughly neutral: 49-60 FPS in the Crash Site tutorial against
51.5-53.8 FPS for the same area without FSR4. The reason is the upscale ratio:
the game renders at 2432-3328 and upscales to 4K, only 1.15-1.58x, so FSR4 has
little work to remove. On frames where the game renders at native 4K the hook
is skipped by design, so the display alternates between FSR4 and the game's own
1:1 path; the images are individually clean, but FSR4 is not consistently in
the path.

## Fixed-1080p / A2 attempt (2026-11-14 11:05)

The goal was a fixed 1920x1080 scene render so FSR4 upscales 2x. The final pass
input turned out to **be** the dynamic-resolution size, not a fixed late
intermediate: a chain log (`ASTROHYBRID_FSR4_PASSLOG=3`) during the Crash Site
showed one `2432x1368 -> 3840x2160` (or `3328x1872 -> 3840x2160`) pass per
frame, with no intermediate pass in the same frame.

Attempts and results:

- `Source/game-patches/fixed_1080p.toml` controller patch (`add r14d,[rbx+8]` at
  file offset `0x73E9AB1`, VA `0x77E5AB1`): no change, still 3328/2432.
- Both documented controller fields (`+0x0c` index, `+0x28` level) set to 0:
  no change.
- Level→size table index forced to 0 at its only consumer (`0x6a754c`, table at
  VA `0x88849c0`/`0x88849d4`): no change.
- `KYTY_EOP_TIMESTAMPS=gpu` makes the controller respond to real GPU time: it
  drops from native 4K to 2432 at the title. The new `KYTY_EOP_SCALE` (published
  GPU time stretched, 250/400/1000%) nudges the same, but **gameplay floors at
  2432**; level 0 (1920x1080) is never selected.

Conclusion: the game's DRS does not expose 1920x1080 in gameplay, and the
controller fields/table are not the selection path for this build. Forcing 1080
needs a deeper reverse-engineering pass on the resolution selection (Ghidra
12.1; the installed 12.2 is rejected by REA). Until then FSR4 is at best a
1.58x upscale and performance-neutral.

The runtime was restored to the clean relinked ELF after the tests.

## Headless test harness

Runs do not need the desktop. `gamescope --backend headless` gives the game an
off-screen Xwayland output on the real GPU, and a `uinput` Xbox 360 pad created
before launch drives it without keyboard injection:

```sh
python3 /tmp/opencode/fsr4/vpad_daemon.py &   # pad + FIFO control
printf 'tap BTN_SOUTH\n' > /tmp/opencode/fsr4/vpad.fifo
SDL_VIDEODRIVER=x11 ASTROHYBRID_FSR4=1 \
  gamescope --backend headless -W 1440 -H 900 -w 1440 -h 900 -- \
  Tools/linux/run.sh run --hybrid --direct-audio
```

## A3: resolution-selection RE (2026-11-14 11:37)

A headless decompile of the resolution manager (`eboot.elf` original VA
`0x2a57e0`; relinked `0x6a57e0`) was done with radare2 (`pdc`; r2ghidra is not
installed). REA/Ghidra 12.1.2 works but a full import of the 247 MB eboot times
out (330 s) — use r2 or a persistent Ghidra project.

Findings:

- DRS update helper: original `0x37e59b0` (relinked `0x77e59b0`). Writes the
  resolution index at object `+0x0c`, level at `+0x28`.
- DRS object: `[manager + 0x540]`; manager singleton global at `0xf6759d8`
  (`[0xf6759d8+0x540]` in the relinked layout).
- The manager reads the index it uses from `[0xf372cf8] + 0x3093b4` at
  original `0x2a70e2` (`movsxd r11, [rax+0x3093b4]`).
- Level→size table: original `0x84849c0` (heights) / `0x84849d4` (widths);
  consumer at `0x2a7552`.

Tried, none changed the final pass from 2432/3328:

- `fixed_1080p` controller patch (`0x73e5ab1`).
- Controller fields `+0x0c`/`+0x28` set to 0.
- Table index forced to 0 at `0x2a7552`.
- Global index read at `0x2a70e2` replaced with `xor r11d, r11d`.

Conclusion: this function is the **GPU profiler / DRS controller**, not the
render-target size source. The render size is set elsewhere — most likely in
the Agc render-target descriptor or a renderer config that the profiler only
observes. The next step is to log scene-target creation in Kyty (guest resource
descriptor extents) and correlate it with the upscale pass, then clamp there.
That is a new instrumentation pass, not a game patch.

## A3: Ghidra follow-up (2026-11-14 15:31)

The Ghidra GUI MCP (`127.0.0.1:18080`) now works and was used to decompile the
manager (`FUN_002a57e0`) and its scene-parameter consumers.

- The level→size table at `0x84849c0`/`0x84849d4` is used **only** inside
  `FUN_002a57e0`, in a `GfxSceneParameters` telemetry block; forcing its index
  to 0 does not change the rendered pass.
- The real scene-parameter field is `+0x3093b4` in the engine object pointed to
  by `0xf372cf8`, with a second index at `+0x3093b8`. Writers:
  - `0x6ff7900` setter (`mov [rax+0x3093b4], edi`), getter `0x6ff7910`;
  - `0xc618e7` block copy (`mov rcx, [rbx+0x18e8]; mov [rax+0x3093b4], rcx`).
- The renderer consumer at `0x739df16` selects a width from a 5-entry jump
  table: level 0 = 1920, 1/2 = 2432, 3 = 3328, 4 = 3840; a second read at
  `0x739df9e` selects from a second width table (`+0x3093b8`).
- Forcing both reads to 0 (`0x739df16`, `0x739df9e`) and forcing the block copy
  and setter to 0 still leaves the upscale pass at 2432/3328. The fields are
  scene/telemetry parameters, not the render-target size the upscale pass
  consumes.

Conclusion: the render target size is set outside this scene-parameter path,
most likely in the Agc render-target descriptor / renderer config. The next
step is A4: instrument Kyty's scene-target creation (extent + guest descriptor
address + call site) and clamp there.

The game binary was restored clean after the tests.

## A4: scene-target instrumentation (2026-11-14 15:40)

`ASTROHYBRID_TARGET_LOG=1` (textureCache) logs every newly created image at
least 1280x720 with its extent, format, guest backing address/size, and host
backtrace (2000 entries). The per-second FSR4 line now includes the upscale
pass's color backing address.

At the Crash Site the final upscale pass's color target is
`R16G16B16A16Sfloat`, matching the log exactly:

- `2432x1368`, `addr=0x53e020000`, `size=0x1a20000`
- `3328x1872`, `addr=0x51c630000`, `size=0x30c0000`

Clamp points found:

- `src/graphics/host_gpu/renderer/colorRenderTarget.cpp:560` builds the target
  `desc.info.extent = {width, height, depth}` from the guest RT descriptor.
- The depth target follows the same path (`DepthRenderTarget`).
- The viewport and scissor come from the guest in
  `renderDraw.cpp` (`SetGraphicsDynamicParams`, guest `xscale`/`yscale`), so
  shrinking only the image would crop the scene.

Next (A4b) was implemented and works:

- `ASTROHYBRID_FSR4_TARGET=1` (gated; also the launcher's "FSR 4 (experimental)"
  checkbox sets `ASTROHYBRID_FSR4=1` + `ASTROHYBRID_FSR4_TARGET=1`).
- `FindImage` maps the game's `2432x1368` and `3328x1872` images to `1920x1080`
  when the flag is set; the FSR4 hook uses the actual color image extent for
  `renderSize`, jitter and motion-vector scale.
- Gameplay renders at 1920x1080 and FSR4 dispatches `1920x1080 -> 3840x2160`.
  Captured Crash Site frames (desert, controller, tutorial UI) are full-frame
  and clean; no cropping.
- Crash Site 60 s FPS samples: **51.8 mean / 54 median** with FSR4 at
  3328x1872 vs **53.0 / 56** with the 1920 target. The hub cutscene was
  38-46 FPS before and 56-60 after. The gain is modest because the scene is
  partly CPU/transition-bound.
- The mode is off by default; the normal port (FSR4 off) is unchanged. The
  Linux PR remains FSR4-free.

## Env switches

- `ASTROHYBRID_FSR4=1` — enable the hook.
- `ASTROHYBRID_FSR4_PATH` — provider directory (default `fsr4`).
- `ASTROHYBRID_FSR4_MV_SIGN` — motion-vector scale sign (default `-1`).
- `ASTROHYBRID_FSR4_PRE_EXPOSURE` — default `1.0`.
- `ASTROHYBRID_FSR4_NEAR` / `_FAR` — camera defaults `0.1` / `1000`.
- `ASTROHYBRID_FSR4_SHARPEN=1` — RCAS on (default off).
- `ASTROHYBRID_FSR4_PASS` — shader hash override.
- `ASTROHYBRID_FSR4_PASSLOG=1` — once a second, list every pixel program that
  writes 3840x2160 with its input size and rate. `=2` also prints each pass
  (capped at 400 lines) with its frame counter and output image. `=3` prints the
  whole upscale chain of a frame when its 4K pass arrives (bounded to 40
  chains); combine with `ASTROHYBRID_FSR4_CHAIN_AT=<frame>` to start at a
  gameplay frame.
- `ASTROHYBRID_FSR4_ALLOW_1TO1=1` — also replace the 1:1 4K pass (diagnostic:
  makes every frame go through FSR4).
- `FSR4_ASSET_DIR` — set by the hook to `<path>/assets` if unset.
- `KYTY_EOP_TIMESTAMPS=gpu` — make the guest GPU timestamps real, so the game's
  dynamic-resolution controller sees GPU time instead of command-processor time.
- `KYTY_EOP_SCALE=<percent>` (25-1000, default 100; with `gpu` timestamps)
  stretches the published GPU time away from the record time. Above 100 the
  controller measures more GPU time and lowers the internal resolution; it
  bottoms out at 2432x1368 on this title.

The hook also logs the four resource formats before the first dispatch and a
per-second `FSR4: dispatched N time(s) ...` line, so a gameplay run shows
whether FSR4 keeps replacing the pass.

## Next steps

1. Instrument Kyty's image/resource creation for the scene color/depth targets
   (extent, guest descriptor address, call stack) and correlate with the
   upscale pass. The profiler/DRS controller is not the size source; the Agc
   render-target descriptor or the renderer config is.
2. Until a fixed lower scale exists, FSR4 is a correctness/quality experiment,
   not a performance option. Keep it off by default and out of the Linux PR.
3. If the clamp lands, re-check the MV sign, jitter, depth convention and
   pre-exposure at the new ratio; the current defaults look right at 2432-3328.

The FSR4 provider build/staging instructions are in `Tools/fsr4/README.md`.
