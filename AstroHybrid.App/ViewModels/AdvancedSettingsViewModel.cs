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

        public bool HangWatchdog
        {
            get => _mainViewModel.Settings.HangWatchdog;
            set
            {
                if (!Equals(_mainViewModel.Settings.HangWatchdog, value))
                {
                    _mainViewModel.Settings.HangWatchdog = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public bool ShaderCache
        {
            get => _mainViewModel.Settings.ShaderCache;
            set
            {
                if (!Equals(_mainViewModel.Settings.ShaderCache, value))
                {
                    _mainViewModel.Settings.ShaderCache = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public bool DrawPrepTuning
        {
            get => _mainViewModel.Settings.DrawPrepTuning;
            set
            {
                if (!Equals(_mainViewModel.Settings.DrawPrepTuning, value))
                {
                    _mainViewModel.Settings.DrawPrepTuning = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string Priority
        {
            get => _mainViewModel.Settings.Priority;
            set
            {
                if (!Equals(_mainViewModel.Settings.Priority, value))
                {
                    _mainViewModel.Settings.Priority = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string[] PriorityOptions => new[] { "normal", "abovenormal", "high" };

        public int CpPriority
        {
            get => _mainViewModel.Settings.CpPriority;
            set
            {
                if (!Equals(_mainViewModel.Settings.CpPriority, value))
                {
                    _mainViewModel.Settings.CpPriority = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public int[] CpPriorityOptions => new[] { 1, 2 };

        public bool MotionControls
        {
            get => _mainViewModel.Settings.MotionControls;
            set
            {
                if (!Equals(_mainViewModel.Settings.MotionControls, value))
                {
                    _mainViewModel.Settings.MotionControls = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public bool TriggerState
        {
            get => _mainViewModel.Settings.TriggerState;
            set
            {
                if (!Equals(_mainViewModel.Settings.TriggerState, value))
                {
                    _mainViewModel.Settings.TriggerState = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Experimental Features
        public bool IsExperimentalExpanded { get; set; } = false;

        // Info texts
        public string LightingNote => "Faster lighting uses the NonRT renderer (AstroBot-NonRT.exe) which disables GI probes and some lighting shaders for better performance.";

        public string SpeedNote => "The game runs in real-time regardless of FPS. Lower values slow down the game simulation.";

        public string HangWatchdogNote => "Records diagnostics when the game hangs. Turn on when reporting a freeze; it costs about 1.8 ms per frame on RTX 50 cards, so it is off by default.";

        public string ShaderCacheNote => "Persistent shader and pipeline caches eliminate late shader compiles (100-117ms hitches). Requires title ID.";

        public string DrawPrepNote => "Raises draw-prep helper priority to match CP priority, reducing CP spin time by ~1.2ms/frame when other programs run.";

        public string PriorityNote => "Process priority (above normal recommended) and the command-processor thread priority (2 = highest) keep other programs off the reserved core.";

        public string MotionNote => "Enables accelerometer/gyroscope input for shake and tilt moves. Disable if your controller doesn't have motion sensors.";

        public string TriggerNote2 => "Enables DualSense trigger effect state reporting. The game ignores L2/R2 without it.";

        public string ExperimentalWarning => "Experimental features may cause instability. Only enable if you understand the risks and are prepared to debug issues.";
    }
}