# Repeatable gameplay benchmark: boots with -skipIntro (straight to the world map) and runs run_test.ps1
# with the given extra environment. The game folder is never written: Build\Run\app0 is pointed at an
# overlay (Build\app0-bench) of junctions and hard links to the game's files plus its own args.txt for
# the run, and pointed back at the game folder afterwards.
# Usage: & Tests\worldmap_bench.ps1 [-Seconds 330] [-Env @{NAME='value'}] [-Exe AstroBot.exe]
param(
    [int]$Seconds = 330,
    [hashtable]$Env = @{},
    [string]$Exe = 'AstroBot.exe',
    [int[]]$CpuSamples = @(),
    [string]$ExtraArgs = '',
    [string[]]$Keys = @('45:cross', '60:cross', '75:cross', '90:cross'),
    [int]$Shots = 6,
    [string]$RunDir = 'Build\Run'
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Game = Join-Path $Root 'Game\PPSA21567-app'
$Run = Join-Path $Root $RunDir
$Overlay = Join-Path $Root 'Build\app0-bench'
if (Get-Process AstroBot* -ErrorAction SilentlyContinue) { throw 'An AstroBot executable is already running; app0 cannot be redirected' }
if ((Get-PSDrive C).Free -lt 30GB) { throw 'At least 30 GB free on C: is required' }
$app0 = Join-Path $Run 'app0'
$link = Get-Item -LiteralPath $app0 -Force
if ($link.LinkType -ne 'Junction' -or $link.Target -notin @($Game, $Overlay)) { throw "Unexpected app0 junction: $app0" }
function Set-App0Junction([string]$Target) {
    $link = Get-Item -LiteralPath $app0 -Force
    if ($link.FullName -ne [IO.Path]::GetFullPath((Join-Path $Run 'app0')) -or $link.LinkType -ne 'Junction' -or $link.Target -notin @($Game, $Overlay)) { throw "Refusing to replace unexpected app0 path: $app0" }
    $link.Delete()
    New-Item -ItemType Junction -Path $app0 -Target $Target | Out-Null
}
$base = @{ APS5_HOST_IMPORT_MIB = '14848'; APS5_TRACE_SUMMARY_EVERY = '10'; APS5_NO_LABEL_READBACK_STORE = '1' }
foreach ($k in $Env.Keys) { $base[$k] = $Env[$k] }

if (-not (Test-Path $Overlay)) {
    New-Item -ItemType Directory $Overlay | Out-Null
    foreach ($entry in Get-ChildItem -Force $Game) {
        $target = Join-Path $Overlay $entry.Name
        if ($entry.Name -eq 'args.txt') { continue }
        if ($entry.PSIsContainer) { New-Item -ItemType Junction -Path $target -Target $entry.FullName | Out-Null }
        else { New-Item -ItemType HardLink -Path $target -Target $entry.FullName | Out-Null }
    }
}
$line = (Get-Content (Join-Path $Game 'args.txt') -Raw).TrimEnd("`r", "`n") -replace '\s*-skipIntro true', ''
$extra = if ($ExtraArgs) { " $ExtraArgs" } else { '' }
[System.IO.File]::WriteAllText((Join-Path $Overlay 'args.txt'), "$line -skipIntro true$extra`r`n")

Set-App0Junction $Overlay
try {
    & (Join-Path $PSScriptRoot 'run_test.ps1') -Seconds $Seconds -Shots $Shots -Exe $Exe -Env $base -Trigger 'StartLevel title' -Keys $Keys -CpuSamples $CpuSamples -RunDir $RunDir
} finally {
    Set-App0Junction $Game
}
