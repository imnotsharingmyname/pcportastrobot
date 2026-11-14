using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class ControllerView : UserControl
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;

    public ControllerView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        MotionBox.IsChecked = settings.PadMotion;
        RefreshPads();
    }

    private void RefreshPads()
    {
        var pads = _backend.DetectGamePads();
        PadList.ItemsSource = pads;
        EmptyText.IsVisible = pads.Count == 0;
    }

    private void Refresh(object? sender, RoutedEventArgs e) => RefreshPads();

    private void Save(object? sender, RoutedEventArgs e)
    {
        _settings.PadMotion = MotionBox.IsChecked == true;
        _backend.SaveSettings(_settings);
        Status.Text = $"Saved. Motion relay {( _settings.PadMotion ? "on" : "off") }.";
    }
}
