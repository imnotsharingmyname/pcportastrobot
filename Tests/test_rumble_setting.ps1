# Checks the vibration-strength setting (#36): the old preset index still maps to the same strength, the
# new percent is what reaches the game, and the Switch boost follows the strength instead of being flat.
# Also compile-checks the classic launcher. Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tests\test_rumble_setting.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path $root 'work\rumble-test'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

$roslyn = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path $roslyn)) {
    $found = Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio' -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*Roslyn*' } | Select-Object -First 1
    if ($null -eq $found) { throw 'Roslyn csc.exe not found' }
    $roslyn = $found.FullName
}

# The app's own model and helper, so the test cannot drift from what ships.
$release = Join-Path $root 'AstroHybrid.App\bin\Release'
$program = @'
using System;
using System.Text.Json;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

static class RumbleTest
{
    static int failures;

    static void Check(string name, int actual, int expected)
    {
        if (actual != expected)
        {
            failures++;
            Console.WriteLine("FAIL " + name + ": " + actual + " != " + expected);
        }
        else
        {
            Console.WriteLine("ok   " + name + " = " + actual);
        }
    }

    static int Main()
    {
        // A settings file written by 1.0.7 or older only has the preset index.
        Check("legacy 0 (Off) -> 0%", RumbleSetting.FromLegacyIndex(0), 0);
        Check("legacy 1 (Light) -> 40%", RumbleSetting.FromLegacyIndex(1), 40);
        Check("legacy 2 (Medium) -> 70%", RumbleSetting.FromLegacyIndex(2), 70);
        Check("legacy 3 (Full) -> 100%", RumbleSetting.FromLegacyIndex(3), 100);
        Check("legacy 4 (Strong) -> 150%", RumbleSetting.FromLegacyIndex(4), 150);
        Check("legacy 9 clamps to 150%", RumbleSetting.FromLegacyIndex(9), 150);
        Check("legacy -3 clamps to 0%", RumbleSetting.FromLegacyIndex(-3), 0);

        Check("percent wins over the index", RumbleSetting.EffectivePercent(5, 2), 5);
        Check("no percent falls back to the index", RumbleSetting.EffectivePercent(-1, 2), 70);
        Check("percent 250 clamps to 200", RumbleSetting.EffectivePercent(250, 3), 200);

        // The index kept for older launchers: closest preset, and never "Off" for a strength above zero.
        Check("5% -> index 1", RumbleSetting.ToLegacyIndex(5), 1);
        Check("0% -> index 0", RumbleSetting.ToLegacyIndex(0), 0);
        Check("20% -> index 1", RumbleSetting.ToLegacyIndex(20), 1);
        Check("70% -> index 2", RumbleSetting.ToLegacyIndex(70), 2);
        Check("200% -> index 4", RumbleSetting.ToLegacyIndex(200), 4);

        // The Switch boost follows the strength: 100% is the flat 96 the launcher passed before.
        Check("floor 100% boost -> 96", RumbleSetting.SwitchFloor(100, true), 96);
        Check("floor 70% boost -> 67", RumbleSetting.SwitchFloor(70, true), 67);
        Check("floor 5% boost -> 4", RumbleSetting.SwitchFloor(5, true), 4);
        Check("floor 0% boost -> 0", RumbleSetting.SwitchFloor(0, true), 0);
        Check("floor 100% no boost -> 0", RumbleSetting.SwitchFloor(100, false), 0);

        // Files on disk: the old shape, the new shape, and both.
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"rumble\":2}", options);
        Check("file rumble 2 -> 70%", legacy.EffectiveRumblePercent, 70);
        legacy.NormalizeRumble();
        Check("normalize writes percent 70", legacy.RumblePercent, 70);
        Check("normalize keeps index 2", legacy.Rumble, 2);
        var text = JsonSerializer.Serialize(legacy, options);
        var compact = text.Replace(" ", "");
        if (compact.IndexOf("\"rumblepct\":70", StringComparison.Ordinal) < 0)
        {
            failures++;
            Console.WriteLine("FAIL saved file has no rumblepct: " + text);
        }
        else if (compact.IndexOf("EffectiveRumblePercent", StringComparison.Ordinal) >= 0)
        {
            failures++;
            Console.WriteLine("FAIL computed strength is written to the file: " + text);
        }
        else
        {
            Console.WriteLine("ok   saved file has rumblepct 70 and no computed field");
        }

        var custom = JsonSerializer.Deserialize<AppSettings>("{\"rumble\":1,\"rumblepct\":5}", options);
        Check("file percent 5 wins over index 1", custom.EffectiveRumblePercent, 5);
        Check("file default (no keys) -> 70%", JsonSerializer.Deserialize<AppSettings>("{}", options).EffectiveRumblePercent, 70);

        Console.WriteLine(failures == 0 ? "all rumble checks passed" : failures + " rumble check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
'@
$source = Join-Path $work 'RumbleTest.cs'
Set-Content -Path $source -Value $program -Encoding ASCII
Get-ChildItem $release -Filter *.dll | Copy-Item -Destination $work
# The app's own binding redirects (System.Runtime.CompilerServices.Unsafe and friends) must apply here too.
Copy-Item (Join-Path $root 'AstroHybrid.App\obj\Release\AstroHybrid.exe.config') (Join-Path $work 'RumbleTest.exe.config')
$refs = Get-ChildItem $release -Filter *.dll | ForEach-Object { "/r:$($_.FullName)" }
& $roslyn /nologo /target:exe /langversion:latest /nullable:enable "/out:$work\RumbleTest.exe" `
    $refs `
    (Join-Path $root 'AstroHybrid.App\Services\RumbleSetting.cs') `
    (Join-Path $root 'AstroHybrid.App\Models\AppSettings.cs') $source
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
& "$work\RumbleTest.exe"
if ($LASTEXITCODE -ne 0) { throw "rumble checks failed ($LASTEXITCODE)" }

# The classic launcher is a single source file built with the Windows compiler; it must still compile.
$launcherOut = Join-Path $work 'AstroBotLauncher.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ "/out:$launcherOut" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $root 'Tools\hybrid\launcher\AstroBotLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw "classic launcher does not compile ($LASTEXITCODE)" }
Write-Output 'classic launcher compiles'
