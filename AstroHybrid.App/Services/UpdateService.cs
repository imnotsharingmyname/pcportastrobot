using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class UpdateService
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/imnotsharingmyname/pcportastrobot/releases";
        private const string GitHubRepoUrl = "https://github.com/imnotsharingmyname/pcportastrobot";
        private const string UserAgent = "AstroHybrid-Launcher";
        private const long MaxUpdatePackageBytes = 2L * 1024 * 1024 * 1024;

        private readonly string _rootDirectory;
        private readonly SettingsManager _settingsManager;
        private readonly HttpClient _httpClient;
        private readonly string _tempDirectory;
        private readonly string _backupDirectory;

        public event Action<UpdateState>? StateChanged;
        public event Action<double, string>? DownloadProgressChanged;
        public event Action<string>? StatusChanged;

        public UpdateService(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            _settingsManager = new SettingsManager(rootDirectory);
            _tempDirectory = Path.Combine(rootDirectory, "Build", "launcher", "temp");
            _backupDirectory = Path.Combine(rootDirectory, "Build", "launcher", "backup");

            Directory.CreateDirectory(_tempDirectory);
            Directory.CreateDirectory(_backupDirectory);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        public string GetCurrentVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }

        public async Task<UpdateState> CheckForUpdatesAsync(bool forceCheck = false)
        {
            var state = new UpdateState
            {
                CurrentVersion = GetCurrentVersion(),
                IsChecking = true
            };

            StateChanged?.Invoke(state);
            StatusChanged?.Invoke("Checking for updates...");

            try
            {
                // Load settings to check ignored version and last check time
                var settings = _settingsManager.Load();

                var releases = await FetchReleasesAsync();

                // Find the latest non-prerelease release with a valid launcher asset
                UpdateInfo? latestUpdate = null;
                UpdateInfo? ignoredUpdate = null;

                foreach (var release in releases)
                {
                    if (release.Prerelease) continue;

                    var asset = FindLauncherAsset(release);
                    if (asset != null)
                    {
                        var version = release.TagName.TrimStart('v');
                        var updateInfo = new UpdateInfo
                        {
                            Version = version,
                            ReleaseName = release.Name ?? $"Version {version}",
                            ReleaseNotes = release.Body ?? "",
                            PublishedAt = release.PublishedAt,
                            DownloadUrl = asset.BrowserDownloadUrl,
                            FileName = asset.Name,
                            FileSize = asset.Size,
                            IsPrerelease = release.Prerelease
                        };

                        // Verify this version is newer than current
                        if (updateInfo.IsNewerThan(state.CurrentVersion))
                        {
                            // Check if user ignored this version
                            if (settings.IgnoredUpdateVersion != version)
                            {
                                latestUpdate = updateInfo;
                                break;
                            }
                            else
                            {
                                // Update was ignored, but still track it
                                state.IgnoredVersion = version;
                                ignoredUpdate = updateInfo;
                            }
                        }
                    }
                }

                state.UpdateAvailable = latestUpdate != null;
                state.LatestUpdate = latestUpdate;
                state.IgnoredUpdate = ignoredUpdate;
                state.LastCheckTime = DateTime.Now;
                state.IsChecking = false;

                // Save last check time
                settings.LastUpdateCheck = state.LastCheckTime;
                _settingsManager.Save(settings);

                if (latestUpdate != null)
                {
                    StatusChanged?.Invoke($"Update available: {latestUpdate.Version}");
                }
                else
                {
                    StatusChanged?.Invoke("No updates available");
                }
            }
            catch (Exception ex)
            {
                state.IsChecking = false;
                state.ErrorMessage = ex.Message;
                StatusChanged?.Invoke($"Update check failed: {ex.Message}");
            }

            StateChanged?.Invoke(state);
            return state;
        }

        private async Task<List<GitHubRelease>> FetchReleasesAsync()
        {
            var json = await _httpClient.GetStringAsync(GitHubApiUrl);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return releases ?? new List<GitHubRelease>();
        }

        private GitHubAsset? FindLauncherAsset(GitHubRelease release)
        {
            // Look for the launcher update package (zip file containing the launcher)
            // Expected naming: AstroBotPC-AstroHybrid-{version}.zip or similar
            foreach (var asset in release.Assets)
            {
                var name = asset.Name.ToLowerInvariant();
                if (name.Contains("astrohybrid") && name.EndsWith(".zip") && !name.Contains("linux"))
                {
                    // The Windows package carries the whole runtime (~300 MB in 1.2.28) and grows with it;
                    // the cap only rejects something that cannot be a launcher package.
                    if (asset.Size > 0 && asset.Size < MaxUpdatePackageBytes)
                    {
                        return asset;
                    }
                }
            }
            return null;
        }

        public async Task<bool> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<(double Progress, string Status)>? progress = null)
        {
            var state = new UpdateState
            {
                UpdateAvailable = true,
                IsDownloading = true,
                DownloadProgress = 0,
                DownloadStatus = $"Updating to {updateInfo.Version}... starting download",
                LatestUpdate = updateInfo,
                CurrentVersion = GetCurrentVersion()
            };

            StateChanged?.Invoke(state);

            try
            {
                var tempFile = Path.Combine(_tempDirectory, updateInfo.FileName);

                // Clean up any existing temp file
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var buffer = new byte[8192];
                var totalRead = 0L;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(tempFile);

                int bytesRead;
                double reported = -1;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progressPercent = (double)totalRead / totalBytes;
                        // About 200 UI updates per download instead of one per 8 KB block.
                        if (progressPercent - reported < 0.005 && totalRead < totalBytes) continue;
                        reported = progressPercent;
                        state.DownloadProgress = progressPercent;
                        state.DownloadStatus = $"Updating to {updateInfo.Version}... {progressPercent:P0} ({FormatBytes(totalRead)} / {FormatBytes(totalBytes)})";
                        StateChanged?.Invoke(state);
                        progress?.Report((progressPercent, state.DownloadStatus));
                    }
                }

                state.DownloadProgress = 1.0;
                state.DownloadStatus = $"Updating to {updateInfo.Version}... verifying download";
                StateChanged?.Invoke(state);

                // Verify file integrity if SHA256 is provided
                if (!string.IsNullOrEmpty(updateInfo.Sha256))
                {
                    var computedHash = await ComputeSha256Async(tempFile);
                    if (!computedHash.Equals(updateInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("File integrity check failed: SHA256 mismatch");
                    }
                }

                state.DownloadStatus = $"Updating to {updateInfo.Version}... download verified";
                StateChanged?.Invoke(state);
                return true;
            }
            catch (Exception ex)
            {
                state.IsDownloading = false;
                state.DownloadStatus = $"Update failed while downloading: {ex.Message}. Press Update to try again.";
                state.ErrorMessage = ex.Message;
                StateChanged?.Invoke(state);
                return false;
            }
        }

        public async Task<bool> InstallUpdateAsync(UpdateInfo updateInfo)
        {
            var state = new UpdateState
            {
                UpdateAvailable = true,
                IsInstalling = true,
                DownloadProgress = 1.0,
                DownloadStatus = $"Updating to {updateInfo.Version}... preparing files",
                LatestUpdate = updateInfo,
                CurrentVersion = GetCurrentVersion()
            };
            StateChanged?.Invoke(state);

            try
            {
                var tempFile = Path.Combine(_tempDirectory, updateInfo.FileName);
                if (!File.Exists(tempFile))
                {
                    throw new FileNotFoundException("Downloaded update file not found", tempFile);
                }

                // Create backup of current launcher
                await BackupCurrentLauncherAsync();

                state.DownloadStatus = $"Updating to {updateInfo.Version}... unpacking";
                StateChanged?.Invoke(state);

                // Extract to temp directory. The running executable cannot reliably replace itself,
                // so a small updater process will copy these files after this process exits.
                var extractDir = Path.Combine(_tempDirectory, "extract");
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, true);
                }
                Directory.CreateDirectory(extractDir);

                await Task.Run(() => ZipFile.ExtractToDirectory(tempFile, extractDir));

                // Find the launcher executable in the extracted files
                var launcherExe = FindLauncherExe(extractDir);
                if (launcherExe == null)
                {
                    throw new InvalidOperationException("Launcher executable not found in update package");
                }

                // The files are copied in once the launcher has closed (it can't replace itself while
                // running): the player presses "Restart launcher" (RestartToApplyUpdate).
                _pendingUpdateSource = PackageRoot(extractDir);
                state.IsInstalling = false;
                state.IsReadyToRestart = true;
                state.DownloadStatus = $"Update {updateInfo.Version} is ready. Restart the launcher to finish updating.";
                StateChanged?.Invoke(state);

                return true;
            }
            catch (Exception ex)
            {
                state.IsInstalling = false;
                state.DownloadStatus = $"Update failed while preparing: {ex.Message}. Press Update to try again.";
                state.ErrorMessage = ex.Message;
                StateChanged?.Invoke(state);

                // Try to restore from backup
                await RestoreFromBackupAsync();
                return false;
            }
        }

        private async Task BackupCurrentLauncherAsync()
        {
            var backupDir = Path.Combine(_backupDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backupDir);

            var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (appDir != null)
            {
                foreach (var file in Directory.GetFiles(appDir, "*.exe"))
                {
                    File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), true);
                }
                foreach (var file in Directory.GetFiles(appDir, "*.dll"))
                {
                    File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), true);
                }
                foreach (var file in Directory.GetFiles(appDir, "*.config"))
                {
                    File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), true);
                }
            }

            await Task.CompletedTask;
        }

        private async Task RestoreFromBackupAsync()
        {
            try
            {
                var backups = Directory.GetDirectories(_backupDirectory);
                if (backups.Length == 0) return;

                var latestBackup = backups.OrderByDescending(d => d).First();
                var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                if (appDir != null)
                {
                    foreach (var file in Directory.GetFiles(latestBackup, "*.exe"))
                    {
                        File.Copy(file, Path.Combine(appDir, Path.GetFileName(file)), true);
                    }
                    foreach (var file in Directory.GetFiles(latestBackup, "*.dll"))
                    {
                        File.Copy(file, Path.Combine(appDir, Path.GetFileName(file)), true);
                    }
                    foreach (var file in Directory.GetFiles(latestBackup, "*.config"))
                    {
                        File.Copy(file, Path.Combine(appDir, Path.GetFileName(file)), true);
                    }
                }
            }
            catch { }

            await Task.CompletedTask;
        }

        // Release zips up to 1.2.2 hold everything inside one folder (AstroBotPC-AstroHybrid-x.y.z\...).
        // Copying that layout as-is put the update in a subfolder of the install and left the running
        // launcher unchanged, so the update was offered again forever. The package root is that folder
        // when the archive is a single folder with no loose files, otherwise the extract folder itself.
        public static string PackageRoot(string extractDir)
        {
            var directories = Directory.GetDirectories(extractDir);
            if (directories.Length == 1 && Directory.GetFiles(extractDir).Length == 0)
                return directories[0];
            return extractDir;
        }

        private string? FindLauncherExe(string extractDir)
        {
            // Look for AstroHybrid.exe in the extracted package
            var files = Directory.GetFiles(extractDir, "AstroHybrid.exe", SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];

            // Also check for any .exe in root
            files = Directory.GetFiles(extractDir, "*.exe", SearchOption.TopDirectoryOnly);
            if (files.Length > 0) return files[0];

            return null;
        }

        private async Task CopyUpdateFilesAsync(string extractDir)
        {
            var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (appDir == null) throw new InvalidOperationException("Cannot determine application directory");

            // Copy all files from extract directory to app directory
            foreach (var file in Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(extractDir.Length + 1);
                var destFile = Path.Combine(appDir, relativePath);
                var destDir = Path.GetDirectoryName(destFile);

                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, destFile, true);
            }

            await Task.CompletedTask;
        }

        private string? _pendingUpdateSource;

        public bool HasPendingUpdate => _pendingUpdateSource != null && Directory.Exists(_pendingUpdateSource);

        // Closes the launcher; a small script copies the prepared update in place and starts the new
        // launcher (apply-update.ps1).
        public async Task RestartToApplyUpdateAsync()
        {
            if (!HasPendingUpdate)
                throw new InvalidOperationException("No prepared update to install");
            await ScheduleUpdateInstallAsync(_pendingUpdateSource!);
        }

        private async Task ScheduleUpdateInstallAsync(string extractDir)
        {
            var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (appDir == null) throw new InvalidOperationException("Cannot determine application directory");

            var scriptPath = Path.Combine(_tempDirectory, "apply-update.ps1");
            var currentPid = Process.GetCurrentProcess().Id;
            var escapedSource = EscapePowerShellString(extractDir);
            var escapedTarget = EscapePowerShellString(appDir);
            var escapedExe = EscapePowerShellString(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var escapedTemp = EscapePowerShellString(_tempDirectory);
            var script = $@"
$ErrorActionPreference = 'Stop'
$source = (Get-Item -LiteralPath '{escapedSource}').FullName
$target = '{escapedTarget}'
$launcher = '{escapedExe}'
$temp = '{escapedTemp}'
$log = Join-Path $temp 'apply-update.log'
while (Get-Process -Id {currentPid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 250 }}
$failed = @()
Get-ChildItem -LiteralPath $source -Recurse -File | ForEach-Object {{
    $relative = $_.FullName.Substring($source.Length).TrimStart('\')
    $destination = Join-Path $target $relative
    $directory = Split-Path -Parent $destination
    $copied = $false
    for ($try = 0; $try -lt 20 -and -not $copied; $try++) {{
        try {{
            if ($directory) {{ New-Item -ItemType Directory -Force -Path $directory | Out-Null }}
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
            $copied = $true
        }} catch {{ Start-Sleep -Milliseconds 500 }}
    }}
    if (-not $copied) {{ $failed += $relative }}
}}
if ($failed.Count -eq 0) {{
    Remove-Item -LiteralPath $source -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temp '*.zip') -Force -ErrorAction SilentlyContinue
    ""$(Get-Date -Format s) update applied"" | Out-File -FilePath $log -Append
}} else {{
    # Keep the prepared files so the next Update finishes the job; never leave the player without a launcher.
    ""$(Get-Date -Format s) could not replace (in use?): $($failed -join ', ')"" | Out-File -FilePath $log -Append
}}
Start-Process -FilePath $launcher
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
";
            File.WriteAllText(scriptPath, script, Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appDir
            };
            Process.Start(psi);
            await Task.Delay(150);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                System.Windows.Application.Current.Shutdown()));
        }

        private static string EscapePowerShellString(string value) => value.Replace("'", "''");

        private async Task<string> ComputeSha256Async(string filePath)
        {
            return await Task.Run(() =>
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            });
        }

        public void IgnoreVersion(string version)
        {
            var settings = _settingsManager.Load();
            settings.IgnoredUpdateVersion = version;
            _settingsManager.Save(settings);
        }

        public void ClearIgnoredVersion()
        {
            var settings = _settingsManager.Load();
            settings.IgnoredUpdateVersion = null;
            _settingsManager.Save(settings);
        }

        public async Task LaunchUpdaterAndExitAsync(string updateExePath)
        {
            // Create a small batch file to run the updater after this process exits
            var batPath = Path.Combine(_tempDirectory, "run_updater.bat");
            var appExe = System.Reflection.Assembly.GetExecutingAssembly().Location;

            var batContent = $@"
@echo off
timeout /t 2 /nobreak >nul
start "" "" ""{updateExePath}""
";
            File.WriteAllText(batPath, batContent);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };

            Process.Start(psi);

            // Exit current application
            System.Windows.Application.Current.Shutdown();
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:F1} {suffixes[i]}";
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
