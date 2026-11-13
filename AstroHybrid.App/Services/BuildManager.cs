using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class BuildManager
    {
        private readonly string _rootDirectory;
        private readonly string _pythonPath;

        public event Action<string>? ProgressChanged;
        public event Action<int>? ProgressPercentChanged;
        public event Action<string>? ErrorOccurred;

        public BuildManager(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            _pythonPath = FindPython();
        }

        private string FindPython()
        {
            // Try common Python locations
            var paths = new[]
            {
                "python",
                "python3",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Python39\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files\Python310\python.exe",
            };

            foreach (var path in paths)
            {
                try
                {
                    var psi = new ProcessStartInfo(path, "--version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit(2000);
                        if (proc.ExitCode == 0)
                            return path;
                    }
                }
                catch { }
            }

            return "python"; // Default, will fail with clear error if not found
        }

        public async Task<bool> BuildRunHybridAsync(string gameDumpPath, IProgress<SetupStep>? progress = null)
        {
            try
            {
                // Step 1: Validate prerequisites
                progress?.Report(new SetupStep { Name = "Checking prerequisites", Description = "Verifying build dependencies...", ProgressPercent = 5 });
                ReportProgress("Checking prerequisites...", 5);

                if (!await CheckPrerequisitesAsync())
                {
                    ErrorOccurred?.Invoke("Missing prerequisites. Please install Python and ensure AnyPS5 is built.");
                    return false;
                }

                // Step 2: Run make_run_hybrid.py
                progress?.Report(new SetupStep { Name = "Building RunHybrid", Description = "Creating hybrid runtime with Kyty GPU stack...", ProgressPercent = 20 });
                ReportProgress("Building RunHybrid...", 20);

                var scriptPath = Path.Combine(_rootDirectory, "Tools", "hybrid", "make_run_hybrid.py");
                if (!File.Exists(scriptPath))
                {
                    ErrorOccurred?.Invoke($"Build script not found: {scriptPath}");
                    return false;
                }

                var envVars = new Dictionary<string, string>
                {
                    ["ASTROHYBRID_KYTY_GPU"] = "pgo"
                };

                var success = await RunProcessAsync(_pythonPath, $"-I \"{scriptPath}\"", _rootDirectory, envVars, progress);
                if (!success)
                {
                    ErrorOccurred?.Invoke("Failed to build RunHybrid. Check the logs for details.");
                    return false;
                }

                // Step 3: Verify output
                progress?.Report(new SetupStep { Name = "Verifying installation", Description = "Checking generated files...", ProgressPercent = 85 });
                ReportProgress("Verifying installation...", 85);

                if (!VerifyRunHybridOutput())
                {
                    ErrorOccurred?.Invoke("RunHybrid build completed but required files are missing.");
                    return false;
                }

                // Step 4: Generate manifest
                progress?.Report(new SetupStep { Name = "Generating manifest", Description = "Creating installation manifest...", ProgressPercent = 95 });
                ReportProgress("Generating manifest...", 95);

                await GenerateInstallationManifestAsync(gameDumpPath);

                progress?.Report(new SetupStep { Name = "Complete", Description = "Installation successful!", ProgressPercent = 100, IsComplete = true });
                ReportProgress("Installation complete!", 100);

                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Build error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CheckPrerequisitesAsync()
        {
            // Check Python
            try
            {
                var psi = new ProcessStartInfo(_pythonPath, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                await WaitForExitAsync(proc, 2000);
                if (proc.ExitCode != 0) return false;
            }
            catch
            {
                return false;
            }

            // Check AnyPS5 build exists
            var anyps5Libs = Path.Combine(_rootDirectory, "Build", "anyps5", "core", "libs", "libs");
            var requiredLibs = new[] { "libc.prx", "libkernel.prx", "libScePad.prx", "libAstroHybridHost.prx" };
            foreach (var lib in requiredLibs)
            {
                if (!File.Exists(Path.Combine(anyps5Libs, lib)))
                    return false;
            }

            // Check Kyty GPU DLL
            var kytyPaths = new[]
            {
                @"E:\AstroHybrid-Work\build\kyty-gpu-pgo\kyty_gpu.dll",
                @"E:\AstroHybrid-Work\build\kyty-gpu\kyty_gpu.dll"
            };

            bool kytyFound = false;
            foreach (var path in kytyPaths)
            {
                if (File.Exists(path))
                {
                    kytyFound = true;
                    break;
                }
            }

            if (!kytyFound)
                return false;

            // Check NIDS file
            var nidsPath = @"E:\AstroHybrid-Work\source\kyty\src\hybrid\kytyGpuNids.inc";
            if (!File.Exists(nidsPath))
                return false;

            return true;
        }

        private async Task<bool> RunProcessAsync(string fileName, string arguments, string workingDirectory,
            Dictionary<string, string>? envVars, IProgress<SetupStep>? progress)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (envVars != null)
            {
                foreach (var kvp in envVars)
                {
                    psi.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var outputLines = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputLines.Add(e.Data);
                    ProgressChanged?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputLines.Add("[ERR] " + e.Data);
                    ProgressChanged?.Invoke("[ERR] " + e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Monitor progress
            while (!process.HasExited)
            {
                await Task.Delay(500);
                // Could parse output for progress indicators
            }

            await WaitForExitAsync(process);

            return process.ExitCode == 0;
        }

        private bool VerifyRunHybridOutput()
        {
            var runHybridPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            var requiredFiles = new[]
            {
                "AstroBot.exe",
                "kyty_gpu.dll",
                "astrohybrid_host.dll",
                "u59-preset.cmd",
                "libs\\libc.prx",
                "libs\\libkernel.prx",
                "libs\\libScePad.prx",
                "libs\\libSceAgc.prx",
                "libs\\libSceAgcDriver.prx",
                "libs\\libSceVideoOut.prx"
            };

            foreach (var file in requiredFiles)
            {
                if (!File.Exists(Path.Combine(runHybridPath, file)))
                {
                    ProgressChanged?.Invoke($"Missing required file: {file}");
                    return false;
                }
            }

            // Check app0 junction exists
            var app0Path = Path.Combine(runHybridPath, "app0");
            if (!Directory.Exists(app0Path))
            {
                ProgressChanged?.Invoke("app0 junction not found");
                return false;
            }

            return true;
        }

        private async Task GenerateInstallationManifestAsync(string gameDumpPath)
        {
            var manifest = new InstallationManifest
            {
                InstalledAt = DateTime.Now,
                GameVersion = "01.019.000",
                GameTitleId = "PPSA21567",
                AnyPs5Version = GetAnyPs5Version(),
                KytyVersion = GetKytyVersion(),
                InstallationPath = Path.Combine(_rootDirectory, "Build", "RunHybrid"),
                GameDumpPath = gameDumpPath,
                Files = GetInstalledFiles()
            };

            var settingsManager = new SettingsManager(_rootDirectory);
            var settings = settingsManager.Load();
            settings.InstallationManifest = manifest;
            settingsManager.Save(settings);
        }

        private string GetAnyPs5Version()
        {
            try
            {
                var gitDir = Path.Combine(_rootDirectory, "Tools", "AnyPS5");
                if (Directory.Exists(gitDir))
                {
                    var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                    {
                        WorkingDirectory = gitDir,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = proc.StandardOutput.ReadToEnd();
                        WaitForExitAsync(proc).Wait();
                        return output.Trim();
                    }
                }
            }
            catch { }
            return "unknown";
        }

        private string GetKytyVersion()
        {
            try
            {
                var gitDir = @"E:\AstroHybrid-Work\source\kyty";
                if (Directory.Exists(gitDir))
                {
                    var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                    {
                        WorkingDirectory = gitDir,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = proc.StandardOutput.ReadToEnd();
                        WaitForExitAsync(proc).Wait();
                        return output.Trim();
                    }
                }
            }
            catch { }
            return "unknown";
        }

        private string[] GetInstalledFiles()
        {
            var runHybridPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            var files = new List<string>();

            if (Directory.Exists(runHybridPath))
            {
                foreach (var file in Directory.GetFiles(runHybridPath, "*.*", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(runHybridPath.Length + 1);
                    files.Add(relative);
                }
            }

            return files.ToArray();
        }

        private void ReportProgress(string message, int percent)
        {
            ProgressChanged?.Invoke(message);
            ProgressPercentChanged?.Invoke(percent);
        }

        private static Task WaitForExitAsync(Process process, int timeoutMs = -1)
        {
            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) => tcs.TrySetResult(true);

            if (timeoutMs > 0)
            {
                var completed = process.WaitForExit(timeoutMs);
                if (!completed)
                {
                    tcs.TrySetResult(false);
                }
            }

            if (process.HasExited)
            {
                tcs.TrySetResult(true);
            }

            return tcs.Task;
        }
    }
}