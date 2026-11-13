using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        public async Task<int> LaunchGameAsync(AppSettings settings)
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
            var envVars = BuildEnvironmentVariables(settings);

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

        public async Task<int> LaunchGameWithDlssAsync(AppSettings settings)
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

            var envVars = BuildEnvironmentVariables(settings);
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

        public async Task<int> LaunchGameWithFrameGenAsync(AppSettings settings)
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

            var envVars = BuildEnvironmentVariables(settings);
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

        private Dictionary<string, string> BuildEnvironmentVariables(AppSettings settings)
        {
            var env = new Dictionary<string, string>();

            // Lighting/renderer selection
            // What this game build supports (GameSetup); no file = the original 01.019.000 install.
            var runHybrid = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            var features = GameFeatures.Load(runHybrid);
            var lightingExes = new[] { "AstroBot-NonRT.exe", "AstroBot.exe" };
            if (settings.Lighting >= 0 && settings.Lighting < lightingExes.Length)
            {
                var exe = lightingExes[settings.Lighting];
                env["ASTROHYBRID_EXE"] = File.Exists(Path.Combine(runHybrid, exe)) ? exe : "AstroBot.exe";
            }

            // FPS limit
            var fpsValues = new[] { "30", "35", "45", "50", "55", "60", "unlimited" };
            if (settings.Fps >= 0 && settings.Fps < fpsValues.Length)
            {
                env["ASTROHYBRID_FPS_LIMIT"] = fpsValues[settings.Fps];
            }

            // Window size
            var sizeValues = new[,] { { 1280, 720 }, { 1600, 900 }, { 1920, 1080 }, { 2560, 1440 } };
            if (settings.Size >= 0 && settings.Size < sizeValues.GetLength(0))
            {
                env["KYTY_GPU_WIDTH"] = sizeValues[settings.Size, 0].ToString();
                env["KYTY_GPU_HEIGHT"] = sizeValues[settings.Size, 1].ToString();
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
            if (settings.Upscale != 0 || settings.FrameGenMode != 0)
            {
                env["ASTROHYBRID_CUDA_HOLE"] = "1";
                env["ASTROHYBRID_DLSS_PATH"] = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            }
            if (settings.Upscale != 0)
            {
                env["ASTROHYBRID_DLSS"] = "1";
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

            // NVIDIA Reflex
            if (settings.LowLatency)
            {
                env["ASTROHYBRID_LOW_LATENCY"] = "1";
            }

            // Controller settings
            var padValues = new[] { "auto", "ps", "xbox", "switch", "generic" };
            if (settings.Controller >= 0 && settings.Controller < padValues.Length)
            {
                env["ASTROHYBRID_PAD_PREFER"] = padValues[settings.Controller];
            }

            var rumbleValues = new[] { "0", "40", "70", "100", "150" };
            if (settings.Rumble >= 0 && settings.Rumble < rumbleValues.Length)
            {
                env["ASTROHYBRID_RUMBLE_SCALE"] = rumbleValues[settings.Rumble];
            }

            env["ASTROHYBRID_RUMBLE_SWITCH_FLOOR"] = settings.SwitchBoost ? "96" : "0";

            // Window title
            env["ASTROHYBRID_WINDOW_TITLE"] = "Astro Bot";

            // Game language (AnyPS5 console language number); Auto maps the Windows display language.
            int language = settings.Language >= 0 && settings.Language <= 30 ? settings.Language : WindowsLanguage();
            env["ANYPS5_LANGUAGE"] = language.ToString();

            // VSync: Vulkan FIFO presentation (the default is immediate presentation).
            if (settings.VSync) env["KYTY_GPU_PRESENT_MODE"] = "Fifo";

            // Advanced page: the variables Run-AstroHybrid.bat reads (it keeps its defaults when unset).
            if (settings.HangWatchdog) env["ASTROHYBRID_HANG_WATCHDOG"] = "1";
            if (!settings.ShaderCache) env["ASTROHYBRID_SHADER_CACHE"] = "0";
            if (!settings.DrawPrepTuning) env["ASTROHYBRID_DRAW_PREP_TUNING"] = "0";
            if (settings.Priority == "normal" || settings.Priority == "abovenormal" || settings.Priority == "high")
                env["ASTROHYBRID_PRIORITY"] = settings.Priority;
            if (settings.CpPriority == 1 || settings.CpPriority == 2) env["KYTY_CP_PRIORITY"] = settings.CpPriority.ToString();
            env["ASTROHYBRID_PAD_MOTION"] = settings.MotionControls ? "1" : "0";
            if (!settings.TriggerState) env["APS5_PAD_TRIGGER_STATE"] = "0";

            // Always-on optimizations
            env["ASTROHYBRID_COMMIT_EPOCH_FASTPATH"] = "1";

            return env;
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