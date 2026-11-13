using System;
using System.ComponentModel;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.ViewModels
{
    public class AdvancedSettingsViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainViewModel;

        public AdvancedSettingsViewModel(MainWindowViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += (sender, e) => OnPropertyChanged(string.Empty);
        }

        // Lighting/Renderer
        public string[] LightingLabels => new[] { "Faster lighting (recommended)", "Original lighting (slower)" };

        public int LightingIndex
        {
            get => _mainViewModel.Settings.Lighting;
            set
            {
                if (_mainViewModel.Settings.Lighting != value)
                {
                    _mainViewModel.Settings.Lighting = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Game Speed
        public string[] SpeedLabels => new[] { "100% - real time", "95%", "90%", "85%", "80%", "75% (recommended)" };

        public int SpeedIndex
        {
            get => _mainViewModel.Settings.Speed;
            set
            {
                if (_mainViewModel.Settings.Speed != value)
                {
                    _mainViewModel.Settings.Speed = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Hang Watchdog
        private bool _hangWatchdog = true;
        public bool HangWatchdog
        {
            get => _hangWatchdog;
            set => SetProperty(ref _hangWatchdog, value);
        }

        // Shader Cache
        private bool _shaderCache = true;
        public bool ShaderCache
        {
            get => _shaderCache;
            set => SetProperty(ref _shaderCache, value);
        }

        // Draw Prep Tuning
        private bool _drawPrepTuning = true;
        public bool DrawPrepTuning
        {
            get => _drawPrepTuning;
            set => SetProperty(ref _drawPrepTuning, value);
        }

        // Priority
        private string _priority = "abovenormal";
        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        public string[] PriorityOptions => new[] { "normal", "abovenormal", "high", "realtime" };

        // CP Priority
        private int _cpPriority = 2;
        public int CpPriority
        {
            get => _cpPriority;
            set => SetProperty(ref _cpPriority, value);
        }

        public int[] CpPriorityOptions => new[] { 0, 1, 2 };

        // CUDA Hole
        private bool _cudaHole = true;
        public bool CudaHole
        {
            get => _cudaHole;
            set => SetProperty(ref _cudaHole, value);
        }

        // Motion Controls
        private bool _motionControls = true;
        public bool MotionControls
        {
            get => _motionControls;
            set => SetProperty(ref _motionControls, value);
        }

        // Trigger State
        private bool _triggerState = true;
        public bool TriggerState
        {
            get => _triggerState;
            set => SetProperty(ref _triggerState, value);
        }

        // Experimental Features
        public bool IsExperimentalExpanded { get; set; } = false;

        // Info texts
        public string LightingNote => "Faster lighting uses the NonRT renderer (AstroBot-NonRT.exe) which disables GI probes and some lighting shaders for better performance.";

        public string SpeedNote => "The game runs in real-time regardless of FPS. Lower values slow down the game simulation.";

        public string HangWatchdogNote => "Records diagnostics when the game hangs. Enable when reporting freezes. Defaults to ON for RTX 50 series.";

        public string ShaderCacheNote => "Persistent shader and pipeline caches eliminate late shader compiles (100-117ms hitches). Requires title ID.";

        public string DrawPrepNote => "Raises draw-prep helper priority to match CP priority, reducing CP spin time by ~1.2ms/frame when other programs run.";

        public string PriorityNote => "Process and thread priorities keep other programs off the reserved CP core.";

        public string MotionNote => "Enables accelerometer/gyroscope input for shake and tilt moves. Disable if your controller doesn't have motion sensors.";

        public string TriggerNote2 => "Enables DualSense trigger effect state reporting. The game ignores L2/R2 without it.";

        public string ExperimentalWarning => "Experimental features may cause instability. Only enable if you understand the risks and are prepared to debug issues.";
    }
}