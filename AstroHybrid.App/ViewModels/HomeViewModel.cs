using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AstroHybrid.App.Models;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainViewModel;

        public HomeViewModel(MainWindowViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += (sender, e) => OnPropertyChanged(string.Empty);
            NavigateToSettingsCommand = new RelayCommand(() => _mainViewModel.CurrentPage = LauncherPage.GraphicsSettings);
            NavigateToControllerCommand = new RelayCommand(() => _mainViewModel.CurrentPage = LauncherPage.ControllerSettings);
            NavigateToInstallationCommand = new RelayCommand(() => _mainViewModel.CurrentPage = LauncherPage.Installation);
        }

        public string GameStatus => _mainViewModel.InstallationState?.IsInstalled == true
            ? "Installed and Ready"
            : "Not Installed";

        public string GameVersion => _mainViewModel.InstallationState?.GameVersion ?? "Unknown";

        public string GameTitleId => _mainViewModel.InstallationState?.GameTitleId ?? "Unknown";

        public string CpuInfo => _mainViewModel.HardwareInfo?.CpuName ?? "Unknown";

        public string GpuInfo => _mainViewModel.HardwareInfo?.GpuName ?? "Unknown";

        public string RamInfo => _mainViewModel.HardwareInfo?.RamGb > 0
            ? $"{_mainViewModel.HardwareInfo.RamGb} GB"
            : "Unknown";

        public string DisplayInfo => _mainViewModel.HardwareInfo != null
            ? $"{_mainViewModel.HardwareInfo.ScreenWidth}x{_mainViewModel.HardwareInfo.ScreenHeight}\n{_mainViewModel.HardwareInfo.RefreshRate} Hz"
            : "Unknown";

        public string CurrentGraphicsConfig => BuildGraphicsConfigString();

        public string RayTracingToggleText => _mainViewModel.Settings.Lighting == 1 ? "RTX: ON" : "RTX: OFF";

        public bool CanLaunch => _mainViewModel.InstallationState?.IsInstalled == true;

        public ICommand NavigateToSettingsCommand { get; }
        public ICommand NavigateToControllerCommand { get; }
        public ICommand NavigateToInstallationCommand { get; }
        public ICommand ToggleRayTracingCommand => new RelayCommand(() =>
        {
            _mainViewModel.Settings.Lighting = _mainViewModel.Settings.Lighting == 1 ? 0 : 1;
            OnPropertyChanged(nameof(CurrentGraphicsConfig));
            OnPropertyChanged(nameof(RayTracingToggleText));
            _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
        });

        private string BuildGraphicsConfigString()
        {
            var parts = new System.Collections.Generic.List<string>();

            var sizeLabels = new[] { "1280x720", "1600x900", "1920x1080", "2560x1440" };
            if (_mainViewModel.Settings.Size >= 0 && _mainViewModel.Settings.Size < sizeLabels.Length)
                parts.Add(sizeLabels[_mainViewModel.Settings.Size]);

            if (_mainViewModel.Settings.Ultrawide)
                parts.Add("21:9 Ultrawide");

            parts.Add(_mainViewModel.Settings.Fullscreen ? "Fullscreen" : "Windowed");

            parts.Add(_mainViewModel.Settings.Lighting == 1 ? "RTX ON" : "RTX OFF (Non-RT)");

            if (_mainViewModel.Settings.Upscale != 0)
                parts.Add("DLSS");

            if (_mainViewModel.Settings.FrameGenMode != 0)
            {
                var fgLabels = new[] { "", "Auto", "2x", "3x", "4x" };
                if (_mainViewModel.Settings.FrameGenMode < fgLabels.Length)
                    parts.Add($"FG {fgLabels[_mainViewModel.Settings.FrameGenMode]}");
            }

            if (_mainViewModel.Settings.LowLatency)
                parts.Add("Reflex");

            return string.Join(" | ", parts);
        }
    }
}