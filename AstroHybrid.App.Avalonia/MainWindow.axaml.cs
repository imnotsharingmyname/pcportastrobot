using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;
using AstroHybrid.App.Avalonia.Views;

namespace AstroHybrid.App.Avalonia;

public partial class MainWindow : Window
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private readonly Dictionary<Button, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();

        _backend = new Backend();
        _settings = _backend.LoadSettings();
        if (string.IsNullOrWhiteSpace(_settings.GamePath) && Directory.Exists(_backend.DefaultGamePath))
        {
            _settings.GamePath = _backend.DefaultGamePath;
        }

        _pages[NavHome] = new HomeView(_backend, _settings, NavigateTo);
        _pages[NavGraphics] = new GraphicsView(_backend, _settings);
        _pages[NavController] = new ControllerView(_backend, _settings);
        _pages[NavAdvanced] = new AdvancedView(_backend, _settings);
        _pages[NavInstallation] = new SetupView(_backend, _settings);

        _backend.StatusChanged += OnBackendStateChanged;
        _backend.GameRunningChanged += OnGameRunningChanged;
        Closed += OnClosed;

        Select(NavHome);
        SetInitialStatus();
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

    private void NavigateTo(string key)
    {
        switch (key)
        {
            case "Graphics":
                Select(NavGraphics);
                break;
            case "Installation":
                Select(NavInstallation);
                break;
            case "Controller":
                Select(NavController);
                break;
            case "Advanced":
                Select(NavAdvanced);
                break;
            default:
                Select(NavHome);
                break;
        }
    }

    private void Select(Button nav)
    {
        if (!_pages.TryGetValue(nav, out var page))
        {
            return;
        }

        PageHost.Content = page;
        nav.Focus();
        foreach (var button in _pages.Keys)
        {
            button.Classes.Set("active", ReferenceEquals(button, nav));
        }

        BestSettingsButton.IsVisible = ReferenceEquals(nav, NavGraphics);

        if (page is IPageRefresh refresh)
        {
            refresh.RefreshPage();
        }

        RefreshChrome();
    }

    private void OnBackendStateChanged() =>
        Dispatcher.UIThread.Post(RefreshChrome);

    private void OnGameRunningChanged(bool running) =>
        Dispatcher.UIThread.Post(RefreshChrome);

    private void RefreshChrome()
    {
        StatusText.Text = _backend.StatusMessage;
        StatusProgress.IsVisible = _backend.IsBusy;

        var ready = _backend.HybridReady && Directory.Exists(_settings.GamePath);
        PlayButton.IsEnabled = ready && !_backend.GameRunning;
        StopButton.IsVisible = _backend.GameRunning;
    }

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

        if (_pages[NavGraphics] is GraphicsView graphics)
        {
            graphics.RefreshPage();
        }

        _backend.SetStatus("Applied best settings for this PC");
        RefreshChrome();
    }

    private void ShowHome(object? sender, RoutedEventArgs e) => Select(NavHome);
    private void ShowGraphics(object? sender, RoutedEventArgs e) => Select(NavGraphics);
    private void ShowController(object? sender, RoutedEventArgs e) => Select(NavController);
    private void ShowAdvanced(object? sender, RoutedEventArgs e) => Select(NavAdvanced);
    private void ShowInstallation(object? sender, RoutedEventArgs e) => Select(NavInstallation);

    private void OnClosed(object? sender, EventArgs e)
    {
        _backend.StatusChanged -= OnBackendStateChanged;
        _backend.GameRunningChanged -= OnGameRunningChanged;
    }
}
