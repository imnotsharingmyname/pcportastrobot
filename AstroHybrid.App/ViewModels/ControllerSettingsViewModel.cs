using System;
using System.ComponentModel;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.ViewModels
{
    public class ControllerSettingsViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainViewModel;

        public ControllerSettingsViewModel(MainWindowViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += (sender, e) => OnPropertyChanged(string.Empty);
        }

        public string[] ControllerLabels => new[] { "Auto - the controller you press (recommended)", "PlayStation (DualSense, DualShock 4)", "Xbox", "Switch Pro (and compatible pads)", "Other / generic" };

        public int ControllerIndex
        {
            get => _mainViewModel.Settings.Controller;
            set
            {
                if (_mainViewModel.Settings.Controller != value)
                {
                    _mainViewModel.Settings.Controller = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string[] RumbleLabels => new[] { "Off", "Light (40%)", "Medium (70%, recommended)", "Full (100%)", "Strong (150%)" };

        public int RumbleIndex
        {
            get => _mainViewModel.Settings.Rumble;
            set
            {
                if (_mainViewModel.Settings.Rumble != value)
                {
                    _mainViewModel.Settings.Rumble = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public bool SwitchBoost
        {
            get => _mainViewModel.Settings.SwitchBoost;
            set
            {
                if (_mainViewModel.Settings.SwitchBoost != value)
                {
                    _mainViewModel.Settings.SwitchBoost = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string MotionNote => "Motion sensors (shake/tilt) work on DualSense and Switch Pro controllers. Xbox and generic pads: press View/Back to tilt with right stick, press again to stop.";

        public string VibrationNote => "Vibration sets Xbox, Switch and other pads; a DualSense keeps its own haptics. Third-party Switch-style pads may need 'Stronger vibration on Switch-style pads' enabled.";

        public string TriggerNote => "DualSense adaptive triggers are supported. Trigger effect state is sent to the game so L2/R2 work correctly.";

        public System.Collections.ObjectModel.ObservableCollection<ControllerInfo> ConnectedControllers => _mainViewModel.Controllers;

        public string RecommendedController => _mainViewModel.Controllers.Count > 0
            ? _mainViewModel.Controllers[0].Type
            : "None detected";

        public void RefreshControllers() => _mainViewModel.RefreshControllersCommand.Execute(null);
    }
}