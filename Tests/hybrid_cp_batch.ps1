param([string]$Label = 'takeover',
      [string]$Cases = 'control,priority1short,control',
      [switch]$PrepTune)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$stamp = Get-Date -Format yyyyMMdd-HHmmss
$manifest = Join-Path $Root "Logs\cp-$Label-$stamp.txt"
$priority = Start-Job -ScriptBlock {
    while ($true) {
        Get-Process AstroBot* -ErrorAction SilentlyContinue | ForEach-Object {
            try { $_.PriorityClass = 'AboveNormal' } catch {}
        }
        Start-Sleep -Milliseconds 200
    }
}
try {
    foreach ($case in $Cases.Split(',')) {
        while (Get-Process AstroBot* -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
        if ((Get-PSDrive C).Free -lt 30GB) { throw 'C: free space below 30 GiB' }
        $e = @{
            KYTY_CP_GAP_STATS='1'; KYTY_CP_GAP_STATS_INTERVAL_MS='10000'; KYTY_CP_PRIORITY='2'
            KYTY_DRAW_PREP_HOT='2'; KYTY_DRAW_PREP_WORKERS='6'; KYTY_DRAW_PREP_WAKE_BACKLOG='2'
            KYTY_DRAW_PREP_STEAL='0'; APS5_NO_POWER_THROTTLING=''; KYTY_DRAW_PREP_PRIORITY='0'
            KYTY_DRAW_PREP_SPIN_US='200'; KYTY_DRAW_PREP_COLD_SPIN_US='50'
            KYTY_CP_BINDING_MEMO_BITS=''; KYTY_SKIP_INACTIVE_PS='0'
            KYTY_SUBMIT_MIN_INTERVAL_US='0'; KYTY_IDLE_FLUSH_DRAWS='8'; KYTY_GFX_SLICE_DRAWS='128'
            KYTY_EOP_FLUSH_PACKETS='256'
            KYTY_FRAME_TIMES_LOG=(Join-Path $Root "Logs\ft-$Label-$stamp-$case-$([guid]::NewGuid().ToString('N').Substring(0,6)).csv")
        }
        if ($PrepTune) {
            $e.KYTY_DRAW_PREP_PRIORITY='1'
            $e.KYTY_DRAW_PREP_SPIN_US='10'
            $e.KYTY_DRAW_PREP_COLD_SPIN_US='5'
        }
        switch ($case) {
            'control' {}
            'hot4' { $e.KYTY_DRAW_PREP_HOT='4' }
            'hot6' { $e.KYTY_DRAW_PREP_HOT='6' }
            'workers4' { $e.KYTY_DRAW_PREP_WORKERS='4'; $e.KYTY_DRAW_PREP_HOT='4' }
            'workers2' { $e.KYTY_DRAW_PREP_WORKERS='2'; $e.KYTY_DRAW_PREP_HOT='2' }
            'workers8' { $e.KYTY_DRAW_PREP_WORKERS='8'; $e.KYTY_DRAW_PREP_HOT='4' }
            'nothrottle' { throw 'EcoQoS experiment was reverted; its patch is in Source/experiments.' }
            'priority1' { $e.KYTY_DRAW_PREP_PRIORITY='1' }
            'priority1short' {
                $e.KYTY_DRAW_PREP_PRIORITY='1'
                $e.KYTY_DRAW_PREP_SPIN_US='10'
                $e.KYTY_DRAW_PREP_COLD_SPIN_US='5'
            }
            'priority1short2' {
                $e.KYTY_DRAW_PREP_PRIORITY='1'
                $e.KYTY_DRAW_PREP_SPIN_US='10'
                $e.KYTY_DRAW_PREP_COLD_SPIN_US='5'
                $e.KYTY_DRAW_PREP_WORKERS='2'
            }
            'priority2short' {
                $e.KYTY_DRAW_PREP_PRIORITY='2'
                $e.KYTY_DRAW_PREP_SPIN_US='10'
                $e.KYTY_DRAW_PREP_COLD_SPIN_US='5'
            }
            'shortspin' {
                $e.KYTY_DRAW_PREP_SPIN_US='10'
                $e.KYTY_DRAW_PREP_COLD_SPIN_US='5'
            }
            'priority2' { $e.KYTY_DRAW_PREP_PRIORITY='2' }
            'memo18' { throw 'Memo sizing was reverted; its patch is in Source/experiments.' }
            'memo16' { throw 'Memo sizing was reverted; its patch is in Source/experiments.' }
            'inactiveps' { $e.KYTY_SKIP_INACTIVE_PS='1' }
            'steal1' { $e.KYTY_DRAW_PREP_STEAL='1' }
            'reserveoff' { $e.KYTY_CPU_RESERVE='off' }
            'sibling' { $e.KYTY_CPU_RESERVE='cp+sibling' }
            'submit1000' { $e.KYTY_SUBMIT_MIN_INTERVAL_US='1000' }
            'submit2000' { $e.KYTY_SUBMIT_MIN_INTERVAL_US='2000' }
            'eop1024' { $e.KYTY_EOP_FLUSH_PACKETS='1024' }
            'submitcombo' {
                $e.KYTY_SUBMIT_MIN_INTERVAL_US='1000'
                $e.KYTY_EOP_FLUSH_PACKETS='1024'
            }
            'idle64' { $e.KYTY_IDLE_FLUSH_DRAWS='64' }
            'idle0' { $e.KYTY_IDLE_FLUSH_DRAWS='0' }
            'slice512' { $e.KYTY_GFX_SLICE_DRAWS='512' }
            'occlusion' { $e.KYTY_GPU_OCCLUSION='1' }
            'nowatchdog' { $e.KYTY_HANG_WATCHDOG='0' }
            'watchdog' { $e.KYTY_HANG_WATCHDOG='auto' }
            'fastevents' { $e.ASTROHYBRID_WATCHDOG_FAST_EVENTS='1' }
            'gpuends' {
                $e.KYTY_GPU_OP_COUNTERS='1'
                $e.KYTY_GPU_OP_PROFILE='90'
                $e.KYTY_GPU_OP_PROFILE_MAX='2'
                $e.KYTY_GPU_OP_PROFILE_STAMPS='passes'
                $e.KYTY_GPU_OP_PROFILE_DIR=(Join-Path $Root "Logs\gpuends-$stamp")
            }
            default { throw "Unknown case: $case" }
        }
        "START $case $(Get-Date -Format o)" | Tee-Object -FilePath $manifest -Append
        & "$PSScriptRoot\hybrid_skygarden.ps1" -Seconds 150 -Shots 2 -Env $e
        $run = Get-ChildItem "$Root\Logs" -Directory -Filter 'run-*' | Sort-Object Name | Select-Object -Last 1
        "RESULT $case $($run.FullName)" | Tee-Object -FilePath $manifest -Append
        & python -I "$PSScriptRoot\hybrid_cp_report.py" $run.FullName |
            Set-Content "$($run.FullName)\cp-report.json"
        $report = (Get-Content "$($run.FullName)\cp-report.json" -Raw | ConvertFrom-Json)[0]
        $m = $report.median
        $row = "| $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | CP experiment $case, $($run.Name), 150 s PGO Sky Garden, AboveNormal process. | $($report.result); $($report.windows) CP windows; last-six median wall=$($m.wall) ms, commit=$($m.commit) ms, draws=$($m.draws)/frame, cost=$($m.us_draw) us/draw, gap=$($m.gap) ms, deep=$($m.deep) ms. Frame CSV retained in Logs. | Measurement only; no default change, paired and image analysis pending. |"
        $log = Join-Path $Root 'Docs\OVERNIGHT.md'
        $text = [IO.File]::ReadAllText($log)
        $separator = '|---|---|---|---|'
        $at = $text.IndexOf($separator) + $separator.Length
        $text = $text.Insert($at, "`n$row")
        [IO.File]::WriteAllText($log, $text, [Text.UTF8Encoding]::new($false))
        Start-Sleep -Seconds 2
    }
} finally {
    Stop-Job $priority
    Remove-Job $priority
}
