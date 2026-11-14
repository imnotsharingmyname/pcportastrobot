using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class AdvancedView : UserControl, IPageRefresh
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private bool _loading = true;

    public AdvancedView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        EnvBox.Text = settings.ExtraEnv;
        _loading = false;
        RefreshPage();
    }

    public void RefreshPage()
    {
        InfoText.Text =
            $"project:  {_backend.Root}\n" +
            $"python:   {_backend.Python}\n" +
            $"runtime:  {_backend.HybridDirectory}\n" +
            $"hybrid:   {(_backend.HybridReady ? "installed" : "missing")}\n" +
            $"settings: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "astrohybrid", "settings.json")}";
    }

    private void OnEnvLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.ExtraEnv = EnvBox.Text ?? "";
        _backend.SaveSettings(_settings);
        _backend.SetStatus("Settings saved");
    }

    private void Reset(object? sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings
        {
            GamePath = Directory.Exists(_backend.DefaultGamePath) ? _backend.DefaultGamePath : _settings.GamePath,
        };
        _backend.SaveSettings(defaults);
        _settings.ExtraEnv = defaults.ExtraEnv;
        EnvBox.Text = defaults.ExtraEnv;
        RefreshPage();
        _backend.SetStatus("Advanced settings reset to defaults");
    }

    private void OpenLogs(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_backend.LogDirectory);
            Process.Start(new ProcessStartInfo("xdg-open")
            {
                ArgumentList = { _backend.LogDirectory },
                UseShellExecute = false,
            });
        }
        catch (Exception exception)
        {
            _backend.SetStatus($"Could not open the folder: {exception.Message}");
        }
    }
}
