using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class HomeView : UserControl, IPageRefresh
{
    private static readonly IBrush Success = Brush.Parse("#00D4AA");
    private static readonly IBrush Warning = Brush.Parse("#FFAA00");
    private static readonly IBrush Error = Brush.Parse("#FF4444");

    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private readonly Action<string> _navigate;

    public HomeView(Backend backend, AppSettings settings, Action<string> navigate)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        _navigate = navigate;

        _backend.GameOutput += OnGameOutput;
        _backend.GameRunningChanged += OnGameRunningChanged;
        Loaded += (_, _) => RefreshPage();
    }

    public void RefreshPage()
    {
        var exists = Directory.Exists(_settings.GamePath);
        var runtime = _backend.HybridReady;
        var ready = runtime && exists;

        HeroStatus.Text = ready
            ? "Ready to play"
            : exists
                ? "Runtime not built — open Installation"
                : "Game dump not found — open Installation";
        HeroStatus.Foreground = ready ? Success : exists ? Warning : Error;

        InstallValue.Text = ready ? "Ready" : exists ? "Setup required" : "Not found";
        RuntimeValue.Text = runtime ? "Hybrid runtime installed" : "Hybrid runtime missing";

        var hardware = _backend.HardwareSummary();
        var lines = hardware.Split('\n');
        var cpuLine = lines.Length > 0 ? lines[0] : "Unknown CPU";
        var cpuParts = cpuLine.Split('·');
        CpuValue.Text = cpuParts[0].Trim();
        GpuValue.Text = lines.FirstOrDefault(line => line.StartsWith("GPU:", StringComparison.Ordinal))
            ?.Substring(4).Trim() ?? "—";
        RamValue.Text = cpuParts.Length > 1 ? cpuParts[1].Trim() : "—";
        DisplayValue.Text = lines.FirstOrDefault(line => line.StartsWith("Display:", StringComparison.Ordinal))
            ?.Substring(8).Trim()
            ?? Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")
            ?? "—";

        var fps = _settings.FpsLimit > 0 ? $"{_settings.FpsLimit} FPS" : "Unlimited";
        ConfigText.Text =
            $"{_settings.Width} × {_settings.Height}   ·   {fps}   ·   " +
            $"{(_settings.Fullscreen ? "Fullscreen" : "Windowed")}   ·   " +
            $"Audio {(_settings.DirectAudio ? "direct" : "default")}";
    }

    private void OpenGraphics(object? sender, RoutedEventArgs e) => _navigate("Graphics");
    private void OpenInstallation(object? sender, RoutedEventArgs e) => _navigate("Installation");

    private void OnGameOutput(string line) =>
        Dispatcher.UIThread.Post(() => AppendLog(line));

    private void OnGameRunningChanged(bool running) =>
        Dispatcher.UIThread.Post(() =>
        {
            AppendLog(running ? "Game process started." : "Game process exited.");
            RefreshPage();
        });

    private void AppendLog(string line)
    {
        LogBox.Text = (LogBox.Text ?? "") + line + Environment.NewLine;
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void ClearLog(object? sender, RoutedEventArgs e) => LogBox.Text = "";
}
