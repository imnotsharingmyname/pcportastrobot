# Next engineering tasks (in priority order)

1. Diagnose the `WAIT_REG_MEM_64` timeouts (KNOWN_ISSUES #1): run with
   `APS5_TRACE_LABEL=0x400201a40`, capture `APS5_DUMP_SUBMITS=<empty dir>`, replay with
   `agc_pm4_replay`, and find why queue 0's write at +4 does not satisfy the 64-bit wait. Fix in
   `Tools/AnyPS5/core/libs/prx/libSceAgcDriver`, rebuild libs, retest FPS.
2. Find the source of the tile-shaped holes: rerun once with `APS5_HOST_IMPORT_MIB` high enough
   that nothing is refused (or with a smaller title resolution) to separate import refusal from the
   storage-image "keeping the CPU's blocks" path.
3. Turn the pinned-lease-after-refused-import abort into a proper fix (or an upstream issue with
   logs) instead of relying on a bigger budget.
4. Install Noto substitute fonts into `Build/Run/anyps5-fonts`.
5. Play past the intro into the Crash Site hub with keyboard and an SDL controller; verify movement,
   jump, camera.
6. Verify audio output (`libSceAudioOut2` path) and record whether music/SFX play.
7. After restarting the Claude session, use REA + Ghidra on `Analysis/elf/eboot_etdyn.elf` to name the
   functions around the AGC submit path and the async-compute wait sites.
8. Profile once frames are stable: Tracy build (`-DAPS5_ENABLE_TRACY=ON`), 1080p vs 1440p.
