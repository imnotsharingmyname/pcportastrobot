using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class HomeView : UserControl
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;

    public HomeView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        _backend.GameOutput += line => Dispatcher.UIThread.Post(() => AppendLog(line));
        _backend.GameRunningChanged += running => Dispatcher.UIThread.Post(() => UpdateRunning(running));
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var exists = Directory.Exists(_settings.GamePath);
        var ready = _backend.HybridReady;
        StatusText.Text = ready ? "Ready to play" : exists ? "Runtime not built" : "Game dump not found";
        GamePathText.Text = string.IsNullOrWhiteSpace(_settings.GamePath)
            ? "Choose the extracted PPSA21567-app folder on the Setup page."
            : _settings.GamePath;
        HardwareText.Text = _backend.HardwareSummary();
        RuntimeText.Text =
            $"Hybrid runtime: {(ready ? "installed" : "missing")}\n" +
            $"GPU stack: {(_backend.HybridReady ? "Kyty (Vulkan)" : "not installed")}\n" +
            $"Run directory: {_backend.HybridDirectory}";

        if (!ready)
        {
            NoticeCard.IsVisible = true;
            NoticeText.Text = exists
                ? "Open Setup and press Install to build the Linux runtime from your own dump."
                : "No game dump is configured. Extract your own PPSA21567 dump and select the PPSA21567-app folder in Setup.";
        }
        else
        {
            NoticeCard.IsVisible = false;
        }

        PlayButton.IsEnabled = ready && !_backend.GameRunning;
        StopButton.IsEnabled = _backend.GameRunning;
    }

    private void Play(object? sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_settings.GamePath))
        {
            AppendLog("Set the extracted PPSA21567-app folder on the Setup page first.");
            return;
        }
        if (!_backend.HybridReady)
        {
            AppendLog("The hybrid runtime is not installed. Use the Setup page first.");
            return;
        }

        try
        {
            _backend.StartGame(_settings);
            AppendLog("Launching AstroBot...");
            PlayButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            AppendLog($"Launch failed: {exception.Message}");
        }
    }

    private void Stop(object? sender, RoutedEventArgs e)
    {
        _backend.StopGame();
        AppendLog("Stop requested.");
    }

    private void UpdateRunning(bool running)
    {
        PlayButton.IsEnabled = !running && _backend.HybridReady;
        StopButton.IsEnabled = running;
        if (!running)
        {
            AppendLog("Game process exited.");
        }
    }

    private void AppendLog(string line)
    {
        LogBox.Text = (LogBox.Text ?? "") + line + Environment.NewLine;
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void ClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = "";
}
