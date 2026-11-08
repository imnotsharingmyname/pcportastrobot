# Async compute on its own Vulkan queue — plan

Goal: run the game's async-compute queues (AGC queues 0x40 and 0x48) on a Vulkan compute queue next to
graphics, as the PS5 hardware does, instead of recording every queue into one Vulkan command stream.

## Why (measured on the world map, 2026-10-29, ~12.9 FPS)

- ~94 GPU batches per frame. Most are forced: a compute-queue `WAIT_REG_MEM` on a queue-0 label needs the
  batch holding that label submitted now.
- ~830 pipeline barriers per frame; every batch ends with a full drain before its label stores.
- Render passes are broken ~3300 times per 10 s by compute work recorded between draws (`command`), and
  ~6300 times by batch ends (`new batch`).
- The queue-0 worker still spends ~29% of its time in pipeline drains; the committer waits on the GPU
  mutex that compute workers hold while recording.

## Current design (AnyPS5)

- One `VkQueue` (`VulkanDevice` `state->queue`), one global `Recorder` (`Recorder::Active()`, 114 call sites
  in 19 files, ~150 public functions). Batches have one global serial order; `Sync`, `FinishUpTo`, reaping
  and completions assume batches finish front-first.
- Every queue worker records under `GuestMemory::GpuMutex()` into the recorder's open batch.
- Pending GPU writes, labels, copied writers and completions are tracked once, in that recorder.

## Facts checked for the design

- RTX 5060 queue families: family 0 has 16 graphics+compute queues (also 8 compute-only queues in family
  2). Lane C can take a second queue of family 0: no queue-family ownership transfers.
- `Recorder::ensureOpen` opens one batch for whichever thread records first; `Batch::queue` only tags that
  thread (`GuestMemory::GpuLockThreadTag()`), so today's batches mix work of every AGC queue.
- `Batch` holds one command buffer, one fence, kept resources, completions, pending writes/reads, label
  dwords and timing queries: Stage 1 turns `open` into one open batch per lane.

## Target design: one recorder, two lanes

Keep one `Recorder` as the single owner of pending-write, label and completion tracking. Split only the
command streams:

- Lane G (graphics: queue 0 and 0x46) and lane C (compute: 0x40, 0x48), each with its own open batch,
  command buffer, `VkQueue` (a second queue of the graphics family or a compute-only family) and a
  timeline semaphore whose value is the lane's batch serial.
- A thread records into its own lane (`Commands()` picks the lane of the calling queue worker).
- Cross-lane order comes only from the game's own synchronization, as on hardware:
  - a label written in lane A and waited for in lane B: lane B's next submit waits on lane A's timeline
    value of the batch holding the label (GPU-side, no CPU wait, no forced flush of lane B);
  - driver-side hazards (a pending write of lane A read by lane B's recorded work): the reader's lane
    waits on the writer's timeline value. The pending-write table already knows the writer batch serial.
- `Sync`, `SyncThrough`, `FinishUpTo`, `Reap`: per-lane in-flight lists; "through serial N" waits on the
  owning lane's timeline. Completions run per lane in order.

## Stage 1 result (2026-10-30, patch 0011)

Implemented as one lane per batch, not per-lane command buffers inside a batch: a thread of the other
lane submits the open batch first (`Recorder::ensureOpen`). Per-lane buffers inside a batch crashed:
some recording paths keep a command buffer across a submit-and-wait and only work because the next
batch reuses that buffer, and the GPU-lock thread tag changes during waits (lanes are now fixed per
queue worker with `Recorder::SetThreadLane`). Validation clean, picture correct, ~5% slower on one
queue (more submits). Stage 2 now submits compute-lane batches to a second queue of family 0 and
replaces the implicit order between lanes with timeline-semaphore waits.

## Stage 2a result (2026-10-30, patch 0012)

`APS5_COMPUTE_QUEUE=1` (with `APS5_COMPUTE_LANE=1`) submits compute-lane batches to queue 3 of family 0. A batch
whose lane differs from the previous one waits on the recorder timeline for it; the present blit waits for the
newest batch. Validation clean, picture correct, ~8% slower than defaults (no overlap yet, one more
cross-queue wait per lane switch). Stage 2b cannot just drop waits: the recorder has one timeline whose
values must be signaled in order, and `WaitSerial`, the in-flight list and reaping assume front-first
completion. 2b needs a timeline per lane and a serial to (graphics value, compute value) map. Its ceiling
is the compute time per frame (~13 ms of ~56 ms GPU busy).

**Stage 2b postponed (2026-10-30):** a profile on the defaults shows the GPU idle ~22 ms per ~77 ms frame and no CPU
thread saturated; cutting GPU time by 9 ms earlier gave no FPS. Overlapping compute only shortens GPU time, so it
waits until the CPU-GPU hand-off chain is shorter.

## Stages (each behind `APS5_COMPUTE_LANE`, off by default; each must keep the picture correct)

1. **Lane plumbing, single queue.** Two open batches with their own command buffers, both submitted to the
   existing queue in serial order. Proves that recording into two streams keeps every tracker correct.
   Expected gain: fewer render-pass breaks (`command`).
2. **Second VkQueue + timeline semaphores.** Lane C submits to its own queue; label waits become semaphore
   waits instead of forced submits. Expected gain: fewer batches and drains, GPU overlap.
3. **Lock split.** Compute workers stop taking the global GPU mutex for recording into lane C (per-lane
   lock), so the committer no longer waits for them.

## Risks

- Every recorder query that assumes one serial order (pending writes, `PendingWriteSettled`, label
  lookups, completion order).
- Resources shared by both lanes (staging shadows, unit-shadow slabs, descriptor pools, upload rings):
  queue-family ownership (use one family with two queues where possible, so no ownership transfers).
- Validation: run `VK_LAYER_KHRONOS_validation` on short scenes for each stage.
