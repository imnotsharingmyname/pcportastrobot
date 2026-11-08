# AGENTS.md — rules for any AI agent working on this project

Project: a native Windows port of the user's own PS5 game Astro Bot (PPSA21567, v01.019.000), built with
AnyPS5 (relinks the game's x86-64 code into a Windows .exe and reimplements the PS5 system libraries natively).
It is NOT an emulator and must not become one. Read `Docs/HANDOVER.md` first: it has the current state, the exact
build and test commands and the next steps.

2026-11-06, user decision: the port becomes a hybrid. AstroBot.exe stays the native AnyPS5 executable, and
KytyPS5 components (GPU renderer, shader recompiler, SRT replay, memory tracking) may be ported into it, for
example as a DLL behind the AGC/VideoOut NID exports. The game must keep launching as AstroBot.exe, not
through a separate emulator application. Plan and evidence: `Docs/HYBRID_GPU_PLAN.md`.

## Hard rules (never break these)

1. Never delete, move, rename or modify `E:\PPSA21567.7z`.
2. Never delete or modify anything under `Game\` (the original game files). Read only. No re-extraction, no copies:
   use the existing folder through links (`Build\Run\app0` is a junction to `Game\PPSA21567-app`).
3. Keep at least 30 GB free on C:. Never delete files you did not create in this project. Old `Logs\run-*` folders
   may be deleted only if space runs low, oldest first.
4. Do not download Sony SDKs, keys or game assets from anywhere.
5. No system-wide changes: no driver installs, no security settings, no registry edits, no disabling antivirus.
6. Never claim a speed-up or a fix you did not measure. Every change is tested with the benchmark
   (`Tests\worldmap_bench.ps1`) and a screenshot check. Write every result, good or bad, as a new row at the TOP
   of the table in `Docs/OVERNIGHT.md`.
7. Do not `git push` anywhere. Commit locally only.
8. Keep new behaviour behind an environment switch that is OFF by default. Only change `Run-AstroBot.bat`
   defaults after an A/B benchmark shows a real gain and the screenshots look right.
9. Inside `Tools\AnyPS5` follow `Tools\AnyPS5\AGENTS.md` (no comments in code, Conventional Commit subjects).

## Lessons already learned (do not repeat)

- Do not split one recorder batch into several Vulkan command buffers: it crashes (`0xC0000005` in nvoglv64).
- Dispatches as committer items (`APS5_PIPELINE_DISPATCH`), deferred copy-backs, 32 MiB shadow window,
  batch caps, a second compute queue (`APS5_COMPUTE_QUEUE`) were all measured and did NOT help. See OVERNIGHT.md.
- Back-to-back benchmark runs: wait until no `AstroBot*` process is left before starting the next one.
- If a benchmark is interrupted, `Build\Run\app0` may still point to `Build\app0-bench`. Restore it with:
  `cmd /c rmdir "Build\Run\app0"` then `cmd /c mklink /J "Build\Run\app0" "Game\PPSA21567-app"`
  (rmdir on a junction removes only the link, never the game files).
