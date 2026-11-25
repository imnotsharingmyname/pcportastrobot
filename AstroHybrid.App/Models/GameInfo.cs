using System;

namespace AstroHybrid.App.Models
{
    public class GameInfo
    {
        public string Path { get; set; } = "";
        public string EbootPath { get; set; } = "";
        public long EbootSize { get; set; }
        public string TitleId { get; set; } = "";
        public string Version { get; set; } = "";
        public bool IsValid { get; set; }
        public string? ValidationError { get; set; }
        // A valid dump that is known to have problems (for example a game version that crashes): shown to the
        // player before setup, never a refusal.
        public string? Warning { get; set; }
        public DateTime? LastValidated { get; set; }
    }

    // One graphics adapter as Windows reports it. Laptops with hybrid graphics list two: the
    // processor's integrated GPU (which drives the built-in screen) and the discrete card, so the
    // launcher must not assume the first one is the card the game renders on (issue #24).
    public class GpuAdapter
    {
        public string Name { get; set; } = "Unknown GPU";
        public string Vendor { get; set; } = "";
        public string DriverVersion { get; set; } = "";
        public string PnpDeviceId { get; set; } = "";
        public bool IsDiscrete { get; set; } = false;
        public bool IsIntegrated { get; set; } = false;
        public bool IsPrimaryDisplay { get; set; } = false;
        public int RtxSeries { get; set; } = 0;
        public bool SupportsDlss { get; set; } = false;
        public bool SupportsFrameGeneration { get; set; } = false;
        public bool SupportsReflex { get; set; } = false;
        public int MaxFrameGenMultiplier { get; set; } = 1;
        // Dedicated video memory in MiB from the display driver's registry entry; 0 when unknown.
        public int VramMb { get; set; } = 0;
        // Intel XeSS Super Resolution (ASTROHYBRID_XESS, AppSettings.Upscale = 3): any GPU with DP4a -
        // Intel Arc and Iris Xe, NVIDIA GTX 10 and newer, AMD RDNA. The renderer keeps the game's own
        // upscaling by itself when XeSS cannot start, so this only decides what the launcher offers.
        public bool SupportsXess { get; set; } = false;
        // Intel Arc (Xe-HPG, Xe2, Xe3) runs XeSS on its XMX matrix engines: the best image and speed.
        public bool XessXmx { get; set; } = false;

        public string KindLabel => IsDiscrete ? "discrete" : IsIntegrated ? "integrated" : "graphics";

        public string Label => $"{Name} ({KindLabel}){(IsPrimaryDisplay ? ", drives the screen" : "")}";
    }

    public class HardwareInfo
    {
        // Every real adapter, in the order Windows reports them. Empty when enumeration fails.
        public System.Collections.Generic.List<GpuAdapter> Gpus { get; set; } = new();

        // Index into Gpus, or -1 for automatic (the best card: discrete, then NVIDIA RTX).
        public int SelectedGpuIndex { get; set; } = -1;

        public void SelectGpu(int index)
        {
            if (index >= -1 && index < Gpus.Count) SelectedGpuIndex = index;
        }

        // The card the launcher's features describe and the game is expected to render on.
        public GpuAdapter? SelectedGpu
        {
            get
            {
                if (Gpus.Count == 0) return null;
                if (SelectedGpuIndex >= 0 && SelectedGpuIndex < Gpus.Count) return Gpus[SelectedGpuIndex];
                // Automatic mirrors what the renderer does: a discrete card wins over integrated
                // graphics; an RTX card wins over other discrete cards.
                foreach (var gpu in Gpus)
                    if (gpu.IsDiscrete && gpu.RtxSeries >= 20) return gpu;
                foreach (var gpu in Gpus)
                    if (gpu.IsDiscrete) return gpu;
                foreach (var gpu in Gpus)
                    if (gpu.IsPrimaryDisplay) return gpu;
                return Gpus[0];
            }
        }

        public string GpuName => SelectedGpu?.Name ?? "Unknown GPU";
        public int RtxSeries => SelectedGpu?.RtxSeries ?? 0;
        public bool SupportsDlss => SelectedGpu?.SupportsDlss == true;
        public bool SupportsFrameGeneration => SelectedGpu?.SupportsFrameGeneration == true;
        public bool SupportsReflex => SelectedGpu?.SupportsReflex == true;
        public int MaxFrameGenMultiplier => SelectedGpu?.MaxFrameGenMultiplier ?? 1;
        public bool SupportsXess => SelectedGpu?.SupportsXess == true;
        public bool XessXmx => SelectedGpu?.XessXmx == true;
        public string GpuVendor => SelectedGpu?.Vendor ?? "";

        public string CpuName { get; set; } = "Unknown CPU";
        public int CpuThreads { get; set; } = Environment.ProcessorCount;
        public int RamGb { get; set; } = 0;
        public int ScreenWidth { get; set; } = 1920;
        public int ScreenHeight { get; set; } = 1080;
        public int RefreshRate { get; set; } = 60;
    }

    public class ControllerInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = ""; // "DualSense", "Xbox", "Switch", "Generic"
        public string InstanceId { get; set; } = "";
        public bool HasMotionSensors { get; set; } = false;
        public bool HasHaptics { get; set; } = false;
        public bool HasTriggerEffects { get; set; } = false;
        public int BatteryLevel { get; set; } = -1;
        public bool IsConnected { get; set; } = true;
    }

    public class InstallationState
    {
        public bool IsInstalled { get; set; } = false;
        public string? InstallPath { get; set; }
        public string? GameDumpPath { get; set; }
        public string? GameVersion { get; set; }
        public string? GameTitleId { get; set; }
        public DateTime? InstalledAt { get; set; }
        public InstallationManifest? Manifest { get; set; }
        public bool IsValid { get; set; } = false;
        public string? ValidationError { get; set; }
        public long InstallSizeBytes { get; set; }
    }

    public class SetupStep
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsComplete { get; set; } = false;
        public bool HasError { get; set; } = false;
        public string? ErrorMessage { get; set; }
        public int ProgressPercent { get; set; } = 0;
    }
}