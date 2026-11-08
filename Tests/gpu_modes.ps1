# Runs the world-map benchmark several times with the GPU profiler on, logging nvidia-smi (VRAM used, clocks,
# power) every 5 s to Logs\smi-modes-<session>-<n>.csv, to find out why some runs are GPU-slow (GPU busy ~80 ms/frame)
# and others fast (~56 ms/frame) in the same scene.
# Usage: & Tests\gpu_modes.ps1 -Rounds 3
param([ValidateRange(1, 100)][int]$Rounds = 3, [ValidateRange(30, 3600)][int]$Seconds = 150, [hashtable]$Env = @{}, [int[]]$CpuSamples = @())
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$session = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
foreach ($round in 1..$Rounds) {
    for ($i = 0; $i -lt 60 -and (Get-Process -Name 'AstroBot*' -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 2 }
    if (Get-Process -Name 'AstroBot*' -ErrorAction SilentlyContinue) { throw 'Previous AstroBot process did not exit' }
    if ((Get-PSDrive C).Free -lt 30GB) { throw 'At least 30 GB free on C: is required' }
    $smi = Join-Path $Root "Logs\smi-modes-$session-$round.csv"
    $telemetry = Join-Path $PSScriptRoot 'gpu_telemetry.py'
    $mon = Start-Process -FilePath 'python' -ArgumentList '-I', ('"{0}"' -f $telemetry), ('"{0}"' -f $smi) -PassThru -WindowStyle Hidden -RedirectStandardError "$smi.errors.txt"
    $e = @{ APS5_RESIDENT_STAGING = '1'; APS5_PIPELINED_DRAWS = '64'; APS5_GDS_GPU_STORES = '1'; APS5_GDS_PLAIN_STORES = '1';
            APS5_DRAW_SKIP_GPU_SNAPSHOTS = '1'; APS5_DRAW_READ_SHADOWS = '1'; APS5_COPY_IN_FROM_SHADOWS = '1';
            APS5_WRITE_ALARMS = '1'; APS5_WRITTEN_SHADOW_MAX_KIB = '16384'; APS5_BDA_NO_SHADOW_PUBLISH = '1';
            APS5_PROFILE_DRAW = '1'; APS5_PROFILE_GPU = '1'; APS5_LINEAR_BLOCK_TRACKING = '1'; APS5_SYNC_COMPLETION_LABELS_ONLY = '1'; APS5_SAMPLED_BUDGET_FLOOR_MIB = '3584'; APS5_POOL_SHED_OVER_BUDGET = '1' }
    foreach ($key in $Env.Keys) { $e[$key] = $Env[$key] }
    "=== round $round (nvidia-smi log: $smi)"
    try {
        for ($i = 0; $i -lt 50 -and -not $mon.HasExited -and (-not (Test-Path $smi) -or (Get-Item $smi).Length -eq 0); $i++) { Start-Sleep -Milliseconds 100 }
        if ($mon.HasExited -or -not (Test-Path $smi) -or (Get-Item $smi).Length -eq 0) { throw "GPU telemetry did not start; see $smi.errors.txt" }
        & (Join-Path $Root 'Tests\worldmap_bench.ps1') -Seconds $Seconds -Env $e -CpuSamples $CpuSamples
    }
    finally { Stop-Process -Id $mon.Id -Force -ErrorAction SilentlyContinue }
}
