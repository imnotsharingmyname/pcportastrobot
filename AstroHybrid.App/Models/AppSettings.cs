using System;
using System.Text.Json.Serialization;
using AstroHybrid.App.Services;

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

        // Use the game's ultrawide render-space patch when the display is 21:9 or 32:9.
        [JsonPropertyName("ultrawide")]
        public bool Ultrawide { get; set; } = false;

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

        // "Auto" on the Graphics page: the launcher picks upscaling / frame generation for the selected card
        // each start (Upscale and FrameGenMode then hold the resolved choice). False in older settings files,
        // so a choice made before 1.2.8 is kept; setup, Best Settings and Restore Defaults turn them on.
        // DLSS quality mode (#156): 0 Quality (default), 1 Balanced, 2 Performance, 3 DLAA. Passed to the renderer
        // as ASTROHYBRID_DLSS_QUALITY; the game's own dynamic resolution still picks the input size.
        [JsonPropertyName("dlssQuality")]
        public int DlssQuality { get; set; } = 0;

        [JsonPropertyName("upscaleAuto")]
        public bool UpscaleAuto { get; set; } = false;

        [JsonPropertyName("fgAuto")]
        public bool FrameGenAuto { get; set; } = false;

        [JsonPropertyName("pad")]
        public int Controller { get; set; } = 0;

        // Preset index used by older builds: 0 = Off, 1 = 40, 2 = 70, 3 = 100, 4 = 150 (percent).
        [JsonPropertyName("rumble")]
        public int Rumble { get; set; } = 2;

        // Vibration strength in percent (0-200); -1 when the file predates it and only "rumble" is known.
        [JsonPropertyName("rumblepct")]
        public int RumblePercent { get; set; } = -1;

        [JsonPropertyName("switchboost")]
        public bool SwitchBoost { get; set; } = true;

        // Xbox/generic pads: View/Back tilts with the right stick while held (false, the default since
        // 1.2.4, #128) or toggles tilt on and off (true, the behaviour before 1.2.4, #146).
        [JsonPropertyName("tiltToggle")]
        public bool TiltToggle { get; set; } = false;

        // Right-stick tilt strength in percent of the default 45 degrees at full deflection (25-200, #146).
        [JsonPropertyName("tiltStrength")]
        public int TiltStrength { get; set; } = 100;

        // DualSense speaker: the sounds the game plays on the controller's own speaker (#143). Off keeps
        // them silent (not moved to the PC speakers).
        [JsonPropertyName("dsSpeaker")]
        public bool DualSenseSpeaker { get; set; } = true;

        // DualSense adaptive triggers: resistance and vibration in percent of what the game asks for
        // (0 = off, 100 = as the game asks; #160).
        [JsonPropertyName("triggerStrength")]
        public int TriggerStrength { get; set; } = 100;

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
        // Game language: -1 = follow Windows, else the console language number (ANYPS5_LANGUAGE).
        [JsonPropertyName("language")]
        public int Language { get; set; } = -1;

        // VSync (Vulkan FIFO presentation) instead of immediate presentation: no tearing.
        [JsonPropertyName("vsync")]
        public bool VSync { get; set; } = false;

        // Graphics card the launcher describes and hands to the game: index into the detected
        // adapters, -1 = automatic (the best card, discrete over integrated) - issue #24.
        [JsonPropertyName("gpu")]
        public int Gpu { get; set; } = -1;

        // Conservative renderer configuration and extra diagnostics. Missing values in older settings
        // files retain this initializer, so Stability Mode is enabled on upgrade unless explicitly opted out.
        [JsonPropertyName("stabilityMode")]
        public bool StabilityMode { get; set; } = true;

        // Experimental worker command-buffer size. Off preserves the 16 MiB setting introduced by 1.2.7.
        [JsonPropertyName("commandBuffer24MiB")]
        public bool CommandBuffer24MiB { get; set; } = false;

        // Runtime options (Advanced page), passed to Run-AstroHybrid.bat; defaults are the script's own.
        [JsonPropertyName("hangWatchdog")]
        public bool HangWatchdog { get; set; } = false;

        // Adds Vulkan device-fault diagnostics and GPU breadcrumbs to crash logs. Keep off for normal play;
        // these signals are intended for reproducing ErrorDeviceLost/TDR failures.
        [JsonPropertyName("gpuFaultDiagnostics")]
        public bool GpuFaultDiagnostics { get; set; } = false;

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

        [JsonPropertyName("skipIntro")]
        public bool SkipIntro { get; set; } = false;

        // Set by the setup wizard: the main window picks the best settings for this PC once.
        [JsonPropertyName("applyBestOnStart")]
        public bool ApplyBestOnStart { get; set; } = false;

        [JsonPropertyName("firstRunComplete")]
        public bool FirstRunComplete { get; set; } = false;

        [JsonPropertyName("lastLaunched")]
        public DateTime? LastLaunched { get; set; }

        // Update settings
        [JsonPropertyName("lastUpdateCheck")]
        public DateTime? LastUpdateCheck { get; set; }

        [JsonPropertyName("ignoredUpdateVersion")]
        public string? IgnoredUpdateVersion { get; set; }

        [JsonPropertyName("autoCheckUpdates")]
        public bool AutoCheckUpdates { get; set; } = true;

        // Vibration strength the game gets: the stored percent, or the old preset index mapped to percent.
        [JsonIgnore]
        public int EffectiveRumblePercent => RumbleSetting.EffectivePercent(RumblePercent, Rumble);

        // Writes both fields after loading, so an older launcher and this build read the same strength.
        public void NormalizeRumble()
        {
            RumblePercent = EffectiveRumblePercent;
            Rumble = RumbleSetting.ToLegacyIndex(RumblePercent);
        }

        // Clamps out-of-range indices, syncs Ultrawide with 21:9/32:9 size presets, and turns off
        // hardware-gated NVIDIA options on systems that do not support them.
        public static readonly (int Width, int Height, string Label, string ShortLabel, int AspectRatio)[] ResolutionPresets =
        {
            (1280, 720,  "1280 x 720",         "1280x720",  16),
            (1600, 900,  "1600 x 900",         "1600x900",  16),
            (1920, 1080, "1920 x 1080",        "1920x1080", 16),
            (2560, 1440, "2560 x 1440",        "2560x1440", 16),
            (2560, 1080, "2560 x 1080 (21:9)", "2560x1080", 21),
            (3440, 1440, "3440 x 1440 (21:9)", "3440x1440", 21),
            (5120, 1440, "5120 x 1440 (32:9)", "5120x1440", 32),
            (3840, 1080, "3840 x 1080 (32:9)", "3840x1080", 32),
            (5120, 2160, "5120 x 2160 (21:9)", "5120x2160", 21),
            (3840, 2160, "3840 x 2160 (4K)",   "3840x2160", 16),
        };

        public static int GetAspectRatio(int size) =>
            size >= 0 && size < ResolutionPresets.Length ? ResolutionPresets[size].AspectRatio : 16;

        [JsonIgnore]
        public bool IsUltrawideSize => GetAspectRatio(Size) != 16;

        [JsonIgnore]
        public bool IsSuperUltrawideSize => GetAspectRatio(Size) == 32;

        public void Sanitize(bool supportsDlss = true, bool supportsFrameGeneration = true, bool supportsReflex = true, int maxFrameGenMultiplier = 4)
        {
            if (Lighting < 0 || Lighting > 1) Lighting = 0;
            if (Fps < 0 || Fps > 6) Fps = 0;
            if (Speed < 0 || Speed > 5) Speed = 0;
            if (Size < 0 || Size >= ResolutionPresets.Length) Size = 2;
            if (Overlay < 0 || Overlay > 3) Overlay = 0;
            if (Upscale < 0 || Upscale > 2) Upscale = 0;   // 0 game's own, 1 NVIDIA DLSS, 2 AMD FSR
            if (FrameGenMode < 0 || FrameGenMode > 4) FrameGenMode = 0;
            if (FrameGenTarget < 0 || FrameGenTarget > 6) FrameGenTarget = 2;
            if (DlssQuality < 0 || DlssQuality > 3) DlssQuality = 0;
            if (Controller < 0 || Controller > 4) Controller = 0;
            if (CpPriority < 0 || CpPriority > 2) CpPriority = 2;
            if (Priority != "normal" && Priority != "abovenormal" && Priority != "high")
                Priority = "abovenormal";

            if (IsUltrawideSize) Ultrawide = true;
            else if (Ultrawide) Size = Size == 9 ? 8 : Size == 3 ? 5 : 4;

            if (!supportsDlss && Upscale == 1) Upscale = 0;
            // Frame generation is NVIDIA DLSS Frame Generation: it needs DLSS as the upscaler.
            if (!supportsFrameGeneration || Upscale != 1) FrameGenMode = 0;
            if (!supportsReflex) LowLatency = false;
            // Fixed 3x / 4x need a card that can generate that many frames (RTX 50 multi frame generation).
            if (FrameGenMode >= 3 && FrameGenMode > Math.Max(2, maxFrameGenMultiplier)) FrameGenMode = 2;
        }
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
