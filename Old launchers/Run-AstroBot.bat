@echo off
rem Launches the relinked Astro Bot (AnyPS5 runtime). Build first with: powershell -ExecutionPolicy Bypass -File Tools\build.ps1
rem Status: reaches title screen and intro cutscene; NOT a finished port (see KNOWN_ISSUES.md).
setlocal
set "ROOT=%~dp0..\"
if not exist "%ROOT%Build\Run\AstroBot.exe" (
  echo Build\Run\AstroBot.exe not found. Run Tools\build.ps1 first.
  exit /b 1
)
rem Default host-import budget (8159 MiB here) is exhausted at boot on this PC and the game aborts with
rem 0xC0000409 after a 60 s pinned-lease wait. 14848 MiB lets the 3.2 GB range at 0x1000000000 import
rem instead of being refused and copied on every use (no refusals, ~5% faster in game; see PERFORMANCE.md).
if not defined APS5_HOST_IMPORT_MIB set APS5_HOST_IMPORT_MIB=14848
rem Skip the eager write-back of every pending linear/small GPU image before each label: it moved
rem ~145 MiB/frame over PCIe; CPU reads still get the data through the flush hook. Menu ~10 -> ~18 FPS.
if not defined APS5_NO_LABEL_READBACK_STORE set APS5_NO_LABEL_READBACK_STORE=1
rem 64-slot hand-off queue between the draw translator and the draw recorder (default 8): the two threads
rem took turns waiting on each other; world map 112 -> 100 ms per frame (run-20261029-000701).
if not defined APS5_PIPELINED_DRAWS set APS5_PIPELINED_DRAWS=64
rem Skip copy-ins of device-local buffers the write tracker shows unchanged since their last copy-back.
rem World map 101.2 -> 102.9 presents per 10 s (run-20261029-033151); four checked screenshots correct.
if not defined APS5_RESIDENT_STAGING set APS5_RESIDENT_STAGING=1
rem GDS counter resets (4-byte DMA_DATA into GDS) are recorded as GPU stores instead of draining the GPU
rem ~10 times a frame. World map 89 -> 108 presents per 10 s, drains ~1000 -> 0 (run-20261029-120317).
if not defined APS5_GDS_GPU_STORES set APS5_GDS_GPU_STORES=1
rem GDS stores as plain GPU stores (not labels): dispatches stop syncing on them under the GPU lock.
rem Committer lock waits ~1500 -> 646 ms per 10 s (run-20261029-185552).
if not defined APS5_GDS_PLAIN_STORES set APS5_GDS_PLAIN_STORES=1
rem Draws read GPU-produced buffers (particles) in place instead of CPU-copying a snapshot of them;
rem the game's own code never touched them (APS5_RESIDENT_TRAP). World map 105 -> ~110 presents per 10 s.
if not defined APS5_DRAW_SKIP_GPU_SNAPSHOTS set APS5_DRAW_SKIP_GPU_SNAPSHOTS=1
rem ...and read them from the dispatch's copy in video memory instead of system RAM over PCIe.
rem World map 106-108 -> 113-114 presents per 10 s (run-20261029-194621).
if not defined APS5_DRAW_READ_SHADOWS set APS5_DRAW_READ_SHADOWS=1
rem Compute copy-ins take GPU results from video memory instead of system RAM: ~2-3 ms less GPU time.
if not defined APS5_COPY_IN_FROM_SHADOWS set APS5_COPY_IN_FROM_SHADOWS=1
rem Write alarms: memory pages already checked are locked read-only, so a first CPU write trips once
rem instead of the driver asking Windows (GetWriteWatch) ~1300 times a frame. World map ~113 -> ~118 per 10 s.
if not defined APS5_WRITE_ALARMS set APS5_WRITE_ALARMS=1
rem Stage written buffers up to 16 MiB in video memory (default 8): the particle pass stops writing
rem system RAM over PCIe; compute GPU time 19 -> 12.7 ms per frame, world map ~121 -> ~124 per 10 s.
if not defined APS5_WRITTEN_SHADOW_MAX_KIB set APS5_WRITTEN_SHADOW_MAX_KIB=16384
rem Address-based compute (the particle pass) no longer copies every image result in its heap back to
rem system RAM first (~27 MiB per run): GPU busy 60 -> 56 ms, world map ~122 -> ~129 per 10 s.
if not defined APS5_BDA_NO_SHADOW_PUBLISH set APS5_BDA_NO_SHADOW_PUBLISH=1
rem Linear storage views share the VRAM unit shadows too. Three paired world-map runs gained ~2.5 percent,
rem GPU busy ~56 -> ~54 ms; focused Vulkan tests and a 180 s validation run passed, screenshots correct.
if not defined APS5_LINEAR_BLOCK_TRACKING set APS5_LINEAR_BLOCK_TRACKING=1
rem Resource reads wait only for labels written at batch completion, not GPU-stored ones: label syncs
rem ~600 -> ~13 per 10 s, three paired world-map runs 134-135 -> 140-142 frames per 10 s, screenshots correct.
if not defined APS5_SYNC_COMPLETION_LABELS_ONLY set APS5_SYNC_COMPLETION_LABELS_ONLY=1
rem Sky Garden uses ~3.4 GB of textures per frame; under the old 2 GB cache budget every extra texture was rebuilt on
rem every use (0.1 FPS). A 3.5 GB floor keeps them cached; the world map is unchanged.
if not defined APS5_SAMPLED_BUDGET_FLOOR_MIB set APS5_SAMPLED_BUDGET_FLOOR_MIB=3584
rem When video memory is over budget (Sky Garden on an 8 GB card) the buffer pool frees its cached device buffers
rem instead of pushing new textures into system memory: Sky Garden 11-14 -> 23-25 frames per 10 s; world map unchanged.
if not defined APS5_POOL_SHED_OVER_BUDGET set APS5_POOL_SHED_OVER_BUDGET=1
if not defined APS5_OUTPUT_RESOLUTION set APS5_OUTPUT_RESOLUTION=2560x1440
if not exist "%ROOT%Logs" mkdir "%ROOT%Logs"
cd /d "%ROOT%Build\Run"
echo Logging to Logs\last-run.out.log / Logs\last-run.err.log
AstroBot.exe 1>"%ROOT%Logs\last-run.out.log" 2>"%ROOT%Logs\last-run.err.log"
set CODE=%ERRORLEVEL%
echo AstroBot.exe exited with %CODE%
exit /b %CODE%
