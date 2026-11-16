using System;
using System.ComponentModel;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

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

        public string[] RumbleLabels => new[] { "Off", "Almost off (5%)", "Minimal (10%)", "Very light (20%)", "Light (40%)", "Medium (70%, recommended)", "Full (100%)", "Strong (150%)", "Custom..." };

        // Percent behind every label; -1 is the "Custom" row, which uses the number box.
        static readonly int[] RumblePresets = { 0, 5, 10, 20, 40, 70, 100, 150, -1 };

        public int RumbleIndex
        {
            get
            {
                int percent = _mainViewModel.Settings.EffectiveRumblePercent;
                int index = Array.IndexOf(RumblePresets, percent);
                return index >= 0 ? index : RumblePresets.Length - 1;
            }
            set
            {
                if (value < 0 || value >= RumblePresets.Length) return;
                if (RumblePresets[value] < 0)
                {
                    // "Custom": keep the strength and unlock the number box.
                    OnPropertyChanged(nameof(RumbleIndex));
                    OnPropertyChanged(nameof(IsCustomRumble));
                    return;
                }
                SetRumblePercent(RumblePresets[value]);
            }
        }

        public bool IsCustomRumble => RumblePresets[RumbleIndex] < 0;

        public int RumbleCustomPercent
        {
            get => _mainViewModel.Settings.EffectiveRumblePercent;
            set => SetRumblePercent(value);
        }

        private void SetRumblePercent(int percent)
        {
            var settings = _mainViewModel.Settings;
            int clamped = RumbleSetting.ClampPercent(percent);
            if (settings.RumblePercent == clamped) return;
            settings.RumblePercent = clamped;
            settings.Rumble = RumbleSetting.ToLegacyIndex(clamped);
            OnPropertyChanged(nameof(RumbleIndex));
            OnPropertyChanged(nameof(RumbleCustomPercent));
            OnPropertyChanged(nameof(IsCustomRumble));
            _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
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

        public string VibrationNote => "Vibration sets Xbox, Switch and other pads; a DualSense keeps its own haptics. " +
                                      "Pick Custom to type any strength from 0 to 200%. A very light setting can be too weak for a third-party Switch-style pad, which then needs 'Stronger vibration' or a higher strength.";

        public string TriggerNote => "DualSense adaptive triggers are supported. Trigger effect state is sent to the game so L2/R2 work correctly.";

        public bool DualSenseWirelessSpeaker
        {
            get => _mainViewModel.Settings.DualSenseWirelessSpeaker;
            set
            {
                if (_mainViewModel.Settings.DualSenseWirelessSpeaker != value)
                {
                    _mainViewModel.Settings.DualSenseWirelessSpeaker = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public bool DualSenseWirelessHaptics
        {
            get => _mainViewModel.Settings.DualSenseWirelessHaptics;
            set
            {
                if (_mainViewModel.Settings.DualSenseWirelessHaptics != value)
                {
                    _mainViewModel.Settings.DualSenseWirelessHaptics = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string DualSenseWirelessNote => "Streams controller speaker effects and HD haptics to a Bluetooth-connected DualSense using Opus audio over Bluetooth HID (USB-connected DualSense controllers use native 4-channel USB audio automatically).";

        public System.Collections.ObjectModel.ObservableCollection<ControllerInfo> ConnectedControllers => _mainViewModel.Controllers;

        public string RecommendedController => _mainViewModel.Controllers.Count > 0
            ? _mainViewModel.Controllers[0].Type
            : "None detected";

        public void RefreshControllers() => _mainViewModel.RefreshControllersCommand.Execute(null);
    }
}