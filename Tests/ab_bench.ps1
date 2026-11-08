# A/B benchmark: the launcher defaults ("base") against the defaults plus extra switches ("test").
# Usage (PowerShell, from the project root):
#   & Tests\ab_bench.ps1 -Test @{ APS5_RECLAIM_RESULTS = '1' } -Rounds 2
# Then: python Tests\presents.py <the run folders it printed>
param(
    [hashtable]$Test = @{},
    [ValidateRange(1, 100)][int]$Rounds = 2,
    [ValidateRange(30, 3600)][int]$Seconds = 150,
    [switch]$GpuTelemetry,
    [hashtable]$Env = @{}
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$session = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
function Defaults {
    @{ APS5_RESIDENT_STAGING = '1'; APS5_PIPELINED_DRAWS = '64'; APS5_GDS_GPU_STORES = '1'; APS5_GDS_PLAIN_STORES = '1';
       APS5_DRAW_SKIP_GPU_SNAPSHOTS = '1'; APS5_DRAW_READ_SHADOWS = '1'; APS5_COPY_IN_FROM_SHADOWS = '1';
       APS5_WRITE_ALARMS = '1'; APS5_WRITTEN_SHADOW_MAX_KIB = '16384'; APS5_BDA_NO_SHADOW_PUBLISH = '1';
       APS5_PROFILE_DRAW = '1'; APS5_RECLAIM_RESULTS = '0'; APS5_LINEAR_BLOCK_TRACKING = '1'; APS5_SYNC_COMPLETION_LABELS_ONLY = '1'; APS5_SAMPLED_BUDGET_FLOOR_MIB = '3584'; APS5_POOL_SHED_OVER_BUDGET = '1' }
}
foreach ($round in 1..$Rounds) {
    $modes = if ($round % 2 -eq 0) { @('test', 'base') } else { @('base', 'test') }
    foreach ($mode in $modes) {
        $e = Defaults
        foreach ($key in $Env.Keys) { $e[$key] = $Env[$key] }
        if ($GpuTelemetry) { $e['APS5_PROFILE_GPU'] = '1' }
        if ($mode -eq 'test') { foreach ($k in $Test.Keys) { $e[$k] = $Test[$k] } }
        for ($i = 0; $i -lt 60 -and (Get-Process -Name 'AstroBot*' -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 2 }
        if (Get-Process -Name 'AstroBot*' -ErrorAction SilentlyContinue) { throw 'Previous AstroBot process did not exit' }
        "=== round $round $mode"
        $mon = $null
        try {
            if ($GpuTelemetry) {
                $smi = Join-Path $Root "Logs\smi-ab-$session-$round-$mode.csv"
                $telemetry = Join-Path $PSScriptRoot 'gpu_telemetry.py'
                $mon = Start-Process -FilePath 'python' -ArgumentList '-I', ('"{0}"' -f $telemetry), ('"{0}"' -f $smi) -PassThru -WindowStyle Hidden -RedirectStandardError "$smi.errors.txt"
                for ($i = 0; $i -lt 50 -and -not $mon.HasExited -and (-not (Test-Path $smi) -or (Get-Item $smi).Length -eq 0); $i++) { Start-Sleep -Milliseconds 100 }
                if ($mon.HasExited -or -not (Test-Path $smi) -or (Get-Item $smi).Length -eq 0) { throw "GPU telemetry did not start; see $smi.errors.txt" }
                "nvidia-smi log: $smi"
            }
            & (Join-Path $Root 'Tests\worldmap_bench.ps1') -Seconds $Seconds -Env $e
        } finally {
            if ($null -ne $mon) { Stop-Process -Id $mon.Id -Force -ErrorAction SilentlyContinue }
        }
    }
}
