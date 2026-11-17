using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class GraphicsView : UserControl, IPageRefresh
{
    private static readonly (int Width, int Height)[] Resolutions =
    {
        (1280, 720), (1600, 900), (1920, 1080), (2560, 1440),
    };

    private static readonly string[] ResolutionLabels =
    {
        "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440",
    };

    private static readonly int[] FpsValues = { 30, 35, 45, 50, 55, 60, 0 };

    private static readonly string[] FpsLabels =
    {
        "30 FPS - steady (recommended)", "35 FPS", "45 FPS", "50 FPS", "55 FPS", "60 FPS", "No limit",
    };

    private static readonly string[] OverlayLabels =
    {
        "Off (F2 shows it)", "FPS only", "FPS + CPU, GPU, VRAM, RAM", "Everything (all stats)",
    };

    private static readonly string[] OverlayValues = { "", "fps", "compact", "full" };

    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private bool _loading = true;
    private bool? _lowLatencySupported;
    private bool _lowLatencyProbed;

    public GraphicsView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;

        ResolutionBox.ItemsSource = ResolutionLabels;
        FpsBox.ItemsSource = FpsLabels;
        OverlayBox.ItemsSource = OverlayLabels;

        // Windows-only rows keep the WPF labels and values so the page reads the
        // same; they are disabled and explained underneath.
        RayTracingBox.ItemsSource = new[]
        {
            "OFF - Faster lighting (Non-RT, recommended)", "ON - Ray Tracing (Original lighting)",
        };
        RayTracingBox.SelectedIndex = 0;
        UpscaleBox.ItemsSource = new[] { "Game's own (default)", "NVIDIA DLSS - sharper image" };
        UpscaleBox.SelectedIndex = 0;
        FrameGenBox.ItemsSource = new[] { "Off (default)", "Auto - reach target (recommended)", "Fixed 2x", "Fixed 3x", "Fixed 4x" };
        FrameGenBox.SelectedIndex = 0;
        TargetFpsBox.ItemsSource = new[] { "No lock - game as fast as it can" };
        TargetFpsBox.SelectedIndex = 0;

        var gpu = _backend.GpuName;
        GpuBox.ItemsSource = new[] { gpu.Length > 0 ? gpu : "Automatic - best graphics card" };
        GpuBox.SelectedIndex = 0;

        Loaded += (_, _) => RefreshPage();
    }

    public void RefreshPage()
    {
        _loading = true;

        var index = Array.FindIndex(Resolutions, r => r.Width == _settings.Width && r.Height == _settings.Height);
        ResolutionBox.SelectedIndex = index >= 0 ? index : 2;

        var fpsIndex = Array.IndexOf(FpsValues, _settings.FpsLimit);
        FpsBox.SelectedIndex = fpsIndex >= 0 ? fpsIndex : 5;

        var overlayIndex = Array.IndexOf(OverlayValues, _settings.OverlayMode ?? "");
        OverlayBox.SelectedIndex = overlayIndex >= 0 ? overlayIndex : 0;

        FullscreenBox.IsChecked = _settings.Fullscreen;
        VsyncBox.IsChecked = _settings.VSync;
        LowLatencyBox.IsChecked = _settings.LowLatency;

        HardwareText.Text = _backend.HardwareSummary();

        _loading = false;

        if (!_lowLatencyProbed)
        {
            // Until the probe answers, keep the row enabled: a missing vulkaninfo
            // is not evidence that the driver lacks the extension.
            ApplyLowLatencySupport(null);
            _ = ProbeLowLatencyAsync();
        }
        else
        {
            ApplyLowLatencySupport(_lowLatencySupported);
        }
    }

    private void ApplyLowLatencySupport(bool? supported)
    {
        // null means the probe could not run; the runtime decides in that case.
        LowLatencyBox.IsEnabled = supported != false;
        LowLatencyNote.IsVisible = supported != true;
        LowLatencyNote.Text = supported == false
            ? "Needs an NVIDIA driver exposing VK_NV_low_latency2 and VK_KHR_present_id; this system does not expose them."
            : "Applied only when the driver exposes VK_NV_low_latency2 and VK_KHR_present_id; the runtime logs the result.";
        if (supported == false && _settings.LowLatency)
        {
            _settings.LowLatency = false;
            _backend.SaveSettings(_settings);
            LowLatencyBox.IsChecked = false;
        }
    }

    /// <summary>
    /// Enables the low-latency row only when the driver exposes the extension the
    /// runtime asks for. vulkaninfo is read once and cached.
    /// </summary>
    private async Task ProbeLowLatencyAsync()
    {
        bool? supported;
        try
        {
            var start = new ProcessStartInfo("vulkaninfo")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start);
            if (process is null)
            {
                supported = null;
            }
            else
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                supported = output.Contains("VK_NV_low_latency2", StringComparison.Ordinal) &&
                            output.Contains("VK_KHR_present_id", StringComparison.Ordinal);
            }
        }
        catch
        {
            supported = null;
        }

        _lowLatencyProbed = true;
        _lowLatencySupported = supported;
        await Dispatcher.UIThread.InvokeAsync(() => ApplyLowLatencySupport(supported));
    }

    private void OnCommitted(object? sender, SelectionChangedEventArgs e) => Commit();

    private void OnCheckCommitted(object? sender, RoutedEventArgs e) => Commit();

    private void Commit()
    {
        if (_loading)
        {
            return;
        }

        var resolution = ResolutionBox.SelectedIndex >= 0
            ? Resolutions[ResolutionBox.SelectedIndex]
            : (Width: 1920, Height: 1080);
        _settings.Width = resolution.Width;
        _settings.Height = resolution.Height;
        _settings.FpsLimit = FpsBox.SelectedIndex >= 0 ? FpsValues[FpsBox.SelectedIndex] : 60;
        _settings.Fullscreen = FullscreenBox.IsChecked == true;
        _settings.VSync = VsyncBox.IsChecked == true;
        _settings.LowLatency = LowLatencyBox.IsChecked == true && LowLatencyBox.IsEnabled;
        _settings.OverlayMode = OverlayBox.SelectedIndex >= 0 ? OverlayValues[OverlayBox.SelectedIndex] : "";

        _backend.SaveSettings(_settings);
        _backend.SetStatus("Settings saved");
    }

    /// <summary>Largest screen-safe 16:9 resolution, used by Best Settings.</summary>
    public static (int Width, int Height) PickResolution(Size available)
    {
        var bestWidth = 1280;
        var bestHeight = 720;
        foreach (var candidate in Resolutions)
        {
            if (candidate.Item1 <= available.Width && candidate.Item2 <= available.Height)
            {
                bestWidth = candidate.Item1;
                bestHeight = candidate.Item2;
            }
        }

        return (bestWidth, bestHeight);
    }
}
