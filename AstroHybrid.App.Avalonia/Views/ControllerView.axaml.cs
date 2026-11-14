using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class ControllerView : UserControl, IPageRefresh
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private bool _loading = true;

    public ControllerView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        MotionBox.IsChecked = settings.PadMotion;
        _loading = false;
        RefreshPads();
    }

    public void RefreshPage() => RefreshPads();

    private void RefreshPads()
    {
        var pads = _backend.DetectGamePads();
        PadList.ItemsSource = pads;
        EmptyText.IsVisible = pads.Count == 0;
    }

    private void Refresh(object? sender, RoutedEventArgs e) => RefreshPads();

    private void OnCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.PadMotion = MotionBox.IsChecked == true;
        _backend.SaveSettings(_settings);
        _backend.SetStatus($"Settings saved — motion relay {(_settings.PadMotion ? "on" : "off")}");
    }
}
