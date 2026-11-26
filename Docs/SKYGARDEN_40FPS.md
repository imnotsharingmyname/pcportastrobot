# Sky Garden: 40 native FPS experiment

The recorded heavy foliage scene uses about 31 ms of GPU work per frame;
40 native FPS requires 25 ms total. The previous CPU batch barely changed
mean FPS. The launcher defaults to 30 FPS and does not enable the internal
1080p cap. Output window resolution is not the same as internal rendering
resolution. The game's DRS may select 1080p already, or a higher level.

This experiment uses the EXISTING fixed_1080p patch from
Source/game-patches/fixed_1080p.toml and GameSetup.SetFixed1080pCap.
It is an optional rendering-quality trade-off, not a new shader or CPU
optimization. It may save GPU time if the game normally selects a higher
internal resolution; if the source is already capped or the DRS already
chooses level 0, expect no gain. No FPS target has been measured here.

## Prepare

Close the game and launcher. From the project root (PowerShell):

```powershell
python Tests\skygarden_resolution_ab.py prepare Build\RunHybrid\AstroBot-NonRT-SoftAssert.exe
```

Select your actual known-working executable if this name differs. This
creates two private copies, not hard links; it refuses unknown/ambiguous
signatures and existing destinations. It does not modify the original
or game dump. Both variants have the same original executable contents
except the four resolution-controller bytes. The JSON reports whether
the input was already capped. Allow disk space for two executable copies.

## Measure

Use the existing hybrid_skygarden.ps1 harness (requires the normal
Build/RunHybrid runtime and app0 junction layout used by that harness).
Keep the same GPU DLL, preset, driver, power plan, scene, window size
and lighting settings. Disable any external frame generator or FPS limiter.
Warm up both variant names in separate runs (driver cache may be per exe).
Then run one ABBA sequence. Make sure each run reaches the same Sky Garden
landing view and remains there for the final minute; reject crashes,
loading, scene drift, pipeline skips or visually broken frames. No changes
to launch defaults are made by this experiment.

```powershell
New-Item -ItemType Directory -Force Logs | Out-Null
$stamp = Get-Date -Format yyyyMMdd-HHmmss-fff
$common = @{
    ASTROHYBRID_FPS_LIMIT='unlimited'
    ASTROHYBRID_DLSSG='0'; ASTROHYBRID_FSRFG='0'
    ASTROHYBRID_DLSS='0'; ASTROHYBRID_FSR='0'; ASTROHYBRID_XESS='0'
    KYTY_GPU_WIDTH='1920'; KYTY_GPU_HEIGHT='1080'
    KYTY_DISPATCH_OVERLAP='0'
    KYTY_HANG_WATCHDOG='0'
    ASTROHYBRID_BENCH_GRACEFUL_EXIT='1'
}
$names = @('AstroBot-SkyGarden-Control.exe','AstroBot-SkyGarden-1080p.exe',
           'AstroBot-SkyGarden-1080p.exe','AstroBot-SkyGarden-Control.exe')
$csvs = @()
for ($i = 0; $i -lt 4; $i++) {
    $e = $common.Clone()
    $csv = Join-Path (Resolve-Path Logs).Path "sky40-$stamp-$i.csv"
    $e['KYTY_FRAME_TIMES_LOG'] = $csv
    & Tests\hybrid_skygarden.ps1 -Seconds 170 -Exe $names[$i] -Env $e -Shots 3
    if (!(Test-Path -LiteralPath $csv)) { throw "Missing native frame log: $csv" }
    $csvs += $csv
}
python Tests\skygarden_resolution_ab.py gate @csvs
```

The gate scores the last 60 seconds of four distinct native frame-time CSVs
using the same microsecond format as Tests/hybrid_cp_report.py. Both
candidate runs must average at least 40 FPS; a failure returns exit code 2.
It reports 1% lows and the mean A/B gain as well. The numeric gate alone is
not proof of gameplay correctness or an improvement: manually confirm
identical scene/camera, native rather than generated frames, warm caches,
and that the baseline was not already internally capped. Review screenshots,
water, cloth, gameplay speed, VRAM and GPU busy time. Reject worse pacing,
crashes, or missing rendering. For a major optimization require a repeatable
15% gain as well as acceptable quality; if the baseline already reaches
40 FPS, the 40 FPS result alone does not establish a gain.

Delete only the two named test copies to undo. The original runtime is
untouched. No engine build or new game files are included.

## Tests

```text
python -m unittest discover -s Tests -p test_skygarden_resolution_ab.py -v
```

Synthetic fixtures cover original/capped inputs, unchanged source,
ambiguous signatures, existing outputs and the 40 FPS numeric gate.
They do not measure gameplay FPS.
