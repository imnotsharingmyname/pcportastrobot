using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class AdvancedView : UserControl
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;

    public AdvancedView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        EnvBox.Text = settings.ExtraEnv;
        InfoText.Text =
            $"project:  {_backend.Root}\n" +
            $"python:   {_backend.Python}\n" +
            $"runtime:  {_backend.HybridDirectory}\n" +
            $"hybrid:   {(_backend.HybridReady ? "installed" : "missing")}\n" +
            $"settings: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "astrohybrid", "settings.json")}";
    }

    private void Save(object? sender, RoutedEventArgs e)
    {
        _settings.ExtraEnv = EnvBox.Text ?? "";
        _backend.SaveSettings(_settings);
        Status.Text = "Saved.";
    }

    private void Reset(object? sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings
        {
            GamePath = Directory.Exists(_backend.DefaultGamePath) ? _backend.DefaultGamePath : _settings.GamePath,
        };
        _backend.SaveSettings(defaults);
        EnvBox.Text = defaults.ExtraEnv;
        Status.Text = "Settings reset to defaults.";
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
            Status.Text = $"Could not open the folder: {exception.Message}";
        }
    }
}
