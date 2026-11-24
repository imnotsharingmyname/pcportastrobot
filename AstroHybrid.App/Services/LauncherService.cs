using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class LauncherService
    {
        private readonly string _rootDirectory;
        private readonly SettingsManager _settingsManager;

        public LauncherService(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            _settingsManager = new SettingsManager(rootDirectory);
        }

        private void AppendDebugLog(string message)
        {
            try
            {
                var logPath = Path.Combine(_rootDirectory, "Logs", "astrohybrid-debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        public async Task<int> LaunchGameAsync(AppSettings settings, HardwareInfo? hardware = null)
        {
            var batPath = Path.Combine(_rootDirectory, "Run-AstroHybrid.bat");
            if (!File.Exists(batPath))
            {
                throw new FileNotFoundException("Run-AstroHybrid.bat not found", batPath);
            }

            var runHybridPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            if (!File.Exists(Path.Combine(runHybridPath, "AstroBot.exe")))
            {
                throw new FileNotFoundException("AstroBot.exe not found in RunHybrid", runHybridPath);
            }

            // Save settings before launch
            settings.LastLaunched = DateTime.Now;
            _settingsManager.Save(settings);

            // Build environment variables from settings
            var envVars = BuildEnvironmentVariables(settings, hardware);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                WorkingDirectory = _rootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var kvp in envVars)
            {
                psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>();

            process.Exited += (sender, e) =>
            {
                tcs.SetResult(process.ExitCode);
            };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[OUT] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[ERR] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return await tcs.Task;
        }

        public async Task<int> LaunchGameWithDlssAsync(AppSettings settings, HardwareInfo? hardware = null)
        {
            var batPath = Path.Combine(_rootDirectory, "Run-AstroHybrid-DLSS.bat");
            if (!File.Exists(batPath))
            {
                throw new FileNotFoundException("Run-AstroHybrid-DLSS.bat not found", batPath);
            }

            var runHybridPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            if (!File.Exists(Path.Combine(runHybridPath, "AstroBot.exe")))
            {
                throw new FileNotFoundException("AstroBot.exe not found in RunHybrid", runHybridPath);
            }

            settings.LastLaunched = DateTime.Now;
            _settingsManager.Save(settings);

            var envVars = BuildEnvironmentVariables(settings, hardware);
            // DLSS-specific overrides
            envVars["ASTROHYBRID_DLSS"] = "1";
            envVars["ASTROHYBRID_CUDA_HOLE"] = "1";

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                WorkingDirectory = _rootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var kvp in envVars)
            {
                psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>();

            process.Exited += (sender, e) =>
            {
                tcs.SetResult(process.ExitCode);
            };

            process.Start();

            return await tcs.Task;
        }

        public async Task<int> LaunchGameWithFrameGenAsync(AppSettings settings, HardwareInfo? hardware = null)
        {
            var batPath = Path.Combine(_rootDirectory, "Run-AstroHybrid-FG.bat");
            if (!File.Exists(batPath))
            {
                throw new FileNotFoundException("Run-AstroHybrid-FG.bat not found", batPath);
            }

            var runHybridPath = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            if (!File.Exists(Path.Combine(runHybridPath, "AstroBot.exe")))
            {
                throw new FileNotFoundException("AstroBot.exe not found in RunHybrid", runHybridPath);
            }

            settings.LastLaunched = DateTime.Now;
            _settingsManager.Save(settings);

            var envVars = BuildEnvironmentVariables(settings, hardware);
            // Frame Gen specific overrides
            envVars["ASTROHYBRID_DLSSG"] = "1";
            envVars["ASTROHYBRID_CUDA_HOLE"] = "1";
            envVars["ASTROHYBRID_DLSS"] = "1";

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                WorkingDirectory = _rootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var kvp in envVars)
            {
                psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>();

            process.Exited += (sender, e) =>
            {
                tcs.SetResult(process.ExitCode);
            };

            process.Start();

            return await tcs.Task;
        }

        private Dictionary<string, string> BuildEnvironmentVariables(AppSettings settings, HardwareInfo? hardware)
        {
            var env = new Dictionary<string, string>();

            // Graphics card (issue #24): an explicit choice names the card, for kyty_gpu.dll (Kyty patch
            // 0047) and the AnyPS5 renderer. A name, not an index: this list comes from Windows, whose
            // order need not be Vulkan's. An automatic choice passes nothing, and kyty_gpu.dll keeps its
            // own preference (a discrete card over integrated graphics).
            var gpu = hardware?.SelectedGpu;
            if (hardware != null && hardware.SelectedGpuIndex >= 0 && gpu != null)
            {
                env["ANYPS5_GPU"] = GpuMatchToken(gpu.Name);
            }

            // Lighting/renderer selection
            // What this game build supports (GameSetup); no file = the original 01.019.000 install.
            var runHybrid = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            var features = GameFeatures.Load(runHybrid);
            // Prefer the soft-assert variant so a command-buffer overflow follows the
            // engine's failure path instead of crashing in command_buffer_writer (issue #25).
            bool useUltrawide = settings.Ultrawide || settings.IsUltrawideSize;
            bool useSuperUltrawide = settings.IsSuperUltrawideSize;
            var candidates = useUltrawide && useSuperUltrawide
                ? (settings.Lighting == 0
                    ? new[] { "AstroBot-NonRT-SuperUltrawide-SoftAssert.exe", "AstroBot-NonRT-SuperUltrawide.exe", "AstroBot-SuperUltrawide.exe", "AstroBot-NonRT-SoftAssert.exe", "AstroBot-NonRT.exe", "AstroBot.exe" }
                    : new[] { "AstroBot-SuperUltrawide.exe", "AstroBot.exe" })
                : useUltrawide
                ? (settings.Lighting == 0
                    ? new[] { "AstroBot-NonRT-Ultrawide-SoftAssert.exe", "AstroBot-NonRT-Ultrawide.exe", "AstroBot-Ultrawide.exe", "AstroBot-NonRT-SoftAssert.exe", "AstroBot-NonRT.exe", "AstroBot.exe" }
                    : new[] { "AstroBot-Ultrawide.exe", "AstroBot.exe" })
                : (settings.Lighting == 0
                    ? new[] { "AstroBot-NonRT-SoftAssert.exe", "AstroBot-NonRT.exe", "AstroBot.exe" }
                    : new[] { "AstroBot.exe" });

            if (!File.Exists(Path.Combine(runHybrid, candidates[0])))
                new GameSetup(_rootDirectory).EnsureVariantExecutables();

            // Bigger worker command buffers (#159): an install set up by 1.1.0 - 1.2.5 has 8 MiB, which
            // ran out in booster_construction. Raised to 16 MiB once per executable; a failure here only
            // keeps the old size, it never stops the game from starting.
            try
            {
                var setup = new GameSetup(_rootDirectory);
                foreach (var candidate in candidates.Concat(new[] { "AstroBot.exe" }).Distinct())
                {
                    // Keep the official 1.2.9 legacy 8-to-16 MiB and Garlic-pool upgrade, then apply
                    // the user's reversible 16/24 MiB selection without disturbing other 1.2.9 patches.
                    setup.UpgradeCommandBufferTable(candidate);
                    setup.SetCommandBufferSize(candidate, settings.StabilityMode && settings.CommandBuffer24MiB);
                }
            }
            catch (Exception ex)
            {
                AppendDebugLog($"command buffer upgrade skipped: {ex.Message}");
            }

            if (settings.Lighting >= 0 && settings.Lighting <= 1)
            {
                string chosen = "AstroBot.exe";
                foreach (var candidate in candidates)
                {
                    if (File.Exists(Path.Combine(runHybrid, candidate)))
                    {
                        chosen = candidate;
                        break;
                    }
                }
                env["ASTROHYBRID_EXE"] = chosen;
            }

            // FPS limit
            var fpsValues = new[] { "30", "35", "45", "50", "55", "60", "unlimited" };
            if (settings.Fps >= 0 && settings.Fps < fpsValues.Length)
            {
                env["ASTROHYBRID_FPS_LIMIT"] = fpsValues[settings.Fps];
            }

            // Window size
            // Indices 0..3,9 = 16:9 (9 = 4K), indices 4,5,8 = 21:9, indices 6,7 = 32:9 (#58, #98)
            if (settings.Size >= 0 && settings.Size < AppSettings.ResolutionPresets.Length)
            {
                var preset = AppSettings.ResolutionPresets[settings.Size];
                int width = preset.Width;
                int height = preset.Height;
                if (settings.Ultrawide && !settings.IsUltrawideSize)
                {
                    width = settings.Size == 9 ? 5120 : settings.Size == 3 ? 3440 : 2560;
                    height = settings.Size == 9 ? 2160 : settings.Size == 3 ? 1440 : 1080;
                }
                env["KYTY_GPU_WIDTH"] = width.ToString();
                env["KYTY_GPU_HEIGHT"] = height.ToString();
            }

            // Fullscreen
            env["KYTY_GPU_FULLSCREEN"] = settings.Fullscreen ? "1" : "0";

            // Game speed
            // Always real time, where the game version is known: it writes one global at a fixed address.
            env["ASTROHYBRID_GAME_SPEED"] = features == null || features.RealTimeSpeed ? "1" : "0";
            if (features != null && features.GameStepAddress != 0)
                env["ASTROHYBRID_GAME_STEP_ADDR"] = "0x" + features.GameStepAddress.ToString("x");
            var speedValues = new[] { "100", "95", "90", "85", "80", "75" };
            if (settings.Speed >= 0 && settings.Speed < speedValues.Length)
            {
                env["ASTROHYBRID_GAME_SPEED_PERCENT"] = speedValues[settings.Speed];
            }

            // Performance overlay
            var overlayValues = new[] { "", "fps", "compact", "full" };
            if (settings.Overlay >= 0 && settings.Overlay < overlayValues.Length)
            {
                env["ASTROHYBRID_OVERLAY"] = overlayValues[settings.Overlay];
            }

            // NVIDIA options, as AstroBotLauncher.cs: DLSS and frame generation need the CUDA address
            // window and the NVIDIA runtime files next to the game.
            if (settings.Upscale == 1 || settings.FrameGenMode != 0)
            {
                env["ASTROHYBRID_CUDA_HOLE"] = "1";
                env["ASTROHYBRID_DLSS_PATH"] = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            }
            // AMD FSR (AMD FSR SDK 2.4 runtime next to the game): the renderer picks FSR 4 where the card
            // supports it, else FSR 3.1, else FSR 2.
            if (settings.Upscale == 2)
            {
                env["ASTROHYBRID_FSR"] = "1";
                env["ASTROHYBRID_FSR_PATH"] = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            }
            if (settings.Upscale == 1)
            {
                env["ASTROHYBRID_DLSS"] = "1";
                var dlssQuality = new[] { "quality", "balanced", "performance", "dlaa" };
                if (settings.DlssQuality > 0 && settings.DlssQuality < dlssQuality.Length)
                    env["ASTROHYBRID_DLSS_QUALITY"] = dlssQuality[settings.DlssQuality];
            }

            // Frame generation plan (AstroBotLauncher.FrameGenPlan): the game is never capped below 30.
            // Auto with a target starts at 2x with the game capped at half the target and lets the game
            // raise the multiplier; fixed modes cap the game at target / multiplier; no lock = uncapped.
            if (settings.FrameGenMode != 0)
            {
                var targetValues = new[] { 0, 50, 60, 90, 120, 144, 165 };
                int goal = settings.FrameGenTarget >= 0 && settings.FrameGenTarget < targetValues.Length
                    ? targetValues[settings.FrameGenTarget] : 60;
                int mult = settings.FrameGenMode == 1 ? 2 : Math.Min(Math.Max(settings.FrameGenMode, 2), 4);
                int limit = goal == 0 ? 0 : Math.Max(30, goal / mult);
                env["ASTROHYBRID_DLSSG"] = "1";
                env["ASTROHYBRID_DLSSG_FRAMES"] = (mult - 1).ToString();
                if (settings.FrameGenMode == 1 && goal != 0)
                {
                    env["ASTROHYBRID_DLSSG_TARGET"] = goal.ToString();
                    env["ASTROHYBRID_DLSSG_MAX_FRAMES"] = "3";
                }
                env["ASTROHYBRID_FPS_LIMIT"] = limit == 0 ? "unlimited" : limit.ToString();
            }

            // NVIDIA Reflex: pass the setting both ways. Run-AstroHybrid-DLSS.bat turns low latency
            // on whenever the variable is unset, so writing only "1" made an off choice come back on
            // for every DLSS / Frame Generation launch; the bat documents 0 as the off value.
            env["ASTROHYBRID_LOW_LATENCY"] = settings.LowLatency ? "1" : "0";

            // Controller settings
            var padValues = new[] { "auto", "ps", "xbox", "switch", "generic" };
            if (settings.Controller >= 0 && settings.Controller < padValues.Length)
            {
                env["ASTROHYBRID_PAD_PREFER"] = padValues[settings.Controller];
            }

            // Vibration strength in percent (0-200); the Switch boost follows it instead of being a flat
            // 96 of 255, which made low strengths louder than the setting itself (#36).
            int rumblePercent = settings.EffectiveRumblePercent;
            env["ASTROHYBRID_RUMBLE_SCALE"] = rumblePercent.ToString();
            env["ASTROHYBRID_RUMBLE_SWITCH_FLOOR"] = RumbleSetting.SwitchFloor(rumblePercent, settings.SwitchBoost).ToString();

            // Window title
            env["ASTROHYBRID_WINDOW_TITLE"] = "Astro Bot";

            // Game language (AnyPS5 console language number); Auto maps the Windows display language.
            int language = settings.Language >= 0 && settings.Language <= 30 ? settings.Language : WindowsLanguage();
            env["ANYPS5_LANGUAGE"] = language.ToString();

            // VSync: Vulkan FIFO presentation (the default is immediate presentation). It also applies with DLSS
            // Frame Generation: the generated frames go through the same swapchain, and forcing VSync in the
            // NVIDIA driver (which only changes the present mode) removes the tearing there too (#15).
            if (settings.VSync) env["KYTY_GPU_PRESENT_MODE"] = "Fifo";

            // Stability Mode: conservative scheduling and extra diagnostics. Kept opt-out so users
            // can restore the official 1.2.9 performance defaults from Advanced Settings.
            if (settings.StabilityMode)
            {
                env["ASTROHYBRID_STABILITY_MODE"] = "1";
                env["KYTY_BARRIER_BATCH"] = "1";
                env["KYTY_DISPATCH_OVERLAP"] = "0";
                env["KYTY_DRAW_RUN"] = "0";
                env["ASTROHYBRID_OPT_1"] = "0";
                env["ASTROHYBRID_OPT_2"] = "0";
                env["ASTROHYBRID_COMMIT_EPOCH_FASTPATH"] = "0";
                env["ASTROHYBRID_DRAW_PREP_TUNING"] = "0";
                env["KYTY_AGC_CB_WATERMARK"] = "1";
                env["KYTY_HANG_WATCHDOG"] = "1";
                env["ASTROHYBRID_HANG_WATCHDOG"] = "1";
                env["KYTY_GFX_SLICE_DRAWS"] = "128";
                env["KYTY_SUBMIT_MIN_INTERVAL_US"] = "0";
            }

            // Advanced page: the variables Run-AstroHybrid.bat reads (it keeps its defaults when unset).
            if (settings.HangWatchdog) env["ASTROHYBRID_HANG_WATCHDOG"] = "1";
            if (settings.GpuFaultDiagnostics) env["ASTROHYBRID_GPU_FAULT_DIAGNOSTICS"] = "1";
            if (!settings.ShaderCache) env["ASTROHYBRID_SHADER_CACHE"] = "0";
            if (!settings.DrawPrepTuning) env["ASTROHYBRID_DRAW_PREP_TUNING"] = "0";
            if (settings.Priority == "normal" || settings.Priority == "abovenormal" || settings.Priority == "high")
                env["ASTROHYBRID_PRIORITY"] = settings.Priority;
            if (settings.CpPriority == 1 || settings.CpPriority == 2) env["KYTY_CP_PRIORITY"] = settings.CpPriority.ToString();
            env["ASTROHYBRID_PAD_MOTION"] = settings.MotionControls ? "1" : "0";
            // Right-stick tilt on Xbox/generic pads: toggle instead of hold, and its strength (#146).
            if (settings.TiltToggle) env["ASTROHYBRID_TILT_TOGGLE"] = "1";
            env["ASTROHYBRID_TILT_STRENGTH"] = Math.Max(25, Math.Min(200, settings.TiltStrength)).ToString();
            // DualSense adaptive triggers: resistance and vibration in percent, 0 = off (#160).
            env["ASTROHYBRID_TRIGGER_STRENGTH"] = Math.Max(0, Math.Min(100, settings.TriggerStrength)).ToString();
            // DualSense speaker off: the controller speaker's sounds are dropped, wired or Bluetooth (#143).
            if (!settings.DualSenseSpeaker)
            {
                env["ASTROHYBRID_DS_SPEAKER"] = "0";
                env["ASTROHYBRID_DS_WIRELESS_SPEAKER"] = "0";
            }
            if (!settings.TriggerState) env["APS5_PAD_TRIGGER_STATE"] = "0";
            if (settings.SkipIntro) env["ASTROHYBRID_SKIP_INTRO"] = "1";

            // Cloth/blanket/bubble stability (#30, #35): the commit epoch fast path only for read-only
            // bindings (writable ones stay on the full check), plus the buffer range fixes.
            env["ASTROHYBRID_COMMIT_EPOCH_FASTPATH"] = "read";
            env["KYTY_BUFFER_RANGE_FIXES"] = "1";
            env["APS5_SAVEDATA_TRANSFER_FALLBACK"] = "1";

            return env;
        }

        // The renderer selects a card by name (ANYPS5_GPU, a case-insensitive substring of the Vulkan
        // device name). The model token is used instead of the full Windows name so it still matches
        // the shorter name Vulkan reports ("NVIDIA GeForce RTX 4050 Laptop GPU" -> "RTX 4050").
        public static string GpuMatchToken(string name)
        {
            var match = Regex.Match(name, @"(?:RTX|GTX)\s*\d{3,4}(?:\s*(?:Ti|SUPER))?", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            match = Regex.Match(name, @"Arc\s*[AB]?\d{3}", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            match = Regex.Match(name, @"(?:UHD|Iris(?:\s*Xe)?|HD)\s*Graphics(?:\s*\d{3})?", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            match = Regex.Match(name, @"RX\s*\d{4}(?:\s*XT)?", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            return name;
        }

        // The console language number for the Windows display language; English (United States) when unknown.
        public static int WindowsLanguage()
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            string name = culture.Name, two = culture.TwoLetterISOLanguageName;
            switch (name)
            {
                case "pt-BR": return 17;
                case "en-GB": return 18;
                case "fr-CA": return 22;
                case "zh-TW": case "zh-HK": case "zh-MO": return 10;
            }
            if (two == "es") return name == "es-ES" ? 3 : 20;
            if (two == "zh") return 11;
            switch (two)
            {
                case "ja": return 0;
                case "en": return 1;
                case "fr": return 2;
                case "de": return 4;
                case "it": return 5;
                case "nl": return 6;
                case "pt": return 7;
                case "ru": return 8;
                case "ko": return 9;
                case "fi": return 12;
                case "sv": return 13;
                case "da": return 14;
                case "nb": case "nn": case "no": return 15;
                case "pl": return 16;
                case "tr": return 19;
                case "ar": return 21;
                case "cs": return 23;
                case "hu": return 24;
                case "el": return 25;
                case "ro": return 26;
                case "th": return 27;
                case "vi": return 28;
                case "id": return 29;
                case "uk": return 30;
                default: return 1;
            }
        }

        public bool IsGameRunning()
        {
            var processes = Process.GetProcessesByName("AstroBot");
            return processes.Length > 0;
        }

        public void KillGameProcesses()
        {
            foreach (var proc in Process.GetProcessesByName("AstroBot"))
            {
                try { proc.Kill(); } catch { }
            }
        }
    }
}
