using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

namespace AstroHybrid.App.ViewModels
{
    public enum LauncherPage
    {
        Home,
        GraphicsSettings,
        ControllerSettings,
        AdvancedSettings,
        Installation
    }

    public class MainWindowViewModel : ViewModelBase
    {
        private readonly SettingsManager _settingsManager;
        private readonly InstallationManager _installationManager;
        private readonly HardwareDetector _hardwareDetector;
        private readonly ControllerDetector _controllerDetector;
        private readonly LauncherService _launcherService;
        private readonly string _rootDirectory;

        public SettingsManager SettingsManager => _settingsManager;

        private LauncherPage _currentPage = LauncherPage.Home;
        private AppSettings _settings = new();
        private InstallationState? _installationState;
        private HardwareInfo? _hardwareInfo;
        private ObservableCollection<ControllerInfo> _controllers = new();
        private ControllerInfo? _selectedController;
        private bool _isLaunching;
        private bool _isLoading;
        private string _statusMessage = "";

        public static MainWindowViewModel? Current { get; private set; }

        public MainWindowViewModel(string rootDirectory)
        {
            Current = this;
            _rootDirectory = rootDirectory;
            _settingsManager = new SettingsManager(rootDirectory);
            _installationManager = new InstallationManager(rootDirectory);
            _hardwareDetector = new HardwareDetector();
            _controllerDetector = new ControllerDetector();
            _launcherService = new LauncherService(rootDirectory);

            _installationManager.StatusChanged += msg => StatusMessage = msg;
            _installationManager.ProgressChanged += pct => { /* could show progress */ };

            NavigateCommand = new RelayCommand<LauncherPage>(page => CurrentPage = page);
            LaunchGameCommand = new RelayCommand(async () => await LaunchGameAsync(), () => !IsLaunching);
            ApplyBestSettingsCommand = new RelayCommand(ApplyBestSettings);
            RefreshControllersCommand = new RelayCommand(RefreshControllers);
            RepairInstallationCommand = new RelayCommand(async () => await RepairInstallationAsync());
            RebuildInstallationCommand = new RelayCommand(async () => await RebuildInstallationAsync());
            OpenInstallFolderCommand = new RelayCommand(() => _installationManager.OpenInstallFolder());
            OpenLogFolderCommand = new RelayCommand(() => _installationManager.OpenLogFolder());
            ChangeInstallLocationCommand = new RelayCommand(async () => await ChangeInstallLocationAsync());
            UninstallCommand = new RelayCommand(async () => await UninstallAsync());

            _ = InitializeAsync();
        }

        public override void OnPropertyChanged(string? propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
            if (propertyName == nameof(Settings) && !IsLoading && _settingsManager != null)
            {
                try { _settingsManager.Save(Settings); } catch { }
            }
        }

        public LauncherPage CurrentPage
        {
            get => _currentPage;
            set => SetProperty(ref _currentPage, value);
        }

        public AppSettings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public InstallationState? InstallationState
        {
            get => _installationState;
            set => SetProperty(ref _installationState, value);
        }

        public HardwareInfo? HardwareInfo
        {
            get => _hardwareInfo;
            set => SetProperty(ref _hardwareInfo, value);
        }

        public ObservableCollection<ControllerInfo> Controllers
        {
            get => _controllers;
            set => SetProperty(ref _controllers, value);
        }

        public ControllerInfo? SelectedController
        {
            get => _selectedController;
            set => SetProperty(ref _selectedController, value);
        }

        public bool IsLaunching
        {
            get => _isLaunching;
            set => SetProperty(ref _isLaunching, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand NavigateCommand { get; }
        public ICommand LaunchGameCommand { get; }
        public ICommand ApplyBestSettingsCommand { get; }
        public ICommand RefreshControllersCommand { get; }
        public ICommand RepairInstallationCommand { get; }
        public ICommand RebuildInstallationCommand { get; }
        public ICommand OpenInstallFolderCommand { get; }
        public ICommand OpenLogFolderCommand { get; }
        public ICommand ChangeInstallLocationCommand { get; }
        public ICommand UninstallCommand { get; }

        private async Task InitializeAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading...";

            try
            {
                // Load settings
                Settings = _settingsManager.Load();

                // Check installation state
                InstallationState = await _installationManager.GetInstallationStateAsync();

                // Detect hardware
                HardwareInfo = await Task.Run(() => _hardwareDetector.DetectHardware());

                // Detect controllers
                await RefreshControllersAsync();

                // Apply best settings if first run
                if (Settings.ApplyBestOnStart && InstallationState?.IsInstalled == true)
                {
                    Settings.ApplyBestOnStart = false;
                    ApplyBestSettings();
                }

                StatusMessage = InstallationState?.IsInstalled == true ? "Ready" : "Setup required";

                // A game converted by an earlier version on a CPU that is not an AMD one still holds AMD-only
                // instructions and hangs after the title screen: convert it again, once.
                var runHybrid = System.IO.Path.Combine(_rootDirectory, "Build", "RunHybrid");
                var features = GameFeatures.Load(runHybrid);
                var setup = new GameSetup(_rootDirectory);
                if (InstallationState?.IsInstalled == true && setup.IsReleaseLayout && features != null && !features.IntelConversion &&
                    GameSetup.NeedsIntelConversion() && !string.IsNullOrEmpty(Settings.GameDumpPath))
                {
                    var answer = MessageBox.Show(
                        "This PC's processor needs an updated conversion of the game (it fixes the hang after the title screen on Intel processors).\n\nConvert the game again now? It takes about a minute.",
                        "AstroHybrid", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (answer == MessageBoxResult.Yes)
                    {
                        StatusMessage = "Converting the game for this processor...";
                        try
                        {
                            await _installationManager.RepairAsync();
                            InstallationState = await _installationManager.GetInstallationStateAsync();
                            StatusMessage = "Ready";
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = $"Conversion failed: {ex.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LaunchGameAsync()
        {
            if (InstallationState?.IsInstalled != true)
            {
                MessageBox.Show("Please complete the setup first.", "AstroHybrid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsLaunching = true;
            StatusMessage = "Launching Astro Bot...";

            try
            {
                // Determine which launcher to use based on settings
                int exitCode;
                if (Settings.FrameGenMode != 0)
                {
                    exitCode = await _launcherService.LaunchGameAsync(Settings);
                }
                else if (Settings.Upscale != 0)
                {
                    exitCode = await _launcherService.LaunchGameAsync(Settings);
                }
                else
                {
                    exitCode = await _launcherService.LaunchGameAsync(Settings);
                }

                StatusMessage = exitCode == 0 ? "Game exited normally" : $"Game exited with code {exitCode}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Launch error: {ex.Message}";
                MessageBox.Show($"Failed to launch game:\n{ex.Message}", "AstroHybrid", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLaunching = false;
            }
        }

        private void ApplyBestSettings()
        {
            if (HardwareInfo == null) return;

            var best = PickBestSettings(HardwareInfo);

            Settings.Lighting = 0; // Faster lighting
            Settings.Fps = 0; // 30 FPS steady
            Settings.Speed = 0; // 100% real-time
            Settings.Size = best.Size;
            Settings.Fullscreen = true;
            Settings.Upscale = best.Upscale;
            Settings.FrameGenMode = best.FrameGen;
            Settings.FrameGenTarget = best.Target;
            Settings.LowLatency = best.LowLatency;
            Settings.Controller = 0; // Auto
            Settings.Rumble = 2; // Medium

            _settingsManager.Save(Settings);
            StatusMessage = "Applied best settings for this PC";
        }

        private void RefreshControllers()
        {
            _ = RefreshControllersAsync();
        }

        // The controller choice stays the user's (Auto lets the game pick the pad that is pressed).
        private async Task RefreshControllersAsync()
        {
            var detected = await Task.Run(() => _controllerDetector.DetectControllers());
            Controllers.Clear();
            foreach (var c in detected)
                Controllers.Add(c);

        }

        private async Task RepairInstallationAsync()
        {
            if (InstallationState?.GameDumpPath == null)
            {
                MessageBox.Show("No game dump configured. Please run setup again.", "AstroHybrid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("This will rebuild the installation from your game dump. Continue?",
                "Repair Installation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            IsLoading = true;
            StatusMessage = "Repairing installation...";

            try
            {
                var success = await _installationManager.RepairAsync();
                if (success)
                {
                    InstallationState = await _installationManager.GetInstallationStateAsync();
                    StatusMessage = "Repair complete";
                }
                else
                {
                    StatusMessage = "Repair failed";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Repair error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RebuildInstallationAsync()
        {
            if (InstallationState?.GameDumpPath == null)
            {
                MessageBox.Show("No game dump configured. Please run setup again.", "AstroHybrid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("This will completely rebuild the installation. Continue?",
                "Rebuild Installation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            IsLoading = true;
            StatusMessage = "Rebuilding installation...";

            try
            {
                var success = await _installationManager.RebuildAsync();
                if (success)
                {
                    InstallationState = await _installationManager.GetInstallationStateAsync();
                    StatusMessage = "Rebuild complete";
                }
                else
                {
                    StatusMessage = "Rebuild failed";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Rebuild error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ChangeInstallLocationAsync()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select new installation location",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                IsLoading = true;
                StatusMessage = "Moving installation...";

                try
                {
                    await _installationManager.ChangeInstallLocationAsync(dialog.SelectedPath);
                    InstallationState = await _installationManager.GetInstallationStateAsync();
                    StatusMessage = "Installation moved successfully";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Move error: {ex.Message}";
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private async Task UninstallAsync()
        {
            var result = MessageBox.Show("This will remove all AstroHybrid generated files. Your original game dump will NOT be deleted. Continue?",
                "Uninstall AstroHybrid", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            result = MessageBox.Show("Are you sure? This cannot be undone.",
                "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            IsLoading = true;
            StatusMessage = "Uninstalling...";

            try
            {
                await _installationManager.UninstallAsync(true); // Keep game dump
                InstallationState = await _installationManager.GetInstallationStateAsync();
                Settings.FirstRunComplete = false;
                _settingsManager.Save(Settings);
                StatusMessage = "Uninstall complete. Run setup to reinstall.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Uninstall error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // Best settings logic (ported from AstroBotLauncher.cs)
        private (int Size, int Upscale, int FrameGen, int Target, bool LowLatency) PickBestSettings(HardwareInfo pc)
        {
            var best = (Size: 2, Upscale: 0, FrameGen: 0, Target: 2, LowLatency: false);

            for (int i = 0; i < 4; i++)
            {
                var widths = new[] { 1280, 1600, 1920, 2560 };
                var heights = new[] { 720, 900, 1080, 1440 };
                if (widths[i] <= pc.ScreenWidth && heights[i] <= pc.ScreenHeight)
                    best.Size = i;
            }

            if (pc.RtxSeries >= 20)
            {
                best.Upscale = 1;
                best.LowLatency = true;
            }

            if (pc.RtxSeries >= 40)
            {
                best.FrameGen = 1; // Auto
                int goal = pc.RtxSeries >= 50 && pc.RefreshRate >= 120 ? 120 :
                          pc.RtxSeries >= 50 && pc.RefreshRate >= 90 ? 90 : 60;
                var targetValues = new[] { 0, 50, 60, 90, 120, 144, 165 };
                best.Target = Array.IndexOf(targetValues, goal);
                if (best.Target < 0) best.Target = 2;
            }

            return best;
        }
    }
}