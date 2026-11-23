using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.ViewModels
{
    public class GraphicsSettingsViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainViewModel;

        public GraphicsSettingsViewModel(MainWindowViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += (sender, e) => OnPropertyChanged(string.Empty);
            RestoreDefaultsCommand = new RelayCommand(() => { _mainViewModel.RestoreGraphicsDefaults(); OnPropertyChanged(string.Empty); });
        }

        public System.Windows.Input.ICommand RestoreDefaultsCommand { get; }

        private bool _advancedExpanded;
        public bool IsAdvancedExpanded
        {
            get => _advancedExpanded;
            set { if (_advancedExpanded != value) { _advancedExpanded = value; OnPropertyChanged(); } }
        }

        // Preset: the game's two lighting builds. Performance is the non-ray-traced build.
        public string[] PresetLabels => new[] { "Performance - faster lighting (recommended)", "Quality - ray-traced lighting" };

        public int PresetIndex
        {
            get => RayTracingIndex;
            set { RayTracingIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveSummary)); }
        }

        // Resolution: group 16:9 presets first (including 4K) before 21:9/32:9 ultrawide options,
        // while keeping underlying AppSettings.Size storage indices unchanged for saved configs.
        private static readonly int[] ResolutionDisplayOrder = { 0, 1, 2, 3, 9, 4, 5, 8, 6, 7 };

        public string[] ResolutionLabels => ResolutionDisplayOrder.Select(i => AppSettings.ResolutionPresets[i].Label).ToArray();

        public int ResolutionIndex
        {
            get
            {
                int displayIndex = Array.IndexOf(ResolutionDisplayOrder, _mainViewModel.Settings.Size);
                return displayIndex >= 0 ? displayIndex : 2;
            }
            set
            {
                if (value < 0 || value >= ResolutionDisplayOrder.Length) return;
                int sizeIndex = ResolutionDisplayOrder[value];
                if (_mainViewModel.Settings.Size != sizeIndex)
                {
                    _mainViewModel.Settings.Size = sizeIndex;
                    bool wantsUltrawide = AppSettings.GetAspectRatio(sizeIndex) != 16;
                    if (_mainViewModel.Settings.Ultrawide != wantsUltrawide)
                    {
                        _mainViewModel.Settings.Ultrawide = wantsUltrawide;
                        OnPropertyChanged(nameof(Ultrawide));
                    }
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Graphics card (issue #24). A laptop with hybrid graphics reports two adapters, and the one
        // that drives the built-in screen is usually the integrated GPU: the launcher must describe
        // (and enable NVIDIA features for) a card the user can pick, not just the first adapter.
        public bool HasGpuChoice => (_mainViewModel.HardwareInfo?.Gpus.Count ?? 0) > 1;

        public string[] GpuLabels
        {
            get
            {
                var labels = new List<string> { "Automatic - best graphics card" };
                foreach (var gpu in _mainViewModel.HardwareInfo?.Gpus ?? new List<GpuAdapter>())
                    labels.Add(gpu.Label);
                return labels.ToArray();
            }
        }

        public int GpuIndex
        {
            get => (_mainViewModel.HardwareInfo?.SelectedGpuIndex ?? -1) + 1;
            set
            {
                var hardware = _mainViewModel.HardwareInfo;
                var selected = value - 1;
                if (hardware == null || selected < -1 || selected >= hardware.Gpus.Count || hardware.SelectedGpuIndex == selected)
                    return;

                hardware.SelectGpu(selected);
                _mainViewModel.Settings.Gpu = selected;
                _mainViewModel.ResolveAutoGraphics();

                // DLSS and frame generation need the NVIDIA card: a card without them turns the
                // options off instead of leaving settings the launcher cannot honour.
                if (!hardware.SupportsDlss && _mainViewModel.Settings.Upscale == 1)
                    _mainViewModel.Settings.Upscale = 0;
                if (!hardware.SupportsFrameGeneration && _mainViewModel.Settings.FrameGenMode != 0)
                    _mainViewModel.Settings.FrameGenMode = 0;

                OnPropertyChanged();
                OnPropertyChanged(nameof(GpuNote));
                OnPropertyChanged(nameof(HardwareNote));
                OnPropertyChanged(nameof(UpscaleIndex));
                OnPropertyChanged(nameof(FrameGenIndex));
                OnPropertyChanged(nameof(SupportsDlss));
                OnPropertyChanged(nameof(SupportsReflex));
                OnPropertyChanged(nameof(IsFrameGenAvailable));
                OnPropertyChanged(nameof(FsrFrameGen));
                OnPropertyChanged(nameof(IsFsrFrameGenAvailable));
                OnPropertyChanged(nameof(FsrNote));
                OnPropertyChanged(nameof(ShowFsrSettings));
                OnPropertyChanged(nameof(ShowDlssQuality));
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.HardwareInfo));
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        public string GpuNote
        {
            get
            {
                var hardware = _mainViewModel.HardwareInfo;
                if (hardware == null || hardware.Gpus.Count == 0)
                    return "No graphics card detected.";

                var detected = string.Join(", ", hardware.Gpus.Select(g =>
                    $"{g.Name} ({(g.IsDiscrete ? "discrete" : g.IsIntegrated ? "integrated" : "graphics")})"));
                var text = "Detected: " + detected + ".";

                if (hardware.SelectedGpuIndex < 0)
                    text += " Automatic uses the discrete card: the renderer prefers one over integrated graphics.";
                else
                    text += " The choice is passed to the game as ANYPS5_GPU; the renderer also prefers a discrete card by itself.";

                if (hardware.SelectedGpu?.RtxSeries >= 20)
                    text += " DLSS, Reflex and frame generation use this card.";
                else if (hardware.Gpus.Any(g => g.RtxSeries >= 20))
                    text += " The NVIDIA options need the RTX card: pick it, or Automatic, to use them.";
                return text;
            }
        }

        // Fullscreen
        public bool Fullscreen
        {
            get => _mainViewModel.Settings.Fullscreen;
            set
            {
                if (_mainViewModel.Settings.Fullscreen != value)
                {
                    _mainViewModel.Settings.Fullscreen = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Ultrawide render-space patch (issue #29/#33/#58).
        public bool Ultrawide
        {
            get => _mainViewModel.Settings.Ultrawide;
            set
            {
                if (_mainViewModel.Settings.Ultrawide != value)
                {
                    _mainViewModel.Settings.Ultrawide = value;
                    if (value && !_mainViewModel.Settings.IsUltrawideSize)
                    {
                        _mainViewModel.Settings.Size = _mainViewModel.Settings.Size == 9 ? 8 : _mainViewModel.Settings.Size == 3 ? 5 : 4;
                    }
                    else if (!value && _mainViewModel.Settings.IsUltrawideSize)
                    {
                        _mainViewModel.Settings.Size = _mainViewModel.Settings.Size == 8 ? 9 : (_mainViewModel.Settings.Size == 5 || _mainViewModel.Settings.Size == 6) ? 3 : 2;
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ResolutionIndex));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string UltrawideNote => "Uses the matching 21:9 or 32:9 game patch and window dimensions (2560x1080 / 3440x1440 / 5120x2160 / 3840x1080 / 5120x1440) when enabled; 16:9 remains unchanged.";

        // Ray Tracing / Lighting mode (0 = OFF / Non-RT, 1 = ON / Ray Tracing)
        public string[] RayTracingLabels => new[] { "OFF - Faster lighting (Non-RT, recommended)", "ON - Ray Tracing (Original lighting)" };

        public int RayTracingIndex
        {
            get => _mainViewModel.Settings.Lighting;
            set
            {
                if (_mainViewModel.Settings.Lighting != value)
                {
                    _mainViewModel.Settings.Lighting = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RayTracingNote));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        public string RayTracingNote => _mainViewModel.Settings.Lighting == 1
            ? "Ray Tracing ON: runs AstroBot.exe with full GI probes and ray-traced lighting shaders (higher GPU load)."
            : "Ray Tracing OFF: runs AstroBot-NonRT-SoftAssert.exe for faster performance and steadier frame times.";

        // VSync sets the swapchain's present mode (FIFO), which frame generation presents through as well (#15).
        public bool IsVSyncAvailable => true;
        public string VSyncNote => _mainViewModel.Settings.FrameGenMode == 0 && !FsrFrameGen
            ? "Locks presentation to your screen's refresh (no tearing)."
            : "Locks presentation to your screen's refresh (no tearing), generated frames included. Keep the frame generation target at or below your screen's refresh rate; with a G-Sync / FreeSync screen you can also leave VSync off.";

        // VSync
        public bool VSync
        {
            get => _mainViewModel.Settings.VSync;
            set
            {
                if (_mainViewModel.Settings.VSync != value)
                {
                    _mainViewModel.Settings.VSync = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // With frame generation on, the target sets the game's own limit (as in the classic launcher).
        public string FpsLimitLabel => IsFpsLimitEnabled ? "FPS limit" : "Target on screen";
        public bool IsFpsLimitEnabled => _mainViewModel.Settings.FrameGenMode == 0;
        public string FpsLimitNote => !IsFpsLimitEnabled || !FsrFrameGen
            ? FpsLimitNoteWithoutFsr
            : "AMD FSR frame generation is on: this is the game's own rate, and the screen shows twice as many frames (30 -> 60, 45 -> 90, 60 -> 120).";
        private string FpsLimitNoteWithoutFsr => IsFpsLimitEnabled
            ? "The game may run below the selected limit in demanding scenes."
            : "Frame generation is on: the game's own limit comes from the frame generation target below (60 on screen = game at 30 x2). Turn frame generation off to use this limit.";

        // FPS Limiter
        public string[] FpsLabels => new[] { "30 FPS - steady (recommended)", "35 FPS", "45 FPS", "50 FPS", "55 FPS", "60 FPS", "No limit" };

        public int FpsIndex
        {
            get => _mainViewModel.Settings.Fps;
            set
            {
                if (_mainViewModel.Settings.Fps != value)
                {
                    _mainViewModel.Settings.Fps = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FsrNote));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Upscaling: Auto, Off and only the upscalers this card supports.
        private bool HasDlss => _mainViewModel.HardwareInfo?.SupportsDlss == true;

        public string[] UpscaleLabels
        {
            get
            {
                var hw = _mainViewModel.HardwareInfo;
                var bestUpscale = hw != null ? _mainViewModel.BestSettingsFor(hw).Upscale : 0;
                var labels = new List<string> { "Auto - " + UpscalerName(bestUpscale) };
                labels.Add("Off - the game's own upscaling");
                if (HasDlss) labels.Add("NVIDIA DLSS - sharper image");
                labels.Add("AMD FSR - " + FsrVersionNote);
                return labels.ToArray();
            }
        }

        // Combo index -> Upscale value (index 0 is Auto): Off, DLSS where supported, FSR.
        private int[] UpscaleValues => HasDlss ? new[] { -1, 0, 1, 2 } : new[] { -1, 0, 2 };

        private static string UpscalerName(int value) =>
            value == 1 ? "NVIDIA DLSS" : value == 2 ? "AMD FSR" : "the game's own upscaling";

        // FSR 4 (machine learning) runs on Radeon RX 9000 cards; the renderer falls back to FSR 3.1 elsewhere.
        private string FsrVersionNote
        {
            get
            {
                var name = _mainViewModel.HardwareInfo?.GpuName ?? "";
                bool rdna4 = name.IndexOf("RX 90", StringComparison.OrdinalIgnoreCase) >= 0;
                return rdna4 ? "FSR 4 on this card" : "FSR 3.1 on this card (FSR 4 needs a Radeon RX 9000)";
            }
        }

        public int UpscaleIndex
        {
            get
            {
                var st = _mainViewModel.Settings;
                if (st.UpscaleAuto) return 0;
                int i = Array.IndexOf(UpscaleValues, st.Upscale);
                return i > 0 ? i : 1;
            }
            set
            {
                var values = UpscaleValues;
                if (value < 0 || value >= values.Length || value == UpscaleIndex) return;
                var st = _mainViewModel.Settings;
                st.UpscaleAuto = value == 0;
                if (value != 0) st.Upscale = values[value];
                _mainViewModel.ResolveAutoGraphics();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFrameGenAvailable));
                OnPropertyChanged(nameof(FrameGenIndex));
                OnPropertyChanged(nameof(FrameGenNote));
                OnPropertyChanged(nameof(FsrFrameGen));
                OnPropertyChanged(nameof(IsFsrFrameGenAvailable));
                OnPropertyChanged(nameof(FsrNote));
                OnPropertyChanged(nameof(ShowFsrSettings));
                OnPropertyChanged(nameof(ShowDlssQuality));
                OnPropertyChanged(nameof(ActiveSummary));
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        // Frame generation: Auto, Off and only the modes this card supports (3x / 4x need RTX 50).
        private int MaxMultiplier => Math.Max(2, _mainViewModel.HardwareInfo?.MaxFrameGenMultiplier ?? 2);

        // Combo index -> FrameGenMode (index 0 is Auto).
        private int[] FrameGenModes
        {
            get
            {
                var modes = new List<int> { -1, 0, 1, 2 };
                if (MaxMultiplier >= 3) modes.Add(3);
                if (MaxMultiplier >= 4) modes.Add(4);
                return modes.ToArray();
            }
        }

        public string[] FrameGenLabels
        {
            get
            {
                var hw = _mainViewModel.HardwareInfo;
                var bestFg = hw != null ? _mainViewModel.BestSettingsFor(hw).FrameGen : 0;
                var labels = new List<string>
                {
                    "Auto - " + (bestFg != 0 ? "on, reaches the target below" : "off"),
                    "Off",
                    MaxMultiplier > 2 ? "Dynamic - 2x to " + MaxMultiplier + "x to reach the target" : "On - 2x to reach the target",
                    "Fixed 2x",
                };
                if (MaxMultiplier >= 3) labels.Add("Fixed 3x");
                if (MaxMultiplier >= 4) labels.Add("Fixed 4x");
                return labels.ToArray();
            }
        }

        public int FrameGenIndex
        {
            get
            {
                var st = _mainViewModel.Settings;
                if (st.FrameGenAuto) return 0;
                int i = Array.IndexOf(FrameGenModes, st.FrameGenMode);
                return i > 0 ? i : 1;
            }
            set
            {
                var modes = FrameGenModes;
                if (value < 0 || value >= modes.Length || value == FrameGenIndex) return;
                var st = _mainViewModel.Settings;
                st.FrameGenAuto = value == 0;
                if (value != 0) st.FrameGenMode = modes[value];
                _mainViewModel.ResolveAutoGraphics();
                if (st.FrameGenMode != 0) st.FsrFrameGen = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FsrFrameGen));
                OnPropertyChanged(nameof(IsFsrFrameGenAvailable));
                OnPropertyChanged(nameof(FsrNote));
                OnPropertyChanged(nameof(ShowFsrSettings));
                OnPropertyChanged(nameof(ShowDlssQuality));
                OnPropertyChanged(nameof(IsFrameGenTargetEnabled));
                OnPropertyChanged(nameof(FrameGenNote));
                OnPropertyChanged(nameof(IsFpsLimitEnabled));
                OnPropertyChanged(nameof(IsVSyncAvailable));
                OnPropertyChanged(nameof(VSyncNote));
                OnPropertyChanged(nameof(FpsLimitNote));
                OnPropertyChanged(nameof(ActiveSummary));
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        // AMD FSR frame generation (AMD FSR SDK 2.4 runtime: amd_fidelityfx_loader_vk.dll and
        // amd_fidelityfx_framegeneration_vk.dll in Build\RunHybrid). Any graphics card, any upscaler;
        // the other frame generator, so not together with NVIDIA frame generation.
        public bool IsFsrFrameGenInstalled => Services.LauncherService.FsrFrameGenRuntimeAvailable(_mainViewModel.RootDirectory);

        public bool IsFsrFrameGenAvailable => IsFsrFrameGenInstalled && _mainViewModel.Settings.FrameGenMode == 0;

        public bool FsrFrameGen
        {
            get => _mainViewModel.Settings.FsrFrameGen && _mainViewModel.Settings.FrameGenMode == 0;
            set
            {
                bool wanted = value && IsFsrFrameGenAvailable;
                if (_mainViewModel.Settings.FsrFrameGen != wanted)
                {
                    _mainViewModel.Settings.FsrFrameGen = wanted;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FsrNote));
                    OnPropertyChanged(nameof(FpsLimitNote));
                    OnPropertyChanged(nameof(VSyncNote));
                    OnPropertyChanged(nameof(ActiveSummary));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
                else if (value != wanted)
                {
                    OnPropertyChanged();
                }
            }
        }

        public string FsrNote
        {
            get
            {
                if (!IsFsrFrameGenInstalled)
                    return "Not installed: AMD FSR frame generation needs amd_fidelityfx_loader_vk.dll and amd_fidelityfx_framegeneration_vk.dll (AMD FSR SDK 2.4) in Build\\RunHybrid.";
                if (_mainViewModel.Settings.FrameGenMode != 0)
                    return "NVIDIA frame generation is on: turn it off to use AMD FSR frame generation instead.";
                var limits = new[] { 30, 35, 45, 50, 55, 60, 0 };
                int limit = FpsIndex >= 0 && FpsIndex < limits.Length ? limits[FpsIndex] : 30;
                string shown = limit == 0 ? "twice the game's frame rate on screen" : $"game up to {limit} FPS x2 = {limit * 2} on screen";
                return (FsrFrameGen ? "On: " + shown + ". " : "One generated frame between every two game frames (" + shown + "). ") +
                       "Works on any graphics card, with any upscaling choice above. NVIDIA Reflex is off while it runs, it adds some input delay, and the HUD can shimmer in fast motion.";
            }
        }

        // DLSS quality (#156), shown under Advanced while DLSS is the upscaler.
        public string[] DlssQualityLabels => new[] { "Quality (default)", "Balanced", "Performance", "DLAA - native resolution, sharpest" };

        public int DlssQualityIndex
        {
            get => _mainViewModel.Settings.DlssQuality;
            set
            {
                if (value < 0 || value > 3 || _mainViewModel.Settings.DlssQuality == value) return;
                _mainViewModel.Settings.DlssQuality = value;
                OnPropertyChanged();
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        public bool ShowDlssQuality => _mainViewModel.Settings.Upscale == 1 && HasDlss;

        // AMD FSR upscaling controls, shown while AMD FSR is the active upscaler.
        public bool ShowFsrSettings => _mainViewModel.Settings.Upscale == 2;

        public string[] FsrQualityLabels => AppSettings.FsrQualityPresets.Select(p => p.Label).ToArray();

        public int FsrQualityIndex
        {
            get => _mainViewModel.Settings.FsrQuality;
            set
            {
                if (value < 0 || value >= AppSettings.FsrQualityPresets.Length || _mainViewModel.Settings.FsrQuality == value) return;
                _mainViewModel.Settings.FsrQuality = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveSummary));
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        public int FsrSharpness
        {
            get => _mainViewModel.Settings.FsrSharpness;
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (_mainViewModel.Settings.FsrSharpness == clamped) return;
                _mainViewModel.Settings.FsrSharpness = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FsrSharpnessLabel));
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        public string FsrSharpnessLabel => FsrSharpness == 0 ? "0% (Off)" : $"{FsrSharpness}%";

        public string[] FsrVersionLabels => new[]
        {
            "Auto (newest for card)",
            "FSR 4 (Radeon RX 9000)",
            "FSR 3.1",
            "FSR 2"
        };

        public int FsrVersionIndex
        {
            get => _mainViewModel.Settings.FsrVersion;
            set
            {
                if (value < 0 || value >= AppSettings.FsrVersionValues.Length || _mainViewModel.Settings.FsrVersion == value) return;
                _mainViewModel.Settings.FsrVersion = value;
                OnPropertyChanged();
                _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
            }
        }

        public bool ShowFrameGen => _mainViewModel.HardwareInfo?.SupportsFrameGeneration == true;
        public bool ShowFrameGenUnavailable => !ShowFrameGen;
        public string FrameGenUnavailableNote => "NVIDIA frame generation needs an RTX 40 or newer card, so it is not offered on this graphics card."
            + (IsFsrFrameGenInstalled ? " AMD FSR frame generation (below) works on this card." : "");

        // What will actually run with the current choices, in plain words.
        public string ActiveSummary
        {
            get
            {
                var st = _mainViewModel.Settings;
                var parts = new List<string>();
                parts.Add(st.Lighting == 1 ? "ray-traced lighting" : "faster lighting");
                string fsrLabel = st.Upscale == 2 && st.FsrQuality >= 0 && st.FsrQuality < AppSettings.FsrQualityPresets.Length
                    ? $"AMD FSR upscaling ({AppSettings.FsrQualityPresets[st.FsrQuality].ShortLabel})"
                    : "AMD FSR upscaling";
                parts.Add(st.Upscale == 1 ? "DLSS upscaling" : st.Upscale == 2 ? fsrLabel : "the game's own upscaling");
                if (st.FrameGenMode != 0)
                {
                    var targetValues = new[] { 0, 50, 60, 90, 120, 144, 165 };
                    int goal = st.FrameGenTarget >= 0 && st.FrameGenTarget < targetValues.Length ? targetValues[st.FrameGenTarget] : 60;
                    string fg = st.FrameGenMode == 1 ? "frame generation" : st.FrameGenMode + "x frame generation";
                    parts.Add(goal > 0 ? fg + " to " + goal + " FPS on screen" : fg);
                }
                else
                {
                    var fps = new[] { "30", "35", "45", "50", "55", "60", "unlimited" };
                    parts.Add((st.Fps >= 0 && st.Fps < fps.Length ? fps[st.Fps] : "30") + " FPS limit");
                    if (FsrFrameGen) parts.Add("AMD FSR frame generation (2x on screen)");
                }
                // Reflex is off while AMD FSR frame generation presents the frames.
                if ((st.LowLatency || st.FrameGenMode != 0) && !FsrFrameGen) parts.Add("Reflex");
                return "Active: " + string.Join(", ", parts) + ".";
            }
        }

        // Frame Gen Target
        public string[] FrameGenTargetLabels => new[] { "No lock - game as fast as it can", "50 FPS on screen", "60 FPS on screen (recommended)", "90 FPS on screen", "120 FPS on screen", "144 FPS on screen", "165 FPS on screen" };

        public int FrameGenTargetIndex
        {
            get => _mainViewModel.Settings.FrameGenTarget;
            set
            {
                if (_mainViewModel.Settings.FrameGenTarget != value)
                {
                    _mainViewModel.Settings.FrameGenTarget = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FrameGenNote));
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // NVIDIA Reflex
        public bool LowLatency
        {
            get => _mainViewModel.Settings.LowLatency;
            set
            {
                if (_mainViewModel.Settings.LowLatency != value)
                {
                    _mainViewModel.Settings.LowLatency = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Performance Overlay
        public string[] OverlayLabels => new[] { "Off (F2 shows it)", "FPS only", "FPS + CPU, GPU, VRAM, RAM", "Everything (all stats)" };

        public int OverlayIndex
        {
            get => _mainViewModel.Settings.Overlay;
            set
            {
                if (_mainViewModel.Settings.Overlay != value)
                {
                    _mainViewModel.Settings.Overlay = value;
                    OnPropertyChanged();
                    _mainViewModel.OnPropertyChanged(nameof(MainWindowViewModel.Settings));
                }
            }
        }

        // Computed properties for UI state
        public bool IsFrameGenAvailable => _mainViewModel.HardwareInfo?.SupportsFrameGeneration == true && _mainViewModel.Settings.Upscale == 1;

        public bool IsFrameGenTargetEnabled => _mainViewModel.Settings.FrameGenMode != 0;

        public bool SupportsDlss => _mainViewModel.HardwareInfo?.SupportsDlss == true;

        public bool SupportsReflex => _mainViewModel.HardwareInfo?.SupportsReflex == true;

        public string FrameGenNote
        {
            get
            {
                int mode = _mainViewModel.Settings.FrameGenMode;
                if (!IsFrameGenAvailable)
                    return "Frame generation (NVIDIA DLSS Frame Generation) needs DLSS upscaling: choose Auto or NVIDIA DLSS above.";
                if (mode == 0)
                    return "Frame generation multiplies the frames you see (30 FPS limit x2 = 60 on screen) and turns Reflex on itself. It runs only while the game window is focused. Do not also use NVIDIA Smooth Motion.";

                var targetValues = new[] { 0, 50, 60, 90, 120, 144, 165 };
                int goal = FrameGenTargetIndex >= 0 && FrameGenTargetIndex < targetValues.Length ? targetValues[FrameGenTargetIndex] : 60;
                int mult = mode == 1 ? 2 : mode;

                if (mode == 1 && goal != 0)
                {
                    int limit = Math.Max(30, goal / 2);
                    return $"Game up to {limit} FPS; x2 to x4 picked in game to stay at {goal}+ FPS on screen";
                }
                else if (goal == 0)
                {
                    return $"Game unlocked, x{mult} on screen (e.g. 30 -> {30 * mult})";
                }
                else
                {
                    int limit = Math.Max(30, goal / mult);
                    return $"Game up to {limit} FPS x{mult} = {limit * mult} on screen";
                }
            }
        }

        public string HardwareNote
        {
            get
            {
                if (_mainViewModel.HardwareInfo == null) return "";

                var gpu = _mainViewModel.HardwareInfo.GpuName;
                if (_mainViewModel.HardwareInfo.RtxSeries >= 50)
                    return $"{gpu}: DLSS + Multi Frame Gen (up to 4x) + Reflex available";
                else if (_mainViewModel.HardwareInfo.RtxSeries >= 40)
                    return $"{gpu}: DLSS + Frame Gen (2x) + Reflex available";
                else if (_mainViewModel.HardwareInfo.RtxSeries >= 20)
                    return $"{gpu}: DLSS + Reflex available (NVIDIA Frame Gen needs RTX 40+" +
                           (IsFsrFrameGenInstalled ? "; AMD FSR frame generation works on this card)" : ")");
                else
                    return $"{gpu}: AMD FSR upscaling" + (IsFsrFrameGenInstalled ? " and frame generation" : "") +
                           " available (DLSS, NVIDIA frame generation and Reflex need an NVIDIA RTX card)";
            }
        }
    }
}
