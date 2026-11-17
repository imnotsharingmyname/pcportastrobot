using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class ControllerView : UserControl, IPageRefresh
{
    private static readonly string[] ControllerLabels =
    {
        "Auto - the controller you press (recommended)",
        "PlayStation (DualSense, DualShock 4)",
        "Xbox",
        "Switch Pro (and compatible pads)",
        "Other / generic",
    };

    private static readonly string[] ControllerValues = { "auto", "ps", "xbox", "switch", "generic" };

    private static readonly (string Label, int Percent)[] RumbleOptions =
    {
        ("Off", 0),
        ("Almost off (5%)", 5),
        ("Minimal (10%)", 10),
        ("Very light (20%)", 20),
        ("Light (40%)", 40),
        ("Medium (70%, recommended)", 70),
        ("Full (100%)", 100),
        ("Strong (150%)", 150),
        ("Custom...", -1),
    };

    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private bool _loading = true;

    public ControllerView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;

        ControllerBox.ItemsSource = ControllerLabels;
        RumbleBox.ItemsSource = RumbleOptions.Select(option => option.Label).ToArray();

        Loaded += (_, _) => RefreshPage();
    }

    public void RefreshPage()
    {
        _loading = true;

        var controllerIndex = Array.IndexOf(ControllerValues, _settings.ControllerPref);
        ControllerBox.SelectedIndex = controllerIndex >= 0 ? controllerIndex : 0;

        var rumbleIndex = Array.FindIndex(RumbleOptions, option => option.Percent == _settings.RumblePercent);
        RumbleBox.SelectedIndex = rumbleIndex >= 0 && _settings.RumblePercent >= 0 ? rumbleIndex : 8;
        RumbleCustomBox.Text = _settings.RumblePercent.ToString();
        RumbleCustomBox.IsEnabled = RumbleBox.SelectedIndex == 8;

        SwitchBoostBox.IsChecked = _settings.RumbleSwitchBoost;
        MotionBox.IsChecked = _settings.PadMotion;
        DsSpeakerBox.IsChecked = _settings.DsWirelessSpeaker;
        DsHapticsBox.IsChecked = _settings.DsWirelessHaptics;

        _loading = false;

        RefreshControllers(null, null);
    }

    private void OnCommitted(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        RumbleCustomBox.IsEnabled = RumbleBox.SelectedIndex == 8;
        Save();
    }

    private void OnCheckCommitted(object? sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Save();
    }

    private void OnCustomRumble(object? sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Save();
    }

    private void Save()
    {
        if (ControllerBox.SelectedIndex >= 0)
        {
            _settings.ControllerPref = ControllerValues[ControllerBox.SelectedIndex];
        }

        if (RumbleBox.SelectedIndex >= 0 && RumbleBox.SelectedIndex != 8)
        {
            _settings.RumblePercent = RumbleOptions[RumbleBox.SelectedIndex].Percent;
        }
        else if (int.TryParse(RumbleCustomBox.Text, out var custom))
        {
            _settings.RumblePercent = Math.Clamp(custom, 0, 200);
        }

        _settings.RumbleSwitchBoost = SwitchBoostBox.IsChecked == true;
        _settings.PadMotion = MotionBox.IsChecked == true;
        _settings.DsWirelessSpeaker = DsSpeakerBox.IsChecked == true;
        _settings.DsWirelessHaptics = DsHapticsBox.IsChecked == true;

        _backend.SaveSettings(_settings);
        _backend.SetStatus("Settings saved");
    }

    private void RefreshControllers(object? sender, RoutedEventArgs? e)
    {
        var pads = _backend.DetectGamePads();
        PadList.ItemsSource = pads;
        EmptyText.IsVisible = pads.Count == 0;
    }
}
