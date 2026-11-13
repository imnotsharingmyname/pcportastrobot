using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
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
            var state = new InstallationState
            {
                InstallPath = Path.Combine(_rootDirectory, "Build", "RunHybrid"),
                GameDumpPath = settings.GameDumpPath,
                GameVersion = settings.GameVersion ?? ReadInstalledParam().Version,
                GameTitleId = settings.GameTitleId ?? ReadInstalledParam().TitleId,
                InstalledAt = settings.InstallationManifest?.InstalledAt,
                Manifest = settings.InstallationManifest
            };

            // Check if installation exists and is valid
            state.IsInstalled = await VerifyInstallationAsync(state.InstallPath);
            state.IsValid = state.IsInstalled;

            if (state.IsInstalled)
            {
                state.InstallSizeBytes = GetDirectorySize(state.InstallPath);
            }
            else
            {
                state.ValidationError = "Installation not found or incomplete.";
            }

            return state;
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

            // Check app0 junction
            var app0Path = Path.Combine(installPath, "app0");
            if (!Directory.Exists(app0Path))
                return false;

            return true;
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
            }

            return success;
        }

        // Release installs rerun the setup on the saved dump (it rebuilds the game files and app0 only and
        // never touches the runtime that came with the download). A development install (no tools\relinker.exe)
        // is rebuilt with Tools\hybrid\make_run_hybrid.py by hand: the generic build would replace its files.
        public async Task<bool> RepairAsync(IProgress<SetupStep>? progress = null)
        {
            var settings = _settingsManager.Load();
            if (string.IsNullOrEmpty(settings.GameDumpPath))
            {
                throw new InvalidOperationException("No game dump folder is saved. Run the setup again from a fresh download, or set it with --setup.");
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

        // Removes what the setup built from the dump (the game programs, app0 and the feature file); the runtime
        // that came with the download stays, so the setup can run again. app0's links are removed as links,
        // never followed into the dump.
        public async Task UninstallAsync(bool keepGameDump = true)
        {
            await Task.Run(() =>
            {
                var installPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
                if (!Directory.Exists(installPath)) return;
                GameSetup.DeleteTree(Path.Combine(installPath, "app0"));
                foreach (var file in Directory.GetFiles(installPath, "AstroBot*.exe").Concat(Directory.GetFiles(installPath, "AstroBot*.json")))
                {
                    File.Delete(file);
                }
                var features = Path.Combine(installPath, GameFeatures.FileName);
                if (File.Exists(features)) File.Delete(features);
            });

            var settings = _settingsManager.Load();
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
                var (titleId, version) = GameSetup.ReadParam(Path.Combine(_rootDirectory, "Build", "RunHybrid", "app0"));
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