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

        // Xbox/generic pads: how View/Back turns the right-stick tilt on (#146).
        public string[] TiltModeLabels => new[] { "Hold View/Back to tilt", "Press View/Back to turn tilt on/off" };

        public int TiltModeIndex
        {
            get => _mainViewModel.Settings.TiltToggle ? 1 : 0;
            set
            {
                bool toggle = value == 1;
                if (_mainViewModel.Settings.TiltToggle != toggle)
                {
                    _mainViewModel.Settings.TiltToggle = toggle;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MotionNote));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string[] TiltStrengthLabels => new[] { "Very gentle (25%)", "Gentle (50%)", "Reduced (75%)", "Default (100%)", "Strong (150%)", "Very strong (200%)" };
        static readonly int[] TiltStrengthPresets = { 25, 50, 75, 100, 150, 200 };

        public int TiltStrengthIndex
        {
            get
            {
                int index = Array.IndexOf(TiltStrengthPresets, _mainViewModel.Settings.TiltStrength);
                return index >= 0 ? index : 3;
            }
            set
            {
                if (value < 0 || value >= TiltStrengthPresets.Length) return;
                if (_mainViewModel.Settings.TiltStrength != TiltStrengthPresets[value])
                {
                    _mainViewModel.Settings.TiltStrength = TiltStrengthPresets[value];
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // DualSense adaptive triggers: off, lighter, or as the game asks (#160).
        public string[] TriggerStrengthLabels => new[] { "Off", "Light (33%)", "Medium (66%)", "Full (as the game asks)" };
        static readonly int[] TriggerStrengthPresets = { 0, 33, 66, 100 };

        public int TriggerStrengthIndex
        {
            get
            {
                int index = Array.IndexOf(TriggerStrengthPresets, _mainViewModel.Settings.TriggerStrength);
                return index >= 0 ? index : TriggerStrengthPresets.Length - 1;
            }
            set
            {
                if (value < 0 || value >= TriggerStrengthPresets.Length) return;
                if (_mainViewModel.Settings.TriggerStrength != TriggerStrengthPresets[value])
                {
                    _mainViewModel.Settings.TriggerStrength = TriggerStrengthPresets[value];
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // DualSense speaker on or off (#143).
        public bool DualSenseSpeaker
        {
            get => _mainViewModel.Settings.DualSenseSpeaker;
            set
            {
                if (_mainViewModel.Settings.DualSenseSpeaker != value)
                {
                    _mainViewModel.Settings.DualSenseSpeaker = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public int DualSenseSpeakerVolume
        {
            get => _mainViewModel.Settings.DualSenseSpeakerVolume;
            set
            {
                int volume = Math.Max(0, Math.Min(100, value));
                if (_mainViewModel.Settings.DualSenseSpeakerVolume == volume) return;
                _mainViewModel.Settings.DualSenseSpeakerVolume = volume;
                OnPropertyChanged();
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        public string MotionNote => _mainViewModel.Settings.TiltToggle
            ? "Motion sensors (shake/tilt) work on DualSense and Switch Pro controllers. Xbox and generic pads: press View/Back to tilt with the right stick, press it again to get the camera back."
            : "Motion sensors (shake/tilt) work on DualSense and Switch Pro controllers. Xbox and generic pads: hold View/Back and move the right stick to tilt; release it to get the camera back.";

        public string VibrationNote => "Vibration sets Xbox, Switch and other pads; a DualSense keeps its own haptics. " +
                                      "Pick Custom to type any strength from 0 to 200%. A very light setting can be too weak for a third-party Switch-style pad, which then needs 'Stronger vibration' or a higher strength.";

        public string TriggerNote => "DualSense adaptive triggers are supported. Trigger effect state is sent to the game so L2/R2 work correctly.";

        public System.Collections.ObjectModel.ObservableCollection<ControllerInfo> ConnectedControllers => _mainViewModel.Controllers;

        public string RecommendedController => _mainViewModel.Controllers.Count > 0
            ? _mainViewModel.Controllers[0].Type
            : "None detected";

        public void RefreshControllers() => _mainViewModel.RefreshControllersCommand.Execute(null);
    }
}
