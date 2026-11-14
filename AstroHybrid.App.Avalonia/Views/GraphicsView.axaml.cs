using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class GraphicsView : UserControl, IPageRefresh
{
    private sealed record ResolutionOption(int Width, int Height)
    {
        public override string ToString() => $"{Width} × {Height}";
    }

    private sealed record FpsOption(string Label, int Value)
    {
        public override string ToString() => Label;
    }

    private static readonly ResolutionOption[] Resolutions =
    {
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
    };

    private static readonly FpsOption[] FpsLimits =
    {
        new("Off", 0),
        new("30 FPS", 30),
        new("40 FPS", 40),
        new("45 FPS", 45),
        new("50 FPS", 50),
        new("55 FPS", 55),
        new("60 FPS", 60),
        new("90 FPS", 90),
        new("120 FPS", 120),
    };

    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private bool _loading = true;

    public GraphicsView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;

        ResolutionBox.ItemsSource = Resolutions;
        FpsBox.ItemsSource = FpsLimits;
        LoadFromSettings();
        _loading = false;
    }

    public void RefreshPage()
    {
        _loading = true;
        LoadFromSettings();
        _loading = false;
    }

    private void LoadFromSettings()
    {
        ResolutionBox.SelectedItem = Resolutions.FirstOrDefault(option =>
            option.Width == _settings.Width && option.Height == _settings.Height) ?? Resolutions[2];
        FpsBox.SelectedItem = FpsLimits.FirstOrDefault(option => option.Value == _settings.FpsLimit) ?? FpsLimits[6];
        FullscreenBox.IsChecked = _settings.Fullscreen;
        GameSpeedBox.IsChecked = _settings.GameSpeed;
        DirectAudioBox.IsChecked = _settings.DirectAudio;
        AudioSinkBox.Text = _settings.AudioSink;

    }

    public static (int Width, int Height) PickResolution(Size dip)
    {
        var best = Resolutions
            .Where(option => option.Width <= dip.Width && option.Height <= dip.Height)
            .OrderByDescending(option => option.Width)
            .FirstOrDefault();

        return best is null
            ? (Resolutions[0].Width, Resolutions[0].Height)
            : (best.Width, best.Height);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => Save();

    private void OnCheckChanged(object? sender, RoutedEventArgs e) => Save();

    private void OnAudioSinkLostFocus(object? sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        if (ResolutionBox.SelectedItem is ResolutionOption resolution)
        {
            _settings.Width = resolution.Width;
            _settings.Height = resolution.Height;
        }

        if (FpsBox.SelectedItem is FpsOption fps)
        {
            _settings.FpsLimit = fps.Value;
        }

        _settings.Fullscreen = FullscreenBox.IsChecked == true;
        _settings.GameSpeed = GameSpeedBox.IsChecked == true;
        _settings.DirectAudio = DirectAudioBox.IsChecked == true;
        _settings.AudioSink = AudioSinkBox.Text?.Trim() ?? "";

        _backend.SaveSettings(_settings);
        _backend.SetStatus("Settings saved");
    }
}
