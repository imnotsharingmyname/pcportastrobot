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

        public string CompactMemoryDisplayInfo
        {
            get
            {
                var hw = _mainViewModel.HardwareInfo;
                if (hw == null) return "Detecting...";
                var ram = hw.RamGb > 0 ? $"{hw.RamGb} GB" : "? GB";
                return hw.ScreenWidth > 0 && hw.ScreenHeight > 0
                    ? $"{ram} / {hw.ScreenWidth} × {hw.ScreenHeight} · {hw.RefreshRate} Hz"
                    : ram;
            }
        }

        public string ProfileResolution
        {
            get
            {
                int idx = _mainViewModel.Settings.Size;
                return idx >= 0 && idx < AppSettings.ResolutionPresets.Length
                    ? AppSettings.ResolutionPresets[idx].Label.Replace(" x ", " × ")
                    : "1920 × 1080";
            }
        }

        public string ProfileUpscale => _mainViewModel.Settings.Upscale == 1 ? "DLSS" : _mainViewModel.Settings.Upscale == 2 ? "AMD FSR" : _mainViewModel.Settings.Upscale == 3 ? "Intel XeSS" : "Native Scale";

        public string ProfileFrameGen
        {
            get
            {
                var fgLabels = new[] { "Frame Gen Off", "Frame Gen Auto", "Frame Gen 2x", "Frame Gen 3x", "Frame Gen 4x" };
                int idx = _mainViewModel.Settings.FrameGenMode;
                if (idx == 0 && _mainViewModel.Settings.FsrFrameGen) return "FSR Frame Gen 2x";
                return idx >= 0 && idx < fgLabels.Length ? fgLabels[idx] : "Frame Gen Off";
            }
        }

        public string ProfileLowLatency => _mainViewModel.Settings.LowLatency ? "Reflex" : "Reflex Off";

        public string ProfileLighting => _mainViewModel.Settings.Lighting == 1 ? "RTX ON" : "Non-RT";

        public string CurrentGraphicsConfig => BuildGraphicsConfigString();

        public string RayTracingToggleText => _mainViewModel.Settings.Lighting == 1 ? "RTX: ON" : "RTX: OFF";

        public bool CanLaunch => _mainViewModel.InstallationState?.IsInstalled == true;

        public ICommand NavigateToSettingsCommand { get; }
        public ICommand NavigateToControllerCommand { get; }
        public ICommand NavigateToInstallationCommand { get; }

        // Pass-throughs for HomeView.xaml release-notes / update banner bindings.
        public UpdateState UpdateState => _mainViewModel.UpdateState;
        public ICommand DownloadUpdateCommand => _mainViewModel.DownloadUpdateCommand;
        public ICommand IgnoreUpdateCommand => _mainViewModel.IgnoreUpdateCommand;

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

            int idx = _mainViewModel.Settings.Size;
            if (idx >= 0 && idx < AppSettings.ResolutionPresets.Length)
                parts.Add(AppSettings.ResolutionPresets[idx].ShortLabel);

            if (_mainViewModel.Settings.Ultrawide)
                parts.Add(_mainViewModel.Settings.IsSuperUltrawideSize ? "32:9 Ultrawide" : "21:9 Ultrawide");

            parts.Add(_mainViewModel.Settings.Fullscreen ? "Fullscreen" : "Windowed");

            parts.Add(_mainViewModel.Settings.Lighting == 1 ? "RTX ON" : "RTX OFF (Non-RT)");

            if (_mainViewModel.Settings.Upscale == 1)
                parts.Add("DLSS");
            else if (_mainViewModel.Settings.Upscale == 2)
                parts.Add("AMD FSR");
            else if (_mainViewModel.Settings.Upscale == 3)
                parts.Add("Intel XeSS");

            if (_mainViewModel.Settings.FrameGenMode != 0)
            {
                var fgLabels = new[] { "", "Auto", "2x", "3x", "4x" };
                if (_mainViewModel.Settings.FrameGenMode < fgLabels.Length)
                    parts.Add($"FG {fgLabels[_mainViewModel.Settings.FrameGenMode]}");
            }

            bool fsrFrameGen = _mainViewModel.Settings.FsrFrameGen && _mainViewModel.Settings.FrameGenMode == 0;
            if (fsrFrameGen)
                parts.Add("FSR FG 2x");

            // Reflex is off while AMD FSR frame generation presents the frames.
            if (_mainViewModel.Settings.LowLatency && !fsrFrameGen)
                parts.Add("Reflex");

            return string.Join(" | ", parts);
        }
    }
}
