using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class HomeView : UserControl, IPageRefresh
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private readonly Action<string> _navigate;

    public HomeView(Backend backend, AppSettings settings, Action<string> navigate)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        _navigate = navigate;

        _backend.GameRunningChanged += OnGameRunningChanged;
        Loaded += (_, _) => RefreshPage();
    }

    public void RefreshPage()
    {
        var dumpExists = Directory.Exists(_settings.GamePath);
        var runtime = _backend.HybridReady;
        var ready = runtime && dumpExists;

        // Status copy matches the launcher's own wording, not a claim about updates.
        HeroStatus.Text = ready
            ? "Installed and Ready"
            : dumpExists
                ? "Setup required"
                : runtime
                    ? "Game dump not found"
                    : "Not Installed";
        HeroStatus.Foreground = ready
            ? Resource("SuccessBrush", "#00D4AA")
            : dumpExists
                ? Resource("WarningBrush", "#FFAA00")
                : Resource("ErrorBrush", "#FF4444");

        var info = _backend.GameInfo;
        var version = info.Version.Length > 0 ? info.Version : "—";
        var titleId = info.TitleId.Length > 0 ? info.TitleId : "PPSA21567";

        GameVersionText.Text = version;
        GameVersionValue.Text = version;
        TitleIdRun.Text = titleId;
        BuildText.Text = Backend.BuildSha;

        CpuValue.Text = _backend.CpuName;
        var gpu = _backend.GpuName;
        GpuValue.Text = gpu.Length > 0 ? gpu : "—";
        var memory = _backend.MemoryLabel;
        RamValue.Text = memory.Length > 0 ? memory : "—";        DisplayValue.Text = PrimaryDisplayLabel();

        ConfigText.Text =
            $"{_settings.Width} × {_settings.Height}   ·   " +
            $"{(_settings.Fullscreen ? "Fullscreen" : "Windowed")}   ·   " +
            $"VSync {(_settings.VSync ? "On" : "Off")}   ·   " +
            $"{(_settings.FpsLimit > 0 ? $"{_settings.FpsLimit} FPS" : "No limit")}   ·   " +
            $"Audio {(_settings.DirectAudio ? "direct" : "default")}";

        SetupActions.IsVisible = !ready;
        InstalledActions.IsVisible = ready;
        VsyncButton.Content = _settings.VSync ? "VSync: On" : "VSync: Off";
    }

    private string PrimaryDisplayLabel()
    {
        var screens = TopLevel.GetTopLevel(this)?.Screens;
        var screen = screens?.Primary ?? screens?.All.FirstOrDefault();
        if (screen is null)
        {
            return Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "—";
        }

        var dip = screen.Bounds.Size.ToSize(1 / screen.Scaling);
        return $"{dip.Width:0}×{dip.Height:0}";
    }

    private IBrush Resource(string key, string fallback) =>
        this.FindResource(key) as IBrush ?? Brush.Parse(fallback);

    private void OpenGraphics(object? sender, RoutedEventArgs e) => _navigate("Graphics");
    private void OpenController(object? sender, RoutedEventArgs e) => _navigate("Controller");
    private void OpenInstallation(object? sender, RoutedEventArgs e) => _navigate("Installation");

    private void ToggleVsync(object? sender, RoutedEventArgs e)
    {
        _settings.VSync = !_settings.VSync;
        _backend.SaveSettings(_settings);
        RefreshPage();
    }

    private void OnGameRunningChanged(bool running) =>
        Dispatcher.UIThread.Post(RefreshPage);
}
