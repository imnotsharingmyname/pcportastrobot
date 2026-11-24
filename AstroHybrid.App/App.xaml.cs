using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;
using AstroHybrid.App.Views;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App
{
    public partial class App : Application
    {
        private SettingsManager _settingsManager;
        private AppSettings _settings;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Optional software rendering fallback (#110, #137) for GPUs/overlays that crash WPF D3D9 on cold boot.
            if (Environment.GetEnvironmentVariable("ASTROHYBRID_SOFTWARE_RENDER") == "1" ||
                e.Args.Any(a => string.Equals(a, "--software-render", StringComparison.OrdinalIgnoreCase)))
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                LogDebug("WPF software rendering enabled.");
            }

            // Set up global exception handling
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                LogException(args.Exception);
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                LogException(ex);
                MessageBox.Show($"An unexpected error occurred:\n{ex?.Message}", "AstroHybrid Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                LogException(args.Exception);
                MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}", "AstroHybrid Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
                Shutdown(1);
            };

            // AstroHybrid.exe --setup "<dump folder>": the setup wizard's work without a window (exit code 0 on
            // success; progress and errors in Logs\setup.log).
            if (e.Args.Length == 2 && e.Args[0] == "--setup")
            {
                RunSetupWithoutWindow(e.Args[1]);
                return;
            }
            // AstroHybrid.exe --play: starts the game with the saved settings, without a window, and stays
            // until it exits (exit code = the game's), so RecompLauncher can track play time and back up saves.
            if (e.Args.Length == 1 && e.Args[0] == "--play")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                RunGameWithoutWindow();
                return;
            }
            // AstroHybrid.exe --remove-game: removes what the setup built (Installation page, Uninstall).
            if (e.Args.Length == 1 && e.Args[0] == "--remove-game")
            {
                RemoveGameWithoutWindow();
                return;
            }

            try
            {
                // Initialize settings
                InitializeSettings();
                LogDebug($"Settings loaded. FirstRunComplete: {_settings.FirstRunComplete}");

                // A game already set up in Build\RunHybrid (for example by the old launcher) is used as it
                // is: the setup wizard would rebuild it from other parts.
                if (!_settings.FirstRunComplete && ExistingInstallation())
                {
                    _settings.FirstRunComplete = true;
                    _settingsManager.Save(_settings);
                    LogDebug("Existing installation in Build\\RunHybrid found, setup skipped.");
                }

                // Show setup wizard or main window based on first run
                if (!_settings.FirstRunComplete)
                {
                    LogDebug("Showing SetupWizard...");
                    // The wizard is the only window for a while: closing it must not end the app.
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var setupWizard = new SetupWizard(GetRootDirectory());
                    var result = setupWizard.ShowDialog();
                    LogDebug($"SetupWizard closed with result: {result}");

                    // Reload settings after setup: the wizard saved them through its own manager, so read the
                    // file again instead of this manager's cached copy.
                    _settingsManager = new SettingsManager(GetRootDirectory());
                    _settings = _settingsManager.Load();
                    LogDebug($"Settings reloaded. FirstRunComplete: {_settings.FirstRunComplete}");

                    if (_settings.FirstRunComplete)
                    {
                        LogDebug("Showing MainWindow...");
                        ShowMainWindow();
                    }
                    else
                    {
                        LogDebug("Setup not complete, shutting down.");
                        Shutdown(0);
                    }
                }
                else
                {
                    LogDebug("First run complete, showing MainWindow...");
                    ShowMainWindow();
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                MessageBox.Show($"Startup error:\n{ex}", "AstroHybrid Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void LogDebug(string message)
        {
            try
            {
                var logPath = Path.Combine(GetRootDirectory(), "Logs", "astrohybrid-debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private async void RunSetupWithoutWindow(string dump)
        {
            var root = GetRootDirectory();
            var log = Path.Combine(root, "Logs", "setup.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                var progress = new Progress<(int Percent, string Message)>(p =>
                {
                    File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {p.Percent,3}% {p.Message}\r\n");
                    // "progress 0.40 Text" lines for RecompLauncher's setup screen; nowhere to go otherwise.
                    try
                    {
                        Console.Out.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "progress {0:0.00} {1}", Math.Max(0, Math.Min(100, p.Percent)) / 100.0, p.Message));
                        Console.Out.Flush();
                    }
                    catch { }
                });
                var features = await new GameSetup(root).RunAsync(dump, progress);
                InitializeSettings();
                _settings.FirstRunComplete = true;
                _settings.ApplyBestOnStart = true;
                _settings.GameDumpPath = dump;
                _settings.GameVersion = features.Version;
                _settings.GameTitleId = features.TitleId;
                _settingsManager.Save(_settings);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAILED: {ex}\r\n");
                Shutdown(2);
            }
        }

        private async void RunGameWithoutWindow()
        {
            try
            {
                InitializeSettings();
                // The saved settings, including choices RecompLauncher wrote, as they are: a later start of the
                // main window must not replace them with its first-run recommendations.
                _settings.ApplyBestOnStart = false;
                // Honor RecompLauncher's Big Picture mode (couch / Steam Deck / controller-only): start fullscreen.
                var rlMode = Environment.GetEnvironmentVariable("RECOMPLAUNCHER_MODE");
                if (string.Equals(rlMode, "bigpicture", StringComparison.OrdinalIgnoreCase))
                {
                    _settings.Fullscreen = true;
                }
                var hardware = new HardwareDetector().DetectHardware();
                hardware.SelectGpu(_settings.Gpu);
                _settings.Sanitize(hardware.SupportsDlss, hardware.SupportsFrameGeneration, hardware.SupportsReflex,
                                   hardware.MaxFrameGenMultiplier);
                int exitCode = await new LauncherService(GetRootDirectory()).LaunchGameAsync(_settings, hardware);
                Shutdown(exitCode);
            }
            catch (Exception ex)
            {
                LogException(ex);
                Shutdown(2);
            }
        }

        private async void RemoveGameWithoutWindow()
        {
            try
            {
                await new InstallationManager(GetRootDirectory()).UninstallAsync(true);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                LogException(ex);
                Shutdown(2);
            }
        }

        private static bool ExistingInstallation()
        {
            var runHybrid = Path.Combine(GetRootDirectory(), "Build", "RunHybrid");
            return File.Exists(Path.Combine(runHybrid, "AstroBot.exe")) && File.Exists(Path.Combine(runHybrid, "kyty_gpu.dll"));
        }

        private void InitializeSettings()
        {
            var rootDir = GetRootDirectory();
            Directory.CreateDirectory(Path.Combine(rootDir, "Build"));

            _settingsManager = new SettingsManager(rootDir);
            _settings = _settingsManager.Load();
        }

        private void ShowMainWindow()
        {
            LogDebug("ShowMainWindow: creating MainWindow...");
            var mainWindow = new MainWindow();
            var rootDir = GetRootDirectory();
            LogDebug("ShowMainWindow: creating MainWindowViewModel...");
            mainWindow.DataContext = new MainWindowViewModel(rootDir);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            LogDebug("ShowMainWindow: calling Show()...");
            mainWindow.Show();
            LogDebug("ShowMainWindow: visible.");
        }

        private void InitializeApplication()
        {
            // Ensure required directories exist
            var rootDir = GetRootDirectory();
            var dirs = new[]
            {
                Path.Combine(rootDir, "Build"),
                Path.Combine(rootDir, "Build", "RunHybrid"),
                Path.Combine(rootDir, "Logs"),
                Path.Combine(rootDir, "Build", "launcher")
            };

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(dir);
            }
        }

        // The game folder: the first folder from the exe upwards that holds Run-AstroHybrid.bat, so the
        // app works next to the old launcher and from its own build folder; else the exe's folder.
        private static string GetRootDirectory()
        {
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            for (var dir = new DirectoryInfo(exeDir); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Run-AstroHybrid.bat")))
                {
                    return dir.FullName;
                }
            }
            return exeDir;
        }

        private void LogException(Exception? ex)
        {
            try
            {
                var logPath = Path.Combine(GetRootDirectory(), "Logs", "astrohybrid-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
            // Also in the debug log, the file people attach to their reports (#137 had only that one).
            LogDebug($"ERROR (details also in astrohybrid-error.log): {ex}");
        }
    }
}