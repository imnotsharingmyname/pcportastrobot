using System;
using System.Diagnostics;
using AstroHybrid.App.Avalonia.Services;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();

static ProcessStartInfo Python(string code) => new("python3")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    ArgumentList = { "-c", code }
};

var output = Backend.CollectDebuggerOutput(Python(
    "import sys; sys.stderr.write('x' * 262144); sys.stderr.flush(); print('stdout-complete')"),
    TimeSpan.FromSeconds(5));
if (!output.Contains("stdout-complete") || !output.Contains(new string('x', 262144)) ||
    !output.Contains("debugger exit 0"))
    throw new Exception("Concurrent stdout/stderr was not captured");

var stopwatch = Stopwatch.StartNew();
var timedOut = Backend.CollectDebuggerOutput(Python("import time; time.sleep(60)"),
    TimeSpan.FromMilliseconds(250));
if (!timedOut.Contains("debugger timed out") || stopwatch.Elapsed > TimeSpan.FromSeconds(5))
    throw new Exception("Debugger timeout was not enforced");
Console.WriteLine("Linux watchdog regression checks passed.");
