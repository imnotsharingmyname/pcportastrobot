using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;
using AstroHybrid.App.Avalonia.Views;

namespace AstroHybrid.App.Avalonia;

public partial class MainWindow : Window
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private readonly Dictionary<Button, UserControl> _pages = new();
    private readonly Dictionary<Button, Control> _nav = new();

    public MainWindow()
    {
        InitializeComponent();
        _backend = new Backend();
        _settings = _backend.LoadSettings();
        if (string.IsNullOrWhiteSpace(_settings.GamePath) && Directory.Exists(_backend.DefaultGamePath))
        {
            _settings.GamePath = _backend.DefaultGamePath;
        }

        _pages[NavHome] = new HomeView(_backend, _settings);
        _pages[NavSetup] = new SetupView(_backend, _settings);
        _pages[NavGraphics] = new GraphicsView(_backend, _settings);
        _pages[NavController] = new ControllerView(_backend, _settings);
        _pages[NavAdvanced] = new AdvancedView(_backend, _settings);

        _nav[NavHome] = NavHome;
        _nav[NavSetup] = NavSetup;
        _nav[NavGraphics] = NavGraphics;
        _nav[NavController] = NavController;
        _nav[NavAdvanced] = NavAdvanced;

        ShowHome(this, new RoutedEventArgs());
        BuildLabel.Text = $"Linux native port · root {System.IO.Path.GetFileName(_backend.Root)}";
    }

    private void Select(Button button)
    {
        PageHost.Content = _pages[button];
        foreach (var pair in _nav)
        {
            pair.Value.Classes.Set("active", pair.Key == button);
        }
    }

    private void ShowHome(object? sender, RoutedEventArgs e) => Select(NavHome);
    private void ShowSetup(object? sender, RoutedEventArgs e) => Select(NavSetup);
    private void ShowGraphics(object? sender, RoutedEventArgs e) => Select(NavGraphics);
    private void ShowController(object? sender, RoutedEventArgs e) => Select(NavController);
    private void ShowAdvanced(object? sender, RoutedEventArgs e) => Select(NavAdvanced);
}
