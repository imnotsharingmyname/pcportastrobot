using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;
using AstroHybrid.App.Avalonia.Views;

namespace AstroHybrid.App.Avalonia;

public partial class MainWindow : Window
{
    // Window geometry of the WPF shell at f59fb37. Above 100% scaling the
    // work area is smaller than these values, so both the initial size and the
    // minimum are clamped: the bottom bar must stay on screen.
    private const double DesignWidth = 1100;
    private const double DesignHeight = 720;
    private const double DesignMinWidth = 900;
    private const double DesignMinHeight = 600;
    private const double WorkAreaMargin = 80;

    private readonly Backend _backend;
    private readonly UpdateService _updates = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, RadioButton> _nav;
    private readonly Dictionary<string, UserControl> _pages = new();
    private string _current = "Home";
    private bool _firstRunChecked;
    private UpdateInfo? _update;

    public MainWindow()
    {
        InitializeComponent();

        _backend = new Backend();
        _settings = _backend.LoadSettings();
        if (string.IsNullOrWhiteSpace(_settings.GamePath) && Directory.Exists(_backend.DefaultGamePath))
        {
            _settings.GamePath = _backend.DefaultGamePath;
        }

        _backend.SetGamePath(_settings.GamePath);

        _nav = new Dictionary<string, RadioButton>
        {
            ["Home"] = NavHome,
            ["Graphics"] = NavGraphics,
            ["Controller"] = NavController,
            ["Advanced"] = NavAdvanced,
            ["Installation"] = NavInstallation,
        };

        _backend.StatusChanged += OnBackendStateChanged;
        _backend.GameRunningChanged += OnGameRunningChanged;
        Closing += OnClosing;

        ApplyWorkAreaClamp();
        Select("Home");
        SetInitialStatus();
        RefreshChrome();
        _ = CheckUpdatesAsync();
    }

    /// <summary>
    /// Mirrors the Windows launcher: read the release feed in the background and
    /// offer the update only when it carries a Linux package. An offline or
    /// failed check leaves the launcher on the BUILD badge.
    /// </summary>
    private async Task CheckUpdatesAsync()
    {
        UpdateInfo? info = null;
        try
        {
            info = await _updates.CheckAsync();
        }
        catch
        {
            // No network, no update; the BUILD badge stays.
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _update = info;
            UpdateButton.IsVisible = info is not null;
            if (_pages.TryGetValue("Home", out var page) && page is HomeView home)
            {
                home.ShowUpdate(info);
            }
        });
    }

    private async void InstallUpdate(object? sender, RoutedEventArgs e)
    {
        if (_update is null)
        {
            return;
        }

        UpdateButton.IsEnabled = false;
        var ok = await _updates.DownloadAndApplyAsync(_update,
            (progress, status) => Dispatcher.UIThread.Post(() => _backend.SetStatus(status, progress < 1)));
        UpdateButton.IsEnabled = true;
        if (ok)
        {
            UpdateButton.IsVisible = false;
            _backend.SetStatus("Update installed — restart the launcher");
        }
    }

    /// <summary>
    /// D2a: every window is sized against the work area, so 150% and 200%
    /// scaling cannot push the bottom bar off screen. At 100% on a normal
    /// display the design values are used unchanged.
    /// </summary>
    private void ApplyWorkAreaClamp()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var available = screen.WorkingArea.Size.ToSize(1 / screen.Scaling);
        var maxWidth = Math.Max(360, available.Width - WorkAreaMargin);
        var maxHeight = Math.Max(320, available.Height - WorkAreaMargin);

        Width = Math.Min(DesignWidth, maxWidth);
        Height = Math.Min(DesignHeight, maxHeight);
        MinWidth = Math.Min(DesignMinWidth, maxWidth);
        MinHeight = Math.Min(DesignMinHeight, maxHeight);
    }

    private UserControl GetPage(string key) => key switch
    {
        // Pages are built on first visit; the constructor only builds Home.
        "Graphics" => _pages.TryGetValue("Graphics", out var graphics) ? graphics : new GraphicsView(_backend, _settings),
        "Controller" => _pages.TryGetValue("Controller", out var controller) ? controller : new ControllerView(_backend, _settings),
        "Advanced" => _pages.TryGetValue("Advanced", out var advanced) ? advanced : new AdvancedView(_backend, _settings),
        "Installation" => _pages.TryGetValue("Installation", out var installation) ? installation : new InstallationView(_backend, _settings),
        _ => _pages.TryGetValue("Home", out var home)
            ? home
            : new HomeView(_backend, _settings, Select),
    };

    private void Select(string key)
    {
        if (!_nav.TryGetValue(key, out var nav))
        {
            return;
        }

        if (!_pages.TryGetValue(key, out var page))
        {
            page = GetPage(key);
            _pages[key] = page;
        }

        _current = key;
        PageHost.Content = page;
        nav.IsChecked = true;

        if (page is IPageRefresh refresh)
        {
            refresh.RefreshPage();
        }

        BestSettingsButton.IsVisible = key == "Graphics";
        RefreshChrome();
    }

    private void SetInitialStatus()
    {
        if (_backend.HybridReady && Directory.Exists(_settings.GamePath))
        {
            _backend.SetStatus("Ready");
        }
        else if (!Directory.Exists(_settings.GamePath))
        {
            _backend.SetStatus("Game dump not found");
        }
        else
        {
            _backend.SetStatus("Setup required");
        }
    }

    private void OnBackendStateChanged() => Dispatcher.UIThread.Post(RefreshChrome);

    private void OnGameRunningChanged(bool running) => Dispatcher.UIThread.Post(RefreshChrome);

    private void RefreshChrome()
    {
        StatusText.Text = _backend.StatusMessage;
        StatusProgress.IsVisible = _backend.IsBusy;

        var ready = _backend.HybridReady && Directory.Exists(_settings.GamePath);
        PlayButton.IsEnabled = ready && !_backend.GameRunning && !_backend.IsBusy;
        StopButton.IsVisible = _backend.GameRunning;
    }

    private void ShowHome(object? sender, RoutedEventArgs e) => Select("Home");
    private void ShowGraphics(object? sender, RoutedEventArgs e) => Select("Graphics");
    private void ShowController(object? sender, RoutedEventArgs e) => Select("Controller");
    private void ShowAdvanced(object? sender, RoutedEventArgs e) => Select("Advanced");
    private void ShowInstallation(object? sender, RoutedEventArgs e) => Select("Installation");

    private void PlayGame(object? sender, RoutedEventArgs e)
    {
        _backend.StartGame(_settings);
        RefreshChrome();
    }

    private void StopGame(object? sender, RoutedEventArgs e)
    {
        _backend.StopGame();
        RefreshChrome();
    }

    private void OpenReleasePage(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open")
            {
                ArgumentList =
                {
                    "https://github.com/mitch030504/pcportastrobot-linux/releases",
                },
                UseShellExecute = false,
            });
        }
        catch
        {
            _backend.SetStatus("Could not open the release page");
        }
    }

    private void ApplyBestSettings(object? sender, RoutedEventArgs e)
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            _backend.SetStatus("No display detected");
            return;
        }

        var dip = screen.WorkingArea.Size.ToSize(1 / screen.Scaling);
        var (width, height) = GraphicsView.PickResolution(dip);

        _settings.Width = width;
        _settings.Height = height;
        _settings.FpsLimit = 60;
        _settings.GameSpeed = true;
        _settings.Fullscreen = width >= (int)dip.Width && height >= (int)dip.Height;
        _backend.SaveSettings(_settings);

        if (_pages.TryGetValue("Graphics", out var page) && page is GraphicsView graphics)
        {
            graphics.RefreshPage();
        }

        _backend.SetStatus("Applied best settings for this PC");
        RefreshChrome();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_firstRunChecked)
        {
            return;
        }

        _firstRunChecked = true;
        if (!_backend.HybridReady)
        {
            _ = RunFirstRunSetupAsync();
        }
    }

    /// <summary>
    /// Mirrors App.xaml.cs: when no hybrid runtime exists, the setup surface is
    /// shown before the shell, so Play is never visible-disabled with no route.
    /// Cancelling with a dump present opens the shell; without one the app exits.
    /// </summary>
    private async Task RunFirstRunSetupAsync()
    {
        Hide();
        var setup = new SetupWindow(_backend, _settings);
        var closed = new TaskCompletionSource();
        setup.Closed += (_, _) => closed.TrySetResult();
        setup.Show();
        await closed.Task;

        if (_backend.HybridReady || Directory.Exists(_settings.GamePath))
        {
            Show();
            Select("Home");
            SetInitialStatus();
        }
        else
        {
            Close();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _backend.StatusChanged -= OnBackendStateChanged;
        _backend.GameRunningChanged -= OnGameRunningChanged;

        // Closing the launcher leaves a running game alone: the child is not
        // killed, so a session in progress is not lost.
    }
}
