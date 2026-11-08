# AstroHybrid Sky Garden run: Tests\skygarden_bench.ps1 on Build\RunHybrid (AstroBot.exe with Kyty's GPU stack)
# with the same settings as the Kyty baseline (E:\AstroHybrid-Work\tests\kyty_bench.ps1): the Kyty engine's
# u59 preset, 1280x720, Immediate presentation, Performance shaders, and by default the non-RT game patch
# (AstroBot-NonRT.exe = the Kyty launcher's Candidate-NonRT.json). Kyty logs its own fps
# ("Kyty fault cost: last N s, F fps") in stdout.log.
# Usage: & Tests\hybrid_skygarden.ps1 [-Seconds 170] [-Exe AstroBot-NonRT.exe] [-Env @{ NAME = 'value' }]
param(
    [int]$Seconds = 170,
    [string]$Exe = 'AstroBot-NonRT.exe',
    [hashtable]$Env = @{},
    [int]$Shots = 8,
    [string]$RunDir = 'Build\RunHybrid'
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Hybrid = Join-Path $Root $RunDir
$e = @{}
$preset = Join-Path $Hybrid 'u59-preset.json'
if (Test-Path $preset) {
    $json = Get-Content -LiteralPath $preset -Raw | ConvertFrom-Json
    foreach ($p in $json.PSObject.Properties) { $e[$p.Name] = [string]$p.Value }
}
$e['APS5_GPU_ENGINE'] = 'kyty'
$e['KYTY_GPU_HOST_MODULE'] = 'astrohybrid_host.dll'
$e['KYTY_GPU_WIDTH'] = '1280'
$e['KYTY_GPU_HEIGHT'] = '720'
$e['KYTY_PRESENT_ABS_VBLANK'] = '1'
$e['KYTY_FAULT_COST_LOG'] = '10'
$e['APS5_WRITE_ALARMS'] = '0'
$e['APS5_PROFILE_DRAW'] = '0'
$e['KYTY_HANG_WATCHDOG'] = '0'
$e['APS5_PAD_TRIGGER_STATE'] = '1'
$e['KYTY_DRAW_PREP_PRIORITY'] = '2'
$e['KYTY_DRAW_PREP_SPIN_US'] = '10'
$e['KYTY_DRAW_PREP_COLD_SPIN_US'] = '5'
foreach ($k in $Env.Keys) { $e[$k] = $Env[$k] }
& (Join-Path $PSScriptRoot 'skygarden_bench.ps1') -RunDir $RunDir -Exe $Exe -Seconds $Seconds -Shots $Shots -Env $e
