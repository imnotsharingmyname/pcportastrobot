# Launches the relinked title with logging, periodic window screenshots and a timeout.
# Usage: powershell -File Tests\run_test.ps1 [-Seconds 300] [-Shots 15] [-Env @{NAME='value'}]
#        [-Trigger 'StartLevel title'] [-Keys @('20:0x0D','40:0x0D')]
# -Keys entries are "<seconds after the trigger line appears in stdout>:<virtual-key code>"; each key is
# sent only if the game window is the foreground window, so keystrokes never reach other applications.
param(
    [int]$Seconds = 300,
    [int]$Shots = 15,
    [hashtable]$Env = @{},
    [string]$Trigger = '',
    [string[]]$Keys = @(),
    [int[]]$CpuSamples = @(),
    [switch]$RenderDoc,
    [string]$Trigger2 = '',
    [string[]]$Keys2 = @(),
    [string]$Exe = 'AstroBot.exe',
    [string]$RunDir = 'Build\Run'
)
$ErrorActionPreference = 'Stop'
if (Get-Process AstroBot* -ErrorAction SilentlyContinue) { throw 'An AstroBot executable is already running; measurements would share the GPU' }
$Root = Split-Path -Parent $PSScriptRoot
$Run = Join-Path $Root $RunDir
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$LogDir = Join-Path $Root "Logs\run-$ts"
New-Item -ItemType Directory -Force $LogDir | Out-Null

$defaults = @{ APS5_OUTPUT_RESOLUTION = '2560x1440' }
if ($RenderDoc) {
    # Loads RenderDoc's Vulkan layer into this process only (no system-wide layer registration);
    # F12 in the game window then captures a frame into $LogDir.
    $defaults['VK_ADD_LAYER_PATH'] = Join-Path $Root 'Tools\renderdoc\RenderDoc_1.46_64'
    $defaults['VK_INSTANCE_LAYERS'] = 'VK_LAYER_RENDERDOC_Capture'
    $defaults['ENABLE_VULKAN_RENDERDOC_CAPTURE'] = '1'
    $defaults['RENDERDOC_CAPFILE'] = Join-Path $LogDir 'astrobot'
}
foreach ($k in $defaults.Keys) { if (-not $Env.ContainsKey($k)) { $Env[$k] = $defaults[$k] } }
$savedEnv = @{}
foreach ($k in $Env.Keys) { $savedEnv[$k] = [Environment]::GetEnvironmentVariable($k); Set-Item "env:$k" $Env[$k] }
$Env.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" } | Set-Content "$LogDir\env.txt"

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
  public static bool Tap(IntPtr h, byte vk) {
    SetForegroundWindow(h);
    System.Threading.Thread.Sleep(150);
    if (GetForegroundWindow() != h) return false;
    byte sc = (byte)MapVirtualKey(vk, 0);
    keybd_event(vk, sc, 0, UIntPtr.Zero);
    System.Threading.Thread.Sleep(120);
    keybd_event(vk, sc, 2, UIntPtr.Zero);
    return true;
  }
}
'@
function ParseKeys($list, $group) {
    @($list | ForEach-Object {
        $a = $_ -split ':'
        if ($a[1] -match '^0x') { [pscustomobject]@{ Group = $group; At = [double]$a[0]; Vk = [byte][Convert]::ToInt32($a[1], 16); Pad = $null; Done = $false } }
        else { [pscustomobject]@{ Group = $group; At = [double]$a[0]; Vk = 0; Pad = $a[1]; Done = $false } }
    })
}
$pending = @(ParseKeys $Keys 1) + @(ParseKeys $Keys2 2)
$trigger2At = $null
$PadFile = "$LogDir\pad.txt"
$PadTool = Join-Path $Root 'Tools\AnyPS5\tools\pad_input.py'
if ($pending | Where-Object Pad) { $env:APS5_PAD_INPUT_FILE = $PadFile; "APS5_PAD_INPUT_FILE=$PadFile" | Add-Content "$LogDir\env.txt" }
$triggerAt = $null

"exe=$Exe" | Add-Content "$LogDir\env.txt"
$p = Start-Process -FilePath (Join-Path $Run $Exe) -WorkingDirectory $Run `
    -RedirectStandardOutput "$LogDir\stdout.log" -RedirectStandardError "$LogDir\stderr.log" -PassThru
$null = $p.Handle  # keeps the handle so ExitCode is available after exit
$start = Get-Date
$interval = [math]::Max(5, [int]($Seconds / [math]::Max(1, $Shots)))
$next = $interval
$peakMB = 0
while (-not $p.HasExited -and ((Get-Date) - $start).TotalSeconds -lt $Seconds) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if (-not $p.HasExited) { $peakMB = [math]::Max($peakMB, [math]::Round($p.PrivateMemorySize64 / 1MB)) }
    $t = ((Get-Date) - $start).TotalSeconds
    if ($Trigger -and $null -eq $triggerAt -and (Select-String -Path "$LogDir\stdout.log" -SimpleMatch $Trigger -Quiet)) {
        $triggerAt = $t
        "[harness] trigger '$Trigger' seen at {0:N0}s" -f $t | Add-Content "$LogDir\harness.log"
    }
    if ($Trigger2 -and $null -eq $trigger2At -and (Select-String -Path "$LogDir\stdout.log" -SimpleMatch $Trigger2 -Quiet)) {
        $trigger2At = $t
        "[harness] trigger2 '$Trigger2' seen at {0:N0}s" -f $t | Add-Content "$LogDir\harness.log"
    }
    foreach ($k in $pending) {
        $base = if ($k.Group -eq 2) { $trigger2At } else { $triggerAt }
        if ($null -eq $base) { continue }
        if (-not $k.Done -and $t -ge $base + $k.At -and $k.Pad) {
            python -I $PadTool $PadFile $k.Pad --hold-ms 150
            "[harness] {0:N0}s pad {1} queued" -f $t, $k.Pad | Add-Content "$LogDir\harness.log"
            $k.Done = $true
        } elseif (-not $k.Done -and $t -ge $base + $k.At -and $p.MainWindowHandle -ne 0) {
            $ok = [Win]::Tap($p.MainWindowHandle, $k.Vk)
            "[harness] {0:N0}s key 0x{1:X2} {2}" -f $t, $k.Vk, $(if ($ok) { 'sent' } else { 'SKIPPED: game window not foreground' }) | Add-Content "$LogDir\harness.log"
            $k.Done = $true
        }
    }
    foreach ($s in @($CpuSamples | Where-Object { $_ -le $t })) {
        python -I (Join-Path $Root 'Tools\thread_cpu.py') $p.Id 5 30 | Set-Content ("$LogDir\threads_{0:D4}s.txt" -f $s)
        $CpuSamples = @($CpuSamples | Where-Object { $_ -ne $s })
    }
    if ($t -ge $next -and -not $p.HasExited -and $p.MainWindowHandle -ne 0) {
        $r = New-Object Win+RECT
        [void][Win]::GetWindowRect($p.MainWindowHandle, [ref]$r)
        $w = $r.R - $r.L; $h = $r.B - $r.T
        if ($w -gt 0 -and $h -gt 0) {
            $bmp = New-Object System.Drawing.Bitmap $w, $h
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $hdc = $g.GetHdc(); [void][Win]::PrintWindow($p.MainWindowHandle, $hdc, 2); $g.ReleaseHdc($hdc)
            $bmp.Save(("$LogDir\shot_{0:D4}s.png" -f [int]$t)); $g.Dispose(); $bmp.Dispose()
        }
        $next += $interval
    }
}
if ($p.HasExited) {
    $code = $p.ExitCode
    $result = 'exit {0} (0x{1:X8}) after {2:N0}s' -f $code, ($code -band 0xFFFFFFFF), ((Get-Date) - $start).TotalSeconds
} else {
    $result = "still running after ${Seconds}s (timeout, stopped by harness)"
    if ($env:ASTROHYBRID_BENCH_GRACEFUL_EXIT -eq '1') {
        # Let the renderer finish saving its caches after the measured window.
        if ($p.CloseMainWindow()) {
            [void]$p.WaitForExit(15000)
        }
        if ($p.HasExited) {
            $result = "still running after ${Seconds}s (window closed by harness, exit $($p.ExitCode))"
        }
    }
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force
    }
}
if (Test-Path "$Run\aps5-summary.jsonl") { Move-Item "$Run\aps5-summary.jsonl" "$LogDir\" -Force }
"$result; peak private memory $peakMB MiB" | Tee-Object "$LogDir\result.txt"
foreach ($k in $savedEnv.Keys) { [Environment]::SetEnvironmentVariable($k, $savedEnv[$k]) }
[Environment]::SetEnvironmentVariable('APS5_PAD_INPUT_FILE', $null)
"logs: $LogDir"
