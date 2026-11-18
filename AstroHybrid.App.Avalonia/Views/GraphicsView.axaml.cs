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
    private static readonly (int Width, int Height)[] WidescreenResolutions =
    {
        (1280, 720), (1600, 900), (1920, 1080), (2560, 1440),
    };

    private static readonly string[] WidescreenLabels =
    {
        "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440",
    };

    private static readonly (int Width, int Height)[] UltrawideResolutions =
    {
        (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (2560, 1080), (3440, 1440),
    };

    private static readonly string[] UltrawideLabels =
    {
        "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440", "2560 x 1080 (21:9)", "3440 x 1440 (21:9)",
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

        FpsBox.ItemsSource = FpsLabels;
        OverlayBox.ItemsSource = OverlayLabels;

        // Windows-only rows keep the WPF labels and values so the page reads the
        // same; they are disabled and explained underneath.
        RayTracingBox.ItemsSource = new[]
        {
            "OFF - Faster lighting (Non-RT, recommended)", "ON - Ray Tracing (Original lighting)",
        };
        RayTracingBox.SelectedIndex = 0;
        UpscaleBox.ItemsSource = new[] { "Game's own (default)" };
        UpscaleBox.SelectedIndex = 0;
        FrameGenBox.ItemsSource = new[] { "Off (default)", "Auto - reach target (recommended)", "Fixed 2x", "Fixed 3x", "Fixed 4x" };
        FrameGenBox.SelectedIndex = 0;
        TargetFpsBox.ItemsSource = new[] { "No lock - game as fast as it can" };
        TargetFpsBox.SelectedIndex = 0;


        Loaded += (_, _) => RefreshPage();
    }

    public void RefreshPage()
    {
        _loading = true;

        var ultrawideAvailable = _backend.HasVariant("AstroBot-Ultrawide");
        var resolutions = ultrawideAvailable ? UltrawideResolutions : WidescreenResolutions;
        ResolutionBox.ItemsSource = ultrawideAvailable ? UltrawideLabels : WidescreenLabels;

        var devices = VulkanDevices.Enumerate();
        var gpuLabels = new List<string> { "Automatic - best graphics card" };
        foreach (var device in devices)
        {
            gpuLabels.Add(device.Name);
        }

        GpuBox.ItemsSource = gpuLabels;
        GpuBox.IsEnabled = devices.Count > 1;
        GpuBox.SelectedIndex = _settings.GpuIndex >= 0 && _settings.GpuIndex < devices.Count
            ? _settings.GpuIndex + 1
            : 0;
        GpuNote.Text = devices.Count > 1
            ? "The selected adapter is passed to the renderer; Automatic lets the driver choose."
            : "Only one Vulkan device was found, so there is nothing to choose.";

        UltrawideBox.IsEnabled = ultrawideAvailable;
        UltrawideNote.Text = ultrawideAvailable
            ? "Renders at the 21:9 aspect; 16:9 users keep the original framing."
            : "The 21:9 patch is not in this runtime; re-run setup to build it.";

        // Upscaling: the Windows row is DLSS, which needs the NVIDIA NGX runtime. On
        // Linux the same slot carries FSR 4.1.2 when the provider has been staged.
        var fsr4Ready = _backend.Fsr4ProviderReady;
        var fsr3Ready = _backend.Fsr3ProviderReady;
        var options = new List<string> { "Game's own temporal upscale (default)" };
        if (fsr3Ready) options.Add("FSR 3.1 - FidelityFX (experimental)");
        if (fsr4Ready) options.Add("FSR 4.1.2 - FidelityFX (experimental)");
        UpscaleBox.ItemsSource = options;
        UpscaleBox.SelectedIndex = _settings.Fsr switch
        {
            "fsr3" when fsr3Ready => options.IndexOf("FSR 3.1 - FidelityFX (experimental)"),
            "fsr4" when fsr4Ready => options.IndexOf("FSR 4.1.2 - FidelityFX (experimental)"),
            _ => 0,
        };
        UpscaleBox.IsEnabled = fsr3Ready || fsr4Ready;
        UpscaleNote.Text = (fsr3Ready || fsr4Ready)
            ? "FSR 4.1.2 replaces the game's final upscale with the FidelityFX provider. The game's dynamic resolution still picks the input size, so the gain depends on the scene."
            : "DLSS needs the NVIDIA NGX runtime, which is Windows-only. Run Tools/fsr4/build.sh to stage the FSR 4.1.2 provider and enable an upscaler here.";

        // Ray Tracing is the lighting build: 0 is the patched Non-RT executable,
        // 1 is the base executable with Kyty's software ray tracing.
        var lightingAvailable = _backend.HasVariant("AstroBot-NonRT");
        RayTracingBox.IsEnabled = lightingAvailable;
        RayTracingBox.SelectedIndex = _settings.Lighting == 1 ? 1 : 0;
        RayTracingNote.Text = !lightingAvailable
            ? "The lighting patches are not in this runtime; re-run setup to build AstroBot-NonRT."
            : _settings.Lighting == 1
                ? "ON: the base build. Kyty's software ray tracing runs the game's tiled lighting and GI passes (KYTY_RT_SOFTWARE)."
                : "OFF: AstroBot-NonRT, the faster lighting build. Switch to ON to play the original ray-traced lighting.";

        var index = Array.FindIndex(resolutions, r => r.Width == _settings.Width && r.Height == _settings.Height);
        ResolutionBox.SelectedIndex = index >= 0 ? index : 2;

        var fpsIndex = Array.IndexOf(FpsValues, _settings.FpsLimit);
        FpsBox.SelectedIndex = fpsIndex >= 0 ? fpsIndex : 5;

        var overlayIndex = Array.IndexOf(OverlayValues, _settings.OverlayMode ?? "");
        OverlayBox.SelectedIndex = overlayIndex >= 0 ? overlayIndex : 0;

        FullscreenBox.IsChecked = _settings.Fullscreen;
        UltrawideBox.IsChecked = _settings.Ultrawide && ultrawideAvailable;
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

        var resolutions = _backend.HasVariant("AstroBot-Ultrawide") ? UltrawideResolutions : WidescreenResolutions;
        var resolution = ResolutionBox.SelectedIndex >= 0
            ? resolutions[ResolutionBox.SelectedIndex]
            : (Width: 1920, Height: 1080);
        _settings.Width = resolution.Width;
        _settings.Height = resolution.Height;
        _settings.FpsLimit = FpsBox.SelectedIndex >= 0 ? FpsValues[FpsBox.SelectedIndex] : 60;
        _settings.Fullscreen = FullscreenBox.IsChecked == true;
        _settings.Ultrawide = UltrawideBox.IsChecked == true && UltrawideBox.IsEnabled;
        _settings.GpuIndex = GpuBox.SelectedIndex > 0 ? GpuBox.SelectedIndex - 1 : -1;
        _settings.VSync = VsyncBox.IsChecked == true;
        _settings.LowLatency = LowLatencyBox.IsChecked == true && LowLatencyBox.IsEnabled;
        _settings.Lighting = RayTracingBox.SelectedIndex == 1 ? 1 : 0;
        var chosen = UpscaleBox.IsEnabled && UpscaleBox.SelectedIndex > 0
            ? UpscaleBox.SelectedItem as string
            : null;
        _settings.Fsr = chosen is null ? ""
            : chosen.StartsWith("FSR 3.1") ? "fsr3"
            : chosen.StartsWith("FSR 4.1.2") ? "fsr4"
            : "";
        _settings.OverlayMode = OverlayBox.SelectedIndex >= 0 ? OverlayValues[OverlayBox.SelectedIndex] : "";

        RayTracingNote.Text = _settings.Lighting == 1
            ? "ON: the base build. Kyty's software ray tracing runs the game's tiled lighting and GI passes (KYTY_RT_SOFTWARE)."
            : "OFF: AstroBot-NonRT, the faster lighting build. Switch to ON to play the original ray-traced lighting.";

        _backend.SaveSettings(_settings);
        _backend.SetStatus("Settings saved");
    }

    /// <summary>Largest screen-safe 16:9 resolution, used by Best Settings.</summary>
    public static (int Width, int Height) PickResolution(Size available)
    {
        var bestWidth = 1280;
        var bestHeight = 720;
        foreach (var candidate in WidescreenResolutions)
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
