using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AstroHybrid.App.Avalonia.Services;
using AstroHybrid.App.Avalonia.Views;

namespace AstroHybrid.App.Avalonia;

public partial class SetupWindow : Window
{
    private const double DesignWidth = 720;
    private const double DesignHeight = 760;
    private const double DesignMinWidth = 620;
    private const double DesignMinHeight = 560;

    public SetupWindow(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        Host.Content = new SetupView(backend, settings);
        Opened += (_, _) => ClampToWorkArea();
    }

    /// <summary>
    /// D2a: the setup window is clamped to the work area too. WPF's 720x760 does
    /// not fit a 768-tall panel, and at 200% scaling it is taller than 1080p.
    /// </summary>
    private void ClampToWorkArea()
    {
        var screens = Screens;
        var screen = screens?.Primary ?? screens?.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        var available = screen.WorkingArea.Size.ToSize(1 / screen.Scaling);
        var maxWidth = Math.Max(360, available.Width - 80);
        var maxHeight = Math.Max(320, available.Height - 80);

        Width = Math.Min(DesignWidth, maxWidth);
        Height = Math.Min(DesignHeight, maxHeight);
        MinWidth = Math.Min(DesignMinWidth, maxWidth);
        MinHeight = Math.Min(DesignMinHeight, maxHeight);
    }

    private void Close(object? sender, RoutedEventArgs e) => Close();
}
