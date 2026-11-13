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
        public DateTime? LastValidated { get; set; }
    }

    public class HardwareInfo
    {
        public string GpuName { get; set; } = "Unknown GPU";
        public int RtxSeries { get; set; } = 0;
        public string CpuName { get; set; } = "Unknown CPU";
        public int CpuThreads { get; set; } = Environment.ProcessorCount;
        public int RamGb { get; set; } = 0;
        public int ScreenWidth { get; set; } = 1920;
        public int ScreenHeight { get; set; } = 1080;
        public int RefreshRate { get; set; } = 60;
        public bool SupportsDlss { get; set; } = false;
        public bool SupportsFrameGeneration { get; set; } = false;
        public bool SupportsReflex { get; set; } = false;
        public int MaxFrameGenMultiplier { get; set; } = 1;
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