using System;
using System.Text.Json.Serialization;

namespace AstroHybrid.App.Models
{
    public class AppSettings
    {
        [JsonPropertyName("lighting")]
        public int Lighting { get; set; } = 0;

        [JsonPropertyName("fps")]
        public int Fps { get; set; } = 0;

        [JsonPropertyName("fpsv")]
        public int FpsVersion { get; set; } = 2;

        [JsonPropertyName("size")]
        public int Size { get; set; } = 2;

        [JsonPropertyName("fullscreen")]
        public bool Fullscreen { get; set; } = true;

        [JsonPropertyName("realspeed")]
        public bool RealSpeed { get; set; } = true;

        [JsonPropertyName("speed")]
        public int Speed { get; set; } = 0;

        [JsonPropertyName("overlay")]
        public int Overlay { get; set; } = 0;

        [JsonPropertyName("upscale")]
        public int Upscale { get; set; } = 0;

        [JsonPropertyName("fgmode")]
        public int FrameGenMode { get; set; } = 0;

        [JsonPropertyName("lowlatency")]
        public bool LowLatency { get; set; } = false;

        [JsonPropertyName("fgtarget")]
        public int FrameGenTarget { get; set; } = 2;

        [JsonPropertyName("pad")]
        public int Controller { get; set; } = 0;

        [JsonPropertyName("rumble")]
        public int Rumble { get; set; } = 2;

        [JsonPropertyName("switchboost")]
        public bool SwitchBoost { get; set; } = true;

        // Installation settings
        [JsonPropertyName("installPath")]
        public string? InstallPath { get; set; }

        [JsonPropertyName("gameDumpPath")]
        public string? GameDumpPath { get; set; }

        [JsonPropertyName("gameVersion")]
        public string? GameVersion { get; set; }

        [JsonPropertyName("gameTitleId")]
        public string? GameTitleId { get; set; }

        [JsonPropertyName("installationManifest")]
        public InstallationManifest? InstallationManifest { get; set; }

        // First run tracking
        // Runtime options (Advanced page), passed to Run-AstroHybrid.bat; defaults are the script's own.
        [JsonPropertyName("hangWatchdog")]
        public bool HangWatchdog { get; set; } = false;

        [JsonPropertyName("shaderCache")]
        public bool ShaderCache { get; set; } = true;

        [JsonPropertyName("drawPrepTuning")]
        public bool DrawPrepTuning { get; set; } = true;

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = "abovenormal";

        [JsonPropertyName("cpPriority")]
        public int CpPriority { get; set; } = 2;

        [JsonPropertyName("padMotion")]
        public bool MotionControls { get; set; } = true;

        [JsonPropertyName("triggerState")]
        public bool TriggerState { get; set; } = true;

        // Set by the setup wizard: the main window picks the best settings for this PC once.
        [JsonPropertyName("applyBestOnStart")]
        public bool ApplyBestOnStart { get; set; } = false;

        [JsonPropertyName("firstRunComplete")]
        public bool FirstRunComplete { get; set; } = false;

        [JsonPropertyName("lastLaunched")]
        public DateTime? LastLaunched { get; set; }
    }

    public class InstallationManifest
    {
        [JsonPropertyName("installedAt")]
        public DateTime InstalledAt { get; set; }

        [JsonPropertyName("gameVersion")]
        public string GameVersion { get; set; } = "";

        [JsonPropertyName("gameTitleId")]
        public string GameTitleId { get; set; } = "";

        [JsonPropertyName("anyps5Version")]
        public string AnyPs5Version { get; set; } = "";

        [JsonPropertyName("kytyVersion")]
        public string KytyVersion { get; set; } = "";

        [JsonPropertyName("installationPath")]
        public string InstallationPath { get; set; } = "";

        [JsonPropertyName("gameDumpPath")]
        public string GameDumpPath { get; set; } = "";

        [JsonPropertyName("files")]
        public string[] Files { get; set; } = Array.Empty<string>();
    }
}