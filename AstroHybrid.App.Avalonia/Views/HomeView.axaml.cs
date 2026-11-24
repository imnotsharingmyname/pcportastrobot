using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class HomeView : UserControl, IPageRefresh
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private readonly Action<string> _navigate;
    private readonly UpdateService _updates = new();
    private UpdateInfo? _update;
    private bool _updateDismissed;

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
        ApplyUpdate();

        CpuValue.Text = _backend.CpuName;
        var gpu = _backend.GpuName;
        GpuValue.Text = gpu.Length > 0 ? gpu : "—";
        var memory = _backend.MemoryLabel;
        RamValue.Text = memory.Length > 0 ? memory : "—";        DisplayValue.Text = PrimaryDisplayLabel();

        ConfigText.Text =
            $"{_settings.Width} × {_settings.Height}   ·   " +
            $"{(_settings.Fullscreen ? "Fullscreen" : "Windowed")}   ·   " +
            $"VSync {(_settings.VSync ? "On" : "Off")}   ·   " +
            $"{(_settings.Fsr.Length == 0 ? "Upscale off" : _settings.Fsr.ToUpperInvariant())}   ·   " +
            $"{(_settings.FpsLimit > 0 ? $"{_settings.FpsLimit} FPS" : "No limit")}   ·   " +
            $"Audio {(_settings.DirectAudio ? "direct" : "default")}";

        SetupActions.IsVisible = !ready;
        InstalledActions.IsVisible = ready;
        RtxButton.Content = _settings.Lighting == 1 ? "RTX: ON" : "RTX: OFF";
    }

    /// <summary>Called by the shell when the background check finds a release.</summary>
    public void ShowUpdate(UpdateInfo? info)
    {
        _update = info;
        _updateDismissed = false;
        ApplyUpdate();
    }

    private void ApplyUpdate()
    {
        var available = _update is not null && !_updateDismissed;
        AvailableBadge.IsVisible = _update is not null;
        BuildBadge.IsVisible = _update is null;
        if (_update is not null)
        {
            AvailableText.Text = _update.Version;
        }

        NewsCard.IsVisible = available;
        if (_update is not null)
        {
            NewsVersion.Text = _update.Version;
            NewsNotes.Text = string.IsNullOrWhiteSpace(_update.Notes)
                ? "No release notes were published."
                : _update.Notes.Trim();
        }
    }

    private void DismissUpdate(object? sender, RoutedEventArgs e)
    {
        _updateDismissed = true;
        ApplyUpdate();
    }

    private async void InstallUpdate(object? sender, RoutedEventArgs e)
    {
        if (_update is null)
        {
            return;
        }

        NewsUpdateButton.IsEnabled = false;
        var ok = await _updates.DownloadAndApplyAsync(_update,
            (progress, status) => Dispatcher.UIThread.Post(() => _backend.SetStatus(status, progress < 1)));
        NewsUpdateButton.IsEnabled = true;
        if (ok)
        {
            _backend.SetStatus("Update installed — restart the launcher");
        }
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

    /// <summary>
    /// Same quick action as the reference: the switch picks between the base build
    /// (ray-traced lighting) and AstroBot-NonRT (faster lighting).
    /// </summary>
    private void ToggleRayTracing(object? sender, RoutedEventArgs e)
    {
        _settings.Lighting = _settings.Lighting == 1 ? 0 : 1;
        _backend.SaveSettings(_settings);
        RefreshPage();
    }

    private void OnGameRunningChanged(bool running) =>
        Dispatcher.UIThread.Post(RefreshPage);
}
