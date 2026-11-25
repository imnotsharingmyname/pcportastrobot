using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class InstallationManager
    {
        private readonly string _rootDirectory;
        private readonly SettingsManager _settingsManager;
        private readonly GameValidator _gameValidator;
        private readonly BuildManager _buildManager;

        public event Action<string>? StatusChanged;
        public event Action<int>? ProgressChanged;

        public InstallationManager(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            _settingsManager = new SettingsManager(rootDirectory);
            _gameValidator = new GameValidator();
            _buildManager = new BuildManager(rootDirectory);

            _buildManager.ProgressChanged += msg => StatusChanged?.Invoke(msg);
            _buildManager.ProgressPercentChanged += pct => ProgressChanged?.Invoke(pct);
            _buildManager.ErrorOccurred += err => StatusChanged?.Invoke($"Error: {err}");
        }

        public async Task<InstallationState> GetInstallationStateAsync()
        {
            var settings = _settingsManager.Load();
            var installPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            var param = ReadInstalledParam();
            var manifest = settings.InstallationManifest;
            var state = new InstallationState
            {
                InstallPath = installPath,
                GameDumpPath = settings.GameDumpPath,
                GameVersion = settings.GameVersion ?? param.Version,
                GameTitleId = settings.GameTitleId ?? param.TitleId,
                InstalledAt = manifest?.InstalledAt,
                Manifest = manifest
            };

            // Check if installation exists and is valid
            state.IsInstalled = await VerifyInstallationAsync(state.InstallPath);
            state.IsValid = state.IsInstalled;

            if (state.IsInstalled)
            {
                // The walk below enumerates every file in the install (the game exes and the
                // hard-linked module copies) and can take seconds on a slow disk; keep it off the
                // caller's thread. GetInstallationStateAsync's other awaits complete synchronously,
                // so without this the whole method blocks the UI during startup.
                state.InstallSizeBytes = await Task.Run(() => GetDirectorySize(state.InstallPath));
                if (state.Manifest == null)
                {
                    var exePath = Path.Combine(installPath, "AstroBot.exe");
                    var installedAt = File.Exists(exePath) ? File.GetLastWriteTime(exePath) : (DateTime?)null;
                    state.Manifest = CreateManifest(_rootDirectory, state.GameDumpPath, state.GameVersion, state.GameTitleId, installedAt);
                    state.InstalledAt = state.Manifest.InstalledAt;
                }
            }
            else
            {
                state.ValidationError = "Installation not found or incomplete.";
            }

            return state;
        }

        public static InstallationManifest CreateManifest(
            string rootDirectory,
            string? gameDumpPath,
            string? gameVersion,
            string? gameTitleId,
            DateTime? installedAt = null)
        {
            var installPath = Path.Combine(rootDirectory, "Build", "RunHybrid");
            return new InstallationManifest
            {
                InstalledAt = installedAt ?? DateTime.Now,
                GameVersion = gameVersion ?? "",
                GameTitleId = gameTitleId ?? "",
                AnyPs5Version = ReadAnyPs5Version(rootDirectory),
                KytyVersion = ReadKytyVersion(rootDirectory),
                InstallationPath = installPath,
                GameDumpPath = gameDumpPath ?? "",
                Files = Array.Empty<string>()
            };
        }

        public static void SaveManifest(string rootDirectory, InstallationManifest manifest)
        {
            var settingsManager = new SettingsManager(rootDirectory);
            var settings = settingsManager.Load();
            settings.InstallationManifest = manifest;
            settingsManager.Save(settings);
        }

        public static string ReadAnyPs5Version(string rootDirectory)
        {
            var gitHash = TryReadGitShortHead(Path.Combine(rootDirectory, "Tools", "AnyPS5"));
            if (!string.IsNullOrEmpty(gitHash))
                return gitHash!;

            var kernelPrx = Path.Combine(rootDirectory, "Build", "RunHybrid", "libs", "libkernel.prx");
            if (File.Exists(kernelPrx))
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                var appVer = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "release";
                return $"{appVer} ({File.GetLastWriteTime(kernelPrx):yyyy-MM-dd})";
            }

            return "unknown";
        }

        public static string ReadKytyVersion(string rootDirectory)
        {
            var dllPath = Path.Combine(rootDirectory, "Build", "RunHybrid", "kyty_gpu.dll");
            if (File.Exists(dllPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(dllPath);
                    var text = Encoding.ASCII.GetString(bytes);
                    var match = Regex.Match(text, @"u59-[A-Za-z0-9._-]{8,80}");
                    if (match.Success)
                        return match.Value;

                    var info = FileVersionInfo.GetVersionInfo(dllPath);
                    if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                        return info.ProductVersion.Trim();
                }
                catch { }
            }

            foreach (var candidate in new[] { @"E:\AstroHybrid-Work\source\kyty", @"C:\kyty-src19" })
            {
                var gitHash = TryReadGitShortHead(candidate);
                if (!string.IsNullOrEmpty(gitHash))
                    return gitHash!;
            }

            return "unknown";
        }

        private static string? TryReadGitShortHead(string repoDir)
        {
            try
            {
                if (!Directory.Exists(Path.Combine(repoDir, ".git")))
                    return null;

                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = repoDir,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                    return null;

                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(3000);
                return proc.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> VerifyInstallationAsync(string installPath)
        {
            if (!Directory.Exists(installPath))
                return false;

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
                if (!File.Exists(Path.Combine(installPath, file)))
                    return false;
            }

            // app0 alone can be a broken/incomplete dump. Streamed data lives beside it, not inside it.
            var app0Path = Path.Combine(installPath, "app0");
            if (!Directory.Exists(app0Path))
                return false;

            return HasUsableDataBackend(installPath);
        }

        private static bool HasUsableDataBackend(string installPath)
        {
            var dataPath = Path.Combine(installPath, "app0", "data");
            try
            {
                if (Directory.Exists(dataPath))
                {
                    // Do not let an empty placeholder hide a missing archive. Check accessibility without
                    // walking/copying the dump; archive-only layouts do not need a data directory.
                    using var entries = Directory.EnumerateFileSystemEntries(dataPath).GetEnumerator();
                    if (entries.MoveNext()) return true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            var archivePath = Path.Combine(installPath, "app0.zar");
            try
            {
                using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return archive.Length > 0 && archive.ReadByte() != -1;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public async Task<bool> InstallAsync(string gameDumpPath, IProgress<SetupStep>? progress = null)
        {
            // Validate game dump first
            var gameInfo = await _gameValidator.ValidateGameDumpAsync(gameDumpPath);
            if (!gameInfo.IsValid)
            {
                throw new InvalidOperationException(gameInfo.ValidationError ?? "Invalid game dump");
            }

            // Save game dump path to settings
            var settings = _settingsManager.Load();
            settings.GameDumpPath = gameDumpPath;
            settings.GameVersion = gameInfo.Version;
            settings.GameTitleId = gameInfo.TitleId;
            _settingsManager.Save(settings);

            // Run the build
            var success = await _buildManager.BuildRunHybridAsync(gameDumpPath, progress);

            if (success)
            {
                settings.FirstRunComplete = true;
                _settingsManager.Save(settings);
                TryCreateDesktopLauncherShortcut(_rootDirectory);
            }

            return success;
        }

        public static void TryCreateDesktopLauncherShortcut(string rootDirectory)
        {
            try
            {
                var exePath = Path.Combine(rootDirectory, "AstroHybrid.exe");
                if (!File.Exists(exePath)) return;
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) return;
                var lnkPath = Path.Combine(desktop, "AstroHybrid.lnk");
                var script = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{lnkPath.Replace("'", "''")}');" +
                             $"$s.TargetPath='{exePath.Replace("'", "''")}';" +
                             $"$s.WorkingDirectory='{rootDirectory.Replace("'", "''")}';" +
                             $"$s.IconLocation='{exePath.Replace("'", "''")}';" +
                             "$s.Description='AstroHybrid - Astro Bot PC Port Launcher';$s.Save()";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);
            }
            catch
            {
                // Best-effort shortcut creation; never fail installation if Desktop is restricted.
            }
        }

        // Release installs rerun the setup on the saved dump (it rebuilds the game files and app0 only and
        // never touches the runtime that came with the download). A development install (no tools\relinker.exe)
        // is rebuilt with Tools\hybrid\make_run_hybrid.py by hand: the generic build would replace its files.
        public async Task<bool> RepairAsync(IProgress<SetupStep>? progress = null)
        {
            var settings = _settingsManager.Load();
            if (string.IsNullOrEmpty(settings.GameDumpPath))
            {
                throw new InvalidOperationException("No game dump folder or ZArchive is saved. Run the setup again from a fresh download, or set it with --setup.");
            }
            var setup = new GameSetup(_rootDirectory);
            if (!setup.IsReleaseLayout)
            {
                throw new InvalidOperationException("This is a development install: rebuild it with Tools\\hybrid\\make_run_hybrid.py.");
            }
            var report = new Progress<(int Percent, string Message)>(p =>
            {
                StatusChanged?.Invoke(p.Message);
                ProgressChanged?.Invoke(p.Percent);
            });
            await setup.RunAsync(settings.GameDumpPath!, report);
            return true;
        }

        public Task<bool> RebuildAsync(IProgress<SetupStep>? progress = null)
        {
            return RepairAsync(progress);
        }

        // The game always runs from Build\\RunHybrid next to the app (Run-AstroHybrid.bat); move the whole
        // AstroHybrid folder instead, then run Repair once so app0 is linked again.
        public Task ChangeInstallLocationAsync(string newPath, IProgress<SetupStep>? progress = null)
        {
            throw new InvalidOperationException("To move the game, close the app, move the whole AstroHybrid folder, then use Repair once.");
        }

        // Removes what the setup built from the dump (the game programs, app0, its sibling archive and the
        // feature file); the runtime stays so setup can run again. Links are removed, never followed into the dump.
        public async Task UninstallAsync(bool keepGameDump = true)
        {
            var settings = _settingsManager.Load();
            await Task.Run(() =>
            {
                var installPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
                GameSetup.DeleteTree(Path.Combine(_rootDirectory, "Build", "ZarSource"));
                if (!Directory.Exists(installPath)) return;
                GameSetup.DeleteTree(Path.Combine(installPath, "app0"));
                // Delete only the generated sibling entry, never a standalone source that is the same path.
                var runtimeArchive = Path.Combine(installPath, "app0.zar");
                if (!GameSource.SamePath(settings.GameDumpPath, runtimeArchive)) File.Delete(runtimeArchive);
                foreach (var file in Directory.GetFiles(installPath, "AstroBot*.exe").Concat(Directory.GetFiles(installPath, "AstroBot*.json")))
                {
                    File.Delete(file);
                }
                var features = Path.Combine(installPath, GameFeatures.FileName);
                if (File.Exists(features)) File.Delete(features);
            });

            settings.FirstRunComplete = false;
            settings.InstallPath = null;
            settings.InstallationManifest = null;
            if (!keepGameDump)
            {
                settings.GameDumpPath = null;
                settings.GameVersion = null;
                settings.GameTitleId = null;
            }
            _settingsManager.Save(settings);
        }

        public void OpenInstallFolder()
        {
            var settings = _settingsManager.Load();
            var path = settings.InstallPath ?? Path.Combine(_rootDirectory, "Build", "RunHybrid");
            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
        }

        public void OpenLogFolder()
        {
            var logPath = Path.Combine(_rootDirectory, "Logs");
            if (Directory.Exists(logPath))
            {
                Process.Start("explorer.exe", logPath);
            }
        }

        // Size of the files the install itself holds; links (app0's folders point into the dump) are not followed.
        private long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                var info = new DirectoryInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
                foreach (var file in info.GetFiles())
                {
                    try { size += file.Length; } catch { }
                }
                foreach (var dir in info.GetDirectories())
                {
                    size += GetDirectorySize(dir.FullName);
                }
            }
            catch { }
            return size;
        }

        private (string TitleId, string Version) ReadInstalledParam()
        {
            try
            {
                var runHybrid = Path.Combine(_rootDirectory, "Build", "RunHybrid");
                // sce_sys read through APS5_PATH_ALIASES (#135): its parent is the dump.
                var root = GameSetup.ReadApp0Aliases(runHybrid).TryGetValue("sce_sys", out var sceSys)
                    ? Path.GetDirectoryName(sceSys) ?? Path.Combine(runHybrid, "app0")
                    : Path.Combine(runHybrid, "app0");
                var (titleId, version) = GameSetup.ReadParam(root);
                return (string.IsNullOrEmpty(titleId) ? null! : titleId, string.IsNullOrEmpty(version) ? null! : version);
            }
            catch
            {
                return (null!, null!);
            }
        }

        private void CopyDirectory(string source, string target, IProgress<SetupStep>? progress)
        {
            var files = Directory.GetFiles(source, "*.*", SearchOption.AllDirectories);
            int total = files.Length;
            int current = 0;

            foreach (var file in files)
            {
                var relative = file.Substring(source.Length + 1);
                var destFile = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(file, destFile, true);

                current++;
                if (progress != null && total > 0)
                {
                    progress.Report(new SetupStep
                    {
                        Name = "Copying files",
                        ProgressPercent = (current * 100) / total
                    });
                }
            }
        }
    }
}
