using System;
using System.Diagnostics;
using System.IO;
using AstroHybrid.App.Avalonia.Services;

if (!OperatingSystem.IsLinux())
    throw new PlatformNotSupportedException("Run this regression on Linux.");

if (UpdateService.LauncherVersion != "1.2.32" || UpdateService.IsNewer("1.2.32", UpdateService.LauncherVersion) ||
    UpdateService.IsNewer("1.2.31", UpdateService.LauncherVersion) || !UpdateService.IsNewer("1.2.33", UpdateService.LauncherVersion))
    throw new Exception("Updater did not use the compiled package version");

var dir = Directory.CreateTempSubdirectory("astrohybrid-updater-test-").FullName;
try
{
    var current = Path.Combine(dir, "AstroHybrid");
    var incoming = Path.Combine(dir, "incoming");
    File.Copy("/bin/sleep", current);
    File.Copy("/bin/true", incoming);
    var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    File.SetUnixFileMode(current, mode);
    using var running = Process.Start(new ProcessStartInfo(current, "60") { UseShellExecute = false })
        ?? throw new Exception("Failed to launch test executable");
    try
    {
        UpdateService.ReplaceLauncher(incoming, current);
        if (running.HasExited) throw new Exception("Original process stopped");
        if (!File.ReadAllBytes(current).AsSpan().SequenceEqual(File.ReadAllBytes(incoming)))
            throw new Exception("New executable not installed");
        if (!File.ReadAllBytes(current + ".bak").AsSpan().SequenceEqual(File.ReadAllBytes("/bin/sleep")))
            throw new Exception("Backup incorrect");
        if (File.GetUnixFileMode(current) != mode) throw new Exception("Executable mode not preserved");
        using var next = Process.Start(new ProcessStartInfo(current) { UseShellExecute = false })
            ?? throw new Exception("Failed to launch replacement");
        if (!next.WaitForExit(5000) || next.ExitCode != 0) throw new Exception("Replacement failed to run");
        try
        {
            UpdateService.ReplaceLauncher(Path.Combine(dir, "missing"), current);
            throw new Exception("Missing input unexpectedly accepted");
        }
        catch (FileNotFoundException) { }
        if (!File.ReadAllBytes(current).AsSpan().SequenceEqual(File.ReadAllBytes(incoming)))
            throw new Exception("Failed update modified executable");
    }
    finally
    {
        if (!running.HasExited) running.Kill();
        running.WaitForExit();
    }
    Console.WriteLine("Linux updater replacement regression passed.");
}
finally { Directory.Delete(dir, true); }
