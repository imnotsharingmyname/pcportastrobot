using System;
using System.ComponentModel;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.ViewModels
{
    public class GraphicsSettingsViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainViewModel;

        public GraphicsSettingsViewModel(MainWindowViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += (sender, e) => OnPropertyChanged(string.Empty);
        }

        // Resolution
        public string[] ResolutionLabels => new[] { "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440" };

        public int ResolutionIndex
        {
            get => _mainViewModel.Settings.Size;
            set
            {
                if (_mainViewModel.Settings.Size != value)
                {
                    _mainViewModel.Settings.Size = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Fullscreen
        public bool Fullscreen
        {
            get => _mainViewModel.Settings.Fullscreen;
            set
            {
                if (_mainViewModel.Settings.Fullscreen != value)
                {
                    _mainViewModel.Settings.Fullscreen = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // FPS Limiter
        public string[] FpsLabels => new[] { "30 FPS - steady (recommended)", "35 FPS", "45 FPS", "50 FPS", "55 FPS", "60 FPS", "No limit" };

        public int FpsIndex
        {
            get => _mainViewModel.Settings.Fps;
            set
            {
                if (_mainViewModel.Settings.Fps != value)
                {
                    _mainViewModel.Settings.Fps = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Upscaling
        public string[] UpscaleLabels => new[] { "Game's own (default)", "NVIDIA DLSS - sharper image" };

        public int UpscaleIndex
        {
            get => _mainViewModel.Settings.Upscale;
            set
            {
                if (_mainViewModel.Settings.Upscale != value)
                {
                    _mainViewModel.Settings.Upscale = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsFrameGenAvailable));
                    OnPropertyChanged(nameof(FrameGenNote));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Frame Generation
        public string[] FrameGenLabels => new[] { "Off (default)", "Auto - reach target (recommended)", "Fixed 2x", "Fixed 3x", "Fixed 4x" };

        public int FrameGenIndex
        {
            get => _mainViewModel.Settings.FrameGenMode;
            set
            {
                if (_mainViewModel.Settings.FrameGenMode != value)
                {
                    _mainViewModel.Settings.FrameGenMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsFrameGenTargetEnabled));
                    OnPropertyChanged(nameof(FrameGenNote));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Frame Gen Target
        public string[] FrameGenTargetLabels => new[] { "No lock - game as fast as it can", "50 FPS on screen", "60 FPS on screen (recommended)", "90 FPS on screen", "120 FPS on screen", "144 FPS on screen", "165 FPS on screen" };

        public int FrameGenTargetIndex
        {
            get => _mainViewModel.Settings.FrameGenTarget;
            set
            {
                if (_mainViewModel.Settings.FrameGenTarget != value)
                {
                    _mainViewModel.Settings.FrameGenTarget = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FrameGenNote));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // NVIDIA Reflex
        public bool LowLatency
        {
            get => _mainViewModel.Settings.LowLatency;
            set
            {
                if (_mainViewModel.Settings.LowLatency != value)
                {
                    _mainViewModel.Settings.LowLatency = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Performance Overlay
        public string[] OverlayLabels => new[] { "Off (F2 shows it)", "FPS only", "FPS + CPU, GPU, VRAM, RAM", "Everything (all stats)" };

        public int OverlayIndex
        {
            get => _mainViewModel.Settings.Overlay;
            set
            {
                if (_mainViewModel.Settings.Overlay != value)
                {
                    _mainViewModel.Settings.Overlay = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Computed properties for UI state
        public bool IsFrameGenAvailable => _mainViewModel.HardwareInfo?.SupportsFrameGeneration == true && UpscaleIndex != 0;

        public bool IsFrameGenTargetEnabled => FrameGenIndex != 0;

        public bool SupportsDlss => _mainViewModel.HardwareInfo?.SupportsDlss == true;

        public bool SupportsReflex => _mainViewModel.HardwareInfo?.SupportsReflex == true;

        public string FrameGenNote
        {
            get
            {
                if (FrameGenIndex == 0)
                    return "Frame generation multiplies the frames you see (30 FPS limit x2 = 60 on screen) and turns Reflex on itself. It runs only while the game window is focused. Do not also use NVIDIA Smooth Motion.";

                var targetValues = new[] { 0, 50, 60, 90, 120, 144, 165 };
                int goal = FrameGenTargetIndex >= 0 && FrameGenTargetIndex < targetValues.Length ? targetValues[FrameGenTargetIndex] : 60;
                int mult = FrameGenIndex == 1 ? 2 : FrameGenIndex;

                if (FrameGenIndex == 1 && goal != 0)
                {
                    int limit = Math.Max(30, goal / 2);
                    return $"Game up to {limit} FPS; x2 to x4 picked in game to stay at {goal}+ FPS on screen";
                }
                else if (goal == 0)
                {
                    return $"Game unlocked, x{mult} on screen (e.g. 30 -> {30 * mult})";
                }
                else
                {
                    int limit = Math.Max(30, goal / mult);
                    return $"Game up to {limit} FPS x{mult} = {limit * mult} on screen";
                }
            }
        }

        public string HardwareNote
        {
            get
            {
                if (_mainViewModel.HardwareInfo == null) return "";

                var gpu = _mainViewModel.HardwareInfo.GpuName;
                if (_mainViewModel.HardwareInfo.RtxSeries >= 50)
                    return $"{gpu}: DLSS + Multi Frame Gen (up to 4x) + Reflex available";
                else if (_mainViewModel.HardwareInfo.RtxSeries >= 40)
                    return $"{gpu}: DLSS + Frame Gen (2x) + Reflex available";
                else if (_mainViewModel.HardwareInfo.RtxSeries >= 20)
                    return $"{gpu}: DLSS + Reflex available (Frame Gen needs RTX 40+)";
                else
                    return $"{gpu}: Using game's own upscaling (NVIDIA features need RTX 20+)";
            }
        }
    }
}