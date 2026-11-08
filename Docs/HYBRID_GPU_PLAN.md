# AstroHybrid: native AstroBot.exe with Kyty's GPU stack

Written 2026-11-06 from measurements, not assumptions. Goal: 30 FPS, then 60, in Sky Garden and on the world map,
still launched as `Build\Run\AstroBot.exe` (no emulator application).

## 1. What the measurements say

Sky Garden steady scene (60-80k draws per 10 s), RTX 5060 8 GB, Ryzen 5 5500:

| | AnyPS5 (patches 0001-0020) | Kyty int21 (`E:\AstroHybrid-Work\reports\BASELINE-AND-BOTTLENECKS.md`) |
|---|---|---|
| FPS | 1.2-1.3 (`run-20261106-192631`, `194423`) | 29.2 |
| GPU | 651 ms busy per frame | 68% busy at 29 FPS, about 23 ms per frame |
| VRAM | device-local heap 7333 of a 7055 MiB budget; storage textures (14.6 GiB of allocations) and device buffers (25.5 GiB) placed in host memory | 6.5 GB used |
| Storage images | 22,850 created in one 170 s run (108 GiB), about 170 per frame, 10 s spent creating them | kept resident |
| CPU | worker thread waits 47% of the steady window; committer mostly waits for work or for the GPU | CP thread 90% busy, 4.4 us per draw |

Conclusions:

- In Sky Garden AnyPS5 is limited by its GPU backend: resources spill out of VRAM and storage images are
  recreated every frame. The per-draw CPU path (capture 18 us + materialize 15 us) is not what holds the frame
  there, so Kyty's SRT trace replay alone would not raise Sky Garden FPS.
- Tried and measured today, no gain: patch 0020 (rebased stage reuse; per-frame data changes, not only
  pointers), result object recycling (`Source/experiments/APS5_RESULT_RECYCLE.patch`; the memo patch costs only
  3 us). A sized-free fast path in HostAllocator corrupted the heap with blocks allocated inside libstdc++ and
  was reverted.
- Kyty already renders this scene 24x faster with correct pictures. The transplant that matters is Kyty's
  whole GPU stack: command processor, renderer, shader recompiler with SRT replay, and memory tracking.

## 2. Architecture

```
AstroBot.exe (AnyPS5 relinked game, unchanged)
 |-- AnyPS5 native libs: libkernel, libc, fs, pad, audio, save data, net ...   (kept)
 |-- libSceAgc / libSceAgcDriver / libSceVideoOut                              (replaced, switch APS5_KYTY_GPU)
       \-- kyty_gpu.dll (built from E:\AstroHybrid-Work\source\kyty with clang-cl)
             Kyty graphics/ (guest_gpu CP, host_gpu renderer, shader recompiler, presentation)
             Kyty libs/agc.cpp + videoOut HLE, exported by NID
             host adapter: memory mappings, event queues, threads -> callbacks into AnyPS5 libkernel
```

Why this seam works:

- AnyPS5 prx files export functions under their NID names (`objdump -p libSceAgcDriver.prx`: 18,386
  exports such as `++AVshDHIds`); `AstroBot.exe` imports only KERNEL32 and the AnyPS5 loader resolves the
  game's imports by NID. Kyty registers its HLE functions by the same NIDs (`LIB_FUNC("23LRUSvYu1M", ...)`)
  with `KYTY_SYSV_ABI`, the convention the game uses. A DLL exporting those functions by NID can stand in for
  the three prx files.
- Both engines run the game's x86-64 code natively in one process and see guest memory at its real address,
  so Kyty's renderer can read guest memory directly in AnyPS5's process.
- Toolchains differ (AnyPS5: MinGW GCC, Itanium C++ ABI; Kyty: clang-cl, MSVC ABI). Only C/SysV functions and
  plain structs cross the boundary; each side keeps its own allocator and exceptions.
- Licences: both trees are GPL-2.0, so combining them is permitted.

What Kyty's graphics needs from outside itself (include scan of `src/graphics`):

| Kyty dependency | Uses | Hybrid adapter |
|---|---|---|
| `common/*` | logging, asserts, threads, config | build as is inside the DLL |
| `kernel/memory.h` (21 files) | GPU-visible mappings, page protection for write tracking | AnyPS5 libkernel reports map/unmap/protect of direct and flexible memory through a callback table; Kyty's memory tracker keeps using VirtualProtect + its fault handler on those ranges. AnyPS5's own write alarms (patch 0009) and GetWriteWatch tracking must be off for GPU ranges to avoid two trackers |
| `kernel/eventQueue.h` | EOP, flip and vblank events the game waits on | adapter calls AnyPS5 libkernel's equeue trigger (new internal export) |
| `kernel/pthread.h` | thread creation, names | host threads, no guest TLS needed |
| `libs/agc`, video out, controller/dialog hooks | HLE entry points | exported by NID from the DLL; controller and dialogs stay in AnyPS5 |
| `loader/systemContent.h` | title paths | adapter returns AnyPS5's app0 path |

## 3. Milestones (each one ends in a test, the previous build stays the default)

1. **Done 2026-11-06 (Kyty `hybrid` `df8580c2`, details and blockers in `Docs/HYBRID_M1_EXPORTS.md`).**
   **kyty_gpu.dll builds.** New CMake target in a copy of the Kyty tree (`E:\AstroHybrid-Work\source\kyty`,
   branch `hybrid`): `common` + `graphics` + `libs/agc` + video out + a stub adapter. Done = the DLL links and
   exports the Agc, AgcDriver and VideoOut NIDs (`dumpbin /exports` compared with AnyPS5's prx export lists).
2. **Done 2026-11-06 (Kyty `hybrid` `036147c8`, AnyPS5 `7afc16282`, patch 0021).** `Run-AstroHybrid.bat` runs
   `Build\RunHybrid` (built by `python -I Tools\hybrid\make_run_hybrid.py`): the same AstroBot.exe and prx files
   (hard links), fresh libc/libkernel, forwarder DLLs for libSceAgc/libSceAgcDriver/libSceVideoOut that resolve every
   NID to kyty_gpu.dll, and `astrohybrid_host.dll` (AnyPS5's libAstroHybridHost). The first graphics or memory call
   starts Kyty's renderer in-process; AnyPS5's guest arena hands Kyty placeholder ranges (0x200000000-0x7ffff0000,
   0x0fe0000000 + 512 MiB, 64 GiB at 0xec00000000) and libkernel forwards the game's direct and flexible memory calls
   (`APS5_GPU_ENGINE=kyty`); Kyty's write-tracking fault handler runs first-chance; EOP and flip events reach the
   game's AnyPS5 queues through a Kyty proxy queue. Result: the title screen renders correctly at 51-60 fps
   (`run-20261106-205252`). `Run-AstroBot.bat` / `Build\Run` are unchanged and remain the AnyPS5 fallback.
   **Adapter and loader switch.** AnyPS5 libkernel gains a callback registration for mappings and an equeue
   trigger export; AnyPS5's loader, under `APS5_KYTY_GPU=1`, resolves the three libraries from
   `kyty_gpu.dll`. Done = AstroBot.exe boots to the first flip with Kyty presenting the window.
3. **Correct world map and Sky Garden pictures.** Fix memory-tracking and event gaps until both benches'
   screenshots are right. Done = the screenshot checks plus one 170 s run per scene.
4. **Performance parity with Kyty.** Target the Kyty numbers (29 FPS Sky Garden) inside AstroBot.exe; compare
   with `Tests\skygarden_bench.ps1` on both switch settings.
5. **Beyond 30 FPS.** Kyty's own limit is its CP commit path (21-22 ms of a 34 ms frame, 4.4 us per draw).
   Use AnyPS5's parallel worker/committer split and its draw-cache ideas there, then measure again.

## 4. Rules that still hold

- Never modify `Game\` or `E:\PPSA21567`; keep 30 GB free on C:; no pushes; commit locally only.
- `APS5_KYTY_GPU` stays off by default until milestone 4 shows a measured gain with correct screenshots.
- Kyty's source in `E:\AstroHybrid-Work\source\kyty` is reused on a new branch; the original Kyty builds and
  caches under `E:\AstroHybrid-Work\build` and the launcher in `E:\AstroBotPC` stay untouched.
- First run after deploying a new driver binary is cold (shader disk cache): warm up once before any A/B.

## 5. Descriptor offset audit (2026-11-08)

Reference source: shadPS4 `33e73127119f672f9f08b8a5a7f71600263f61d9`.

- [Scheduler::BeginRendering and DynamicState::Commit](https://github.com/shadps4-emu/shadPS4/blob/33e73127119f672f9f08b8a5a7f71600263f61d9/src/video_core/renderer_vulkan/vk_scheduler.cpp#L31) keep a rendering scope open for equal attachment state and emit dirty dynamic state. Kyty already has render-scope reuse, draw runs, and recorder packets. Preserve their state and command-buffer boundaries.
- [Pipeline::BindResources](https://github.com/shadps4-emu/shadPS4/blob/33e73127119f672f9f08b8a5a7f71600263f61d9/src/video_core/renderer_vulkan/vk_pipeline_common.cpp#L22) pushes constants and writes descriptors for each invocation, using push descriptors or a heap set. Kyty already has more reuse here. The useful opportunity is to avoid building and writing identical descriptor contents when only aligned offsets move.
- [BufferCache::ObtainBuffer](https://github.com/shadps4-emu/shadPS4/blob/33e73127119f672f9f08b8a5a7f71600263f61d9/src/video_core/buffer_cache/buffer_cache.cpp#L215) streams small read-only ranges when there is no GPU write to preserve. The general path finds a sparse arena, makes its blocks resident, and synchronizes dirty memory. [SubmitPendingArenaBinds](https://github.com/shadps4-emu/shadPS4/blob/33e73127119f672f9f08b8a5a7f71600263f61d9/src/video_core/buffer_cache/buffer_cache.cpp#L537) orders the residency changes with submission. Kyty also streams small CPU-dirty reads and guards resident bindings with tracker signatures and epochs. Changing descriptor encoding can keep all of these ownership and synchronization decisions intact. A fast stream path would need to retain the GPU-dirty guard and snapshot lifetime.
- [Rasterizer::BindBuffers](https://github.com/shadps4-emu/shadPS4/blob/33e73127119f672f9f08b8a5a7f71600263f61d9/src/video_core/renderer_vulkan/vk_rasterizer.cpp#L777) aligns storage offsets and supplies the residual in push data. [EmitContext::DefineBuffers](https://github.com/shadps4-emu/shadPS4/blob/33e73127119f672f9f08b8a5a7f71600263f61d9/src/shader_recompiler/backend/spirv/spirv_emit_context.cpp#L848) preserves typed aliases and coherency at the assigned binding. Kyty already packs residual offsets in shader data. A dynamic descriptor uses the same SPIR-V buffer type and effective aligned byte address. Preserve the binding numbers, ranges, residual offsets and access semantics.
- [Rasterizer::DrawIndirect](https://github.com/shadps4-emu/shadPS4/blob/33e73127119f672f9f08b8a5a7f71600263f61d9/src/video_core/renderer_vulkan/vk_rasterizer.cpp#L241) binds resources, vertex/index state and an indirect argument range once for a guest-provided draw list, with barriers before recording. It supplies no evidence that direct draws with changing V# or SRT bindings can be coalesced. Defer multi-draw for the current shadow and G-buffer runs until resource indexing, access ordering, index and vertex state, and the rendering scope are all proven compatible.

### Experiment and next decision

Patch 0004 adds ten-second stdout counts to `KYTY_DESCRIPTOR_OFFSET_AUDIT=1`, split by bind point and push/allocated set. The audit masks offsets in the existing descriptor writes and compares the remaining digest within a scheduler tick. The comparison is optimistic. It can mask only part of a descriptor array, models the set-wide storage dynamic limit, and omits offset representability, array layout, and bind cost. It does not measure identical unmasked writes or validate a Vulkan layout. It also says nothing about the time spent obtaining the buffers.

The strongest descriptor experiment is a split layout for graphics with dynamic bindings only where complete arrays fit the budget. Use the same handle and range, enforce device alignment, and supply offsets in Vulkan binding and array order. Keep a static path for incompatible bindings, preserve allocation lifetime through the command tick, and use the existing recorder's dynamic-offset packet. Measure actual descriptor CPU cost and set reuse first. Push descriptors cannot carry dynamic offsets, so the experiment must account for the extra set bind and for shader/pipeline layout changes. Keep the shader-data and SRT uploads ordered exactly as before. A whole-layout change is difficult to justify from hit rate alone.

GPU timestamps are in `Logs/offset-audit-gpu`. Eight gameplay captures stamped 18.36–23.45 ms of sampled work, including roughly 6.08–7.71 ms for the five-color G-buffer render target and 1.19–1.82 ms for the 2048-square D16 shadow target. These are sums of stamped spans for the sampled frames. They do not establish a sustained GPU ceiling or measure CPU commit time per pass.

The benchmark copy lives in `Build/RunHybridOffsetAudit`. The pinned `_sd_mem` snapshot is under `work/offset-audit-save` and should stay out of Git. Restore it only into the isolated runtime while the process is closed. Run `Tests/hybrid_skygarden.ps1 -RunDir Build/RunHybridOffsetAudit -Seconds 150 -Shots 6` with `KYTY_FRAME_TIMES_LOG` set to a new file. For the audit, also enable `KYTY_DESCRIPTOR_OFFSET_AUDIT=1`, `KYTY_CP_GAP_STATS=1`, `KYTY_GPU_TIMING=1`, `KYTY_GPU_OP_PROFILE=15`, and `KYTY_GPU_OP_PROFILE_STAMPS=passes`. Keep startup separate from the gameplay window and compare the final screenshots before comparing timings.

| File | SHA-256 |
|---|---|
| AstroBot-NonRT.exe | `ef795d851c8433231ad5e387cc40a20678c00398150d78cd10afdb9c5708eee3` |
| Audit kyty_gpu.dll | `f6f177c70ceeb60f9c49baa618079f1349bbe9e8f8c263da950b7ca7b9243340` |
| Pinned slot0.bin | `7d80e6c6551df7a08a94536e9c06071349171a16ea72a4aed74c6f4b95284b32` |

## 6. Single-set dynamic storage experiment (2026-11-08)

The masked audit suggested reusing about half of push updates. Prior thread samples put exclusive `CommitBindings` time near 2 ms per frame, so removing a fraction of its descriptor work could contribute to the 45 FPS target. The experiment also had to pay for a per-draw dynamic bind, cache lookup, and full image descriptor materialization. Kyty's partial push path could already omit the unchanged images.

The prototype changed whole graphics storage bindings to dynamic only when the original push layout fit the device's dynamic descriptor limit, up to 16 array elements. It retained shader set and binding numbers, buffer handles, ranges, and synchronization. It bound offsets in Vulkan binding order. Offsets larger than 32 bits and whole-size ranges kept the existing static base with a zero dynamic offset. Compute kept its existing binding path. The allocated-set cache compared exact normalized contents and retained its scheduler tick guard. [Vulkan dynamic descriptor binding rules](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdBindDescriptorSets.html)

Two warm crossover pairs rejected this design. CP commit rose from 21.28/22.11 ms to 23.08/24.98 ms at comparable draw counts. The direction of median and p99 changes reversed across pairs. Warm-up GPU capture ranges overlapped and were too few to attribute a GPU difference. The one-set experiment did not test a layout that retains the existing partial image push path alongside dynamic buffers.

DXVK selects descriptor heap, descriptor buffer, or legacy pools based on device support. Its direct multi-draw path commits shared state once and unrolls if storage hazards require barriers between draws. That supports measuring resource stability and hazards before adding batching to Kyty. [DXVK context and drawGeneric](https://github.com/doitsujin/dxvk/blob/829e377d2d5cf4b07455412fcabed66a1bf4390c/src/dxvk/dxvk_context.cpp#L1430)

The binding memo probe found about two-thirds of reads already hit and only 4-5% of requests took a slow path ending in a stream result. This rules out a broad stream-first bypass as a high-impact first change. The next measurement separates inclusive CP draw commit, binding preparation for Vulkan emission, and final descriptor update/bind by attachment signature.

## 7. CPU commit by pass

Patch 0005 adds `KYTY_CP_PASS_STATS=1` alongside `KYTY_CP_GAP_STATS=1`. It records inclusive draw commit time by attachment size, count and formats, plus graphics binding commit, final descriptor work, and buffer rebinding. Descriptor time is inside binding commit. Rebind time is a separate part of draw commit and includes shader table handling. The report uses bounded buckets and leaves non-draw commits unclassified. Shader table bytes are bound ranges, so deduplicated uploads can make actual bytes copied lower.

In the late Sky Garden windows of run `012234`, G-buffer CPU commit was 11.31-12.85 ms and shadow commit was 2.97-3.38 ms. G-buffer rebind was 1.92-2.24 ms. All passes together spent 3.37-3.78 ms in rebinding and 0.89-0.98 ms in final descriptor updates and binds. Even eliminating the latter would leave most of the gap to a 22.22 ms frame.

The installed RTX 5060 advertises 256 bytes of push constants, 16 dynamic storage descriptors, and the descriptor heap, descriptor buffer and multi-draw extensions. The audit models a 256-byte push layout using the existing stage order and mesh/clip-plane reservation. In the G-buffer, promoting one stage generally displaces another, saving about two table bindings per frame despite reducing the bound byte total by about 78-83 kB. The shadow pass could remove about 504 table bindings, but all shadow rebind work costs about 0.49-0.54 ms per frame. This does not justify a shader and pipeline cache layout change now.

Isolated diagnostic DLL SHA-256 `667a61c1af5c6b8edd9461f08932142222ec6f8c6cff41ac09bb9b6bf05e660b`. Reproduce with `Tests/hybrid_skygarden.ps1 -RunDir Build/RunHybridCpPassBudget -Seconds 120 -Shots 6` and the two CP switches above. Read `CpPass:` entries in `stdout.log` after startup.

## 8. Persistent graphics caches in the hybrid

Kyty's cache initialization asked its standalone loader for a title ID. AstroHybrid starts the renderer directly and does not initialize that metadata. The copied `_PipelineCache` files were ignored, including after a graceful window close. As a result, a restart could compile the same expensive shader again.

Patch 0006 accepts `ASTROHYBRID_CACHE_TITLE_ID=PPSA21567` when standalone metadata is absent. The value must be a PS5 title ID in the form `PPSA` followed by five digits. It supplies the cache filename only. File identities, device/driver validation, shader specialization keys and the game's file access are handled by the existing paths. The driver cache, program cache, shader journal and pipeline journal now use the title under the working directory's `_PipelineCache`. The feature remains opt-in.

The benchmark harness accepts `ASTROHYBRID_BENCH_GRACEFUL_EXIT=1` and requests window close after the measured window. It waits up to 15 seconds before falling back to termination. Kyty saves caches on its normal window-close path. Reproduce using `Tests/hybrid_skygarden.ps1 -RunDir Build/RunHybridCacheTitle -Seconds 120 -Shots 6` and pass both settings in `-Env`. Use a dedicated runtime and the pinned save. Keep the first cache-enabled launch separate from warm measurements.

For gameplay, launch `Run-AstroHybrid-NonRT-Cached.bat`. It selects the measured NonRT renderer and turns on persistent graphics caches in the regular `Build/RunHybrid` runtime. Exit the game normally to save newly compiled shaders. Reassembly requires a Kyty DLL built with patch 0006 or later to use the cached launcher.

### Frame-rate settings

`Run-AstroHybrid-FPS.bat` starts the cached NonRT build with a 30 FPS ceiling. In the game window, press **Ctrl+F2** to change the ceiling to 60 FPS, 120 FPS, No limit, and then back to 30 FPS. **F2** shows or hides the overlay. The window title also shows the current ceiling. The selection lasts for the session. Reassembly requires Kyty patch 0007 or later for the limiter.

Set `ASTROHYBRID_FPS_LIMIT` to `30`, `60`, `120`, or `unlimited` before launch to choose a starting value. With the variable unset, the regular launchers keep their previous presentation behavior. The limiter spaces guest flips while keeping the game's virtual vblank clock at its configured rate. No limit removes the AstroHybrid ceiling. Game performance, guest pacing and display presentation can still constrain FPS.

The empty-cache run wrote 333 shader sources, 345 permutations and 405 pipeline journal records. In the 65-105 s window after first present, the same-DLL cache-off control had a 103.71 ms worst frame and six late program compiles. The two warm runs had 54.75 and 50.48 ms worst frames and zero late program compiles. The control spent 58.59 ms in recorded compile waits, versus 0.38 and 0.41 ms in the warm runs. These measurements cover the pinned Sky Garden scene with tracing active. Ordinary late p99 remained about 42-45 ms. The data supports keeping the opt-in cache fix for long compile-related spikes. Sustained throughput still depends on draw commit and command supply to the GPU.

Cache-title build DLL SHA-256 `5787fd7d162e934fb275199cd8c1bb8be8cf83ac433a7c71fae8c9e3541b8833`. Logs: `run-20261108-013651` (cold), `013926` (warm), `014130` (control), `014334` (warm repeat). The working launcher defaults and prior builds remain in place.
