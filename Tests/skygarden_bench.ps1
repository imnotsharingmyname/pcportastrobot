# Sky Garden benchmark: boots straight into the first level (internal name underwater_aerial_garden) with the
# game's own -lvl argument, through the same app0 overlay as worldmap_bench.ps1 (the game folder is never written),
# with the launcher defaults. The level starts ~30 s after launch; the steady part is the arrival and idle scene.
# Usage: & Tests\skygarden_bench.ps1 [-Seconds 110] [-Env @{ NAME = 'value' }]
param(
    [ValidateRange(60, 3600)][int]$Seconds = 110,
    [hashtable]$Env = @{},
    [int[]]$CpuSamples = @(),
    [int]$Shots = 8,
    [string]$RunDir = 'Build\Run',
    [string]$Exe = 'AstroBot.exe'
)
$ErrorActionPreference = 'Stop'
$e = @{ APS5_RESIDENT_STAGING = '1'; APS5_PIPELINED_DRAWS = '64'; APS5_GDS_GPU_STORES = '1'; APS5_GDS_PLAIN_STORES = '1';
        APS5_DRAW_SKIP_GPU_SNAPSHOTS = '1'; APS5_DRAW_READ_SHADOWS = '1'; APS5_COPY_IN_FROM_SHADOWS = '1';
        APS5_WRITE_ALARMS = '1'; APS5_WRITTEN_SHADOW_MAX_KIB = '16384'; APS5_BDA_NO_SHADOW_PUBLISH = '1';
        APS5_LINEAR_BLOCK_TRACKING = '1'; APS5_SYNC_COMPLETION_LABELS_ONLY = '1'; APS5_SAMPLED_BUDGET_FLOOR_MIB = '3584'; APS5_POOL_SHED_OVER_BUDGET = '1'; APS5_PROFILE_DRAW = '1' }
foreach ($key in $Env.Keys) { $e[$key] = $Env[$key] }
& (Join-Path $PSScriptRoot 'worldmap_bench.ps1') -Seconds $Seconds -Env $e -ExtraArgs '-lvl underwater_aerial_garden' -Keys @() -Shots $Shots -CpuSamples $CpuSamples -RunDir $RunDir -Exe $Exe
