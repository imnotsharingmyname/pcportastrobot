// AstroBot Launcher: one window to pick the settings, then it starts AstroHybrid
// (Run-AstroHybrid.bat, no console window). Settings are shared with launcher.ps1
// (Build\astrohybrid-settings.json). Environment variables are set for this launch only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Version information shown in the file's Properties (and read by antivirus heuristics).
[assembly: AssemblyTitle("AstroBot Launcher")]
[assembly: AssemblyDescription("AstroHybrid classic launcher: settings window that starts Run-AstroHybrid.bat")]
[assembly: AssemblyCompany("AstroHybrid Project")]
[assembly: AssemblyProduct("AstroHybrid")]
[assembly: AssemblyCopyright("Copyright (c) 2026 AstroHybrid Project")]
[assembly: AssemblyVersion("1.0.7.0")]
[assembly: AssemblyFileVersion("1.0.7.0")]

static class AstroBotLauncher
{
    static readonly string[] LightingLabels = { "Faster lighting (recommended)", "Original lighting (slower)" };
    static readonly string[] LightingExes = { "AstroBot-NonRT-SoftAssert.exe", "AstroBot.exe" };
    static readonly string[] FpsLabels = { "30 FPS - steady (recommended)", "35 FPS", "45 FPS", "50 FPS", "55 FPS", "60 FPS", "No limit" };
    static readonly string[] FpsValues = { "30", "35", "45", "50", "55", "60", "unlimited" };
    static readonly string[] SpeedLabels = { "100% - real time", "95%", "90%", "85%", "80%", "75% (recommended)" };
    static readonly string[] SpeedValues = { "100", "95", "90", "85", "80", "75" };
    static readonly string[] OverlayLabels = { "Off (F2 shows it)", "FPS only", "FPS + CPU, GPU, VRAM, RAM", "Everything (all stats)" };
    static readonly string[] OverlayValues = { "", "fps", "compact", "full" };
    static readonly string[] SizeLabels = { "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440" };
    static readonly int[,] SizeValues = { { 1280, 720 }, { 1600, 900 }, { 1920, 1080 }, { 2560, 1440 } };
    // The "upscale" values the app writes too: 0 the game's own pass, 1 NVIDIA DLSS (RTX), 2 AMD FSR,
    // 3 Intel XeSS (Intel Arc, most GPUs with DP4a).
    static readonly string[] UpscaleLabels = { "Game's own (default)", "NVIDIA DLSS - sharper image (RTX)", "AMD FSR - sharper image", "Intel XeSS - sharper image (Intel Arc, most GPUs)" };
    static readonly string[] FrameGenLabels = { "Off (default)", "Auto - reach the target (recommended)", "Fixed 2x", "Fixed 3x", "Fixed 4x" };
    static readonly string[] TargetLabels = { "No lock - game as fast as it can", "50 FPS on screen", "60 FPS on screen (recommended)", "90 FPS on screen", "120 FPS on screen", "144 FPS on screen", "165 FPS on screen" };
    static readonly int[] TargetValues = { 0, 50, 60, 90, 120, 144, 165 };
    static readonly string[] PadLabels = { "Auto - the controller you press (recommended)", "PlayStation (DualSense, DualShock 4)", "Xbox", "Switch Pro (and compatible pads)", "Other / generic" };
    static readonly string[] PadValues = { "auto", "ps", "xbox", "switch", "generic" };
    static readonly string[] RumbleLabels = { "Off", "Almost off (5%)", "Minimal (10%)", "Very light (20%)", "Light (40%)", "Medium (70%, recommended)", "Full (100%)", "Strong (150%)", "Custom..." };
    // Percent behind every label; -1 is "Custom", which uses the number box below the list (issue #36).
    static readonly int[] RumblePresets = { 0, 5, 10, 20, 40, 70, 100, 150, -1 };
    static readonly int[] LegacyRumblePercents = { 0, 40, 70, 100, 150 };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, int index, ref DisplayDevice info, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DevMode info);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    // What "Best settings for this PC" looks at: the graphics card on the main display, the CPU,
    // memory and the main display's resolution and refresh rate.
    sealed class PcInfo
    {
        public string Gpu = "unknown graphics card";
        public int RtxSeries;
        public bool IntelXess; // Intel Arc / Iris Xe: XeSS is the recommended upscaler
        public string Cpu = "unknown CPU";
        public int Threads = Environment.ProcessorCount;
        public int RamGb;
        public int Width = Screen.PrimaryScreen.Bounds.Width, Height = Screen.PrimaryScreen.Bounds.Height, Hz = 60;
    }

    static PcInfo DetectPc()
    {
        var pc = new PcInfo();
        string primary = "";
        try
        {
            for (int i = 0; ; i++)
            {
                var dd = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                var name = dd.DeviceString == null ? "" : dd.DeviceString.Trim();
                if (name.Length == 0) continue;
                if ((dd.StateFlags & 4) != 0) { primary = name; break; } // primary device
                if ((dd.StateFlags & 1) != 0 && primary.Length == 0) primary = name; // attached
            }
        }
        catch { }

        // Every adapter, not only the one that drives the desktop (issue #24): on a laptop the
        // desktop adapter is usually the integrated GPU, and the discrete card is never attached to
        // it, so EnumDisplayDevices alone reports "Intel UHD Graphics" on an RTX 4050 laptop.
        string gpu = PickGpu(DisplayAdapterNames(), primary);
        pc.Gpu = gpu.Length > 0 ? gpu : "unknown graphics card";

        var m = Regex.Match(pc.Gpu, @"RTX\s*(\d{1,2})\d{2}", RegexOptions.IgnoreCase);
        if (m.Success) pc.RtxSeries = int.Parse(m.Groups[1].Value);
        pc.IntelXess = Regex.IsMatch(pc.Gpu, @"Intel.*(?:\bArc\b|Iris\(R\)\s*Xe|Iris\s*Xe)", RegexOptions.IgnoreCase);
        try
        {
            var dm = new DevMode { dmSize = (short)Marshal.SizeOf(typeof(DevMode)) };
            if (EnumDisplaySettings(null, -1, ref dm) && dm.dmDisplayFrequency > 1)
            {
                pc.Hz = dm.dmDisplayFrequency;
                pc.Width = dm.dmPelsWidth;
                pc.Height = dm.dmPelsHeight;
            }
        }
        catch { }
        try
        {
            var ms = new MemoryStatus { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref ms)) pc.RamGb = (int)Math.Round(ms.ullTotalPhys / 1073741824.0);
        }
        catch { }
        try
        {
            var name = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string;
            if (!string.IsNullOrEmpty(name)) pc.Cpu = name.Trim();
        }
        catch { }
        return pc;
    }

    // Display adapters Windows has a driver for, from the display class registry key (no WMI and no
    // extra assembly references, so the release script keeps compiling this file with csc alone).
    static List<string> DisplayAdapterNames()
    {
        var names = new List<string>();
        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            {
                if (key != null)
                {
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, "^[0-9]{4}$")) continue;
                        using (var adapter = key.OpenSubKey(sub))
                        {
                            string desc = "";
                            if (adapter != null)
                            {
                                var value = adapter.GetValue("DriverDesc") as string;
                                if (value != null) desc = value.Trim();
                            }
                            if (desc.Length == 0) continue;
                            var lower = desc.ToLowerInvariant();
                            if (lower.Contains("basic display") || lower.Contains("basic render") ||
                                lower.Contains("parsec") || lower.Contains("virtual") ||
                                lower.Contains("remote") || lower.Contains("indirect")) continue;
                            if (!names.Contains(desc)) names.Add(desc);
                        }
                    }
                }
            }
        }
        catch { }
        return names;
    }

    // The card the settings are described for: the same preference as the renderer (an NVIDIA RTX
    // card, then another discrete card), else the adapter that drives the desktop.
    static string PickGpu(List<string> names, string primary)
    {
        foreach (var name in names)
            if (Regex.IsMatch(name, @"\b(RTX|GTX)\s*\d", RegexOptions.IgnoreCase)) return name;
        foreach (var name in names)
            if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return name;
        foreach (var name in names)
            if (Regex.IsMatch(name, @"Radeon\s+RX", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(name, @"Intel\S*\s+Arc\b", RegexOptions.IgnoreCase)) return name;
        foreach (var name in names)
            if (name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) < 0) return name;
        return names.Count > 0 ? names[0] : primary;
    }

    // The best settings for a PC, from what was measured on the development PC (Ryzen 5 5500,
    // RTX 5060, OVERNIGHT.md 2026-11-12): the game itself runs about 30 FPS there, limited by the
    // CPU, so the picture is smoothed with frame generation rather than by asking more of the game.
    // Frame generation needs an RTX 40 or newer; x3 and x4 need an RTX 50. In game, Auto then picks
    // the multiplier from the frame rate actually reached, so slower and faster PCs adapt by themselves.
    struct Best { public int Size, Upscale, FrameGen, Target; public bool LowLatency; public string Why; }

    static Best PickBest(PcInfo pc)
    {
        var b = new Best { Target = 2 };
        for (int i = 0; i < SizeLabels.Length; i++)
            if (SizeValues[i, 0] <= pc.Width && SizeValues[i, 1] <= pc.Height) b.Size = i;
        string why = string.Format("{0}, {1} ({2} threads), {3} GB, {4}x{5} at {6} Hz: ", pc.Gpu, pc.Cpu, pc.Threads, pc.RamGb, pc.Width, pc.Height, pc.Hz);
        if (pc.RtxSeries >= 20)
        {
            b.Upscale = 1;
            b.LowLatency = true;
        }
        if (pc.RtxSeries >= 40)
        {
            b.FrameGen = 1;
            int goal = pc.RtxSeries >= 50 && pc.Hz >= 120 ? 120 : pc.RtxSeries >= 50 && pc.Hz >= 90 ? 90 : 60;
            b.Target = Array.IndexOf(TargetValues, goal);
            why += string.Format("DLSS, frame generation Auto to {0} FPS on screen{1}, Reflex.", goal, pc.RtxSeries >= 50 ? " (x2 to x4)" : " (x2)");
        }
        else if (pc.RtxSeries >= 20)
            why += "DLSS and Reflex (frame generation needs an RTX 40 or newer), game at a steady 30.";
        else if (pc.IntelXess)
        {
            b.Upscale = 3;
            why += "Intel XeSS (frame generation and Reflex need an NVIDIA RTX), game at a steady 30.";
        }
        else
            why += "no NVIDIA RTX, so the game's own upscaling at a steady 30.";
        b.Why = why;
        return b;
    }

    // Frame generation plan: the multiplier and the game's own frame-rate limit (0 = no limit). The game
    // is never locked below 30 FPS: frame generation smooths what is shown, but the game only feels as
    // fast as its own frame rate. Auto takes the smallest multiplier that still reaches the target with
    // the game at 30 or more (60 -> 2x, 90 -> 3x, 120 -> 4x). Whole multipliers only: NVIDIA's dynamic
    // multiplier is D3D12-only.
    static void FrameGenPlan(int mode, int goal, out int multiplier, out int limit)
    {
        if (mode <= 0)
        {
            multiplier = 1;
            limit = 0;
            return;
        }
        if (mode == 1 && goal != 0)
        {
            // Auto: starts at 2x with the game capped at half the target; Kyty then raises the
            // multiplier in game whenever the game's own rate drops (ASTROHYBRID_DLSSG_TARGET).
            multiplier = 2;
            limit = Math.Max(30, goal / 2);
            return;
        }
        multiplier = mode == 1 ? 2 : mode;
        if (mode == 1 && goal == 0) multiplier = 2;
        limit = goal == 0 ? 0 : Math.Max(30, goal / multiplier);
    }

    static int ReadInt(string json, string key, int fallback, int count)
    {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\\d+)");
        int v;
        return m.Success && int.TryParse(m.Groups[1].Value, out v) && v >= 0 && v < count ? v : fallback;
    }

    static int ReadIntRange(string json, string key, int fallback, int min, int max)
    {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\\d+)");
        int v;
        return m.Success && int.TryParse(m.Groups[1].Value, out v) && v >= min && v <= max ? v : fallback;
    }

    // The "rumble" index an older launcher understands: the closest old preset, never Off for a strength above 0.
    static int LegacyRumbleIndex(int percent)
    {
        percent = Math.Max(0, Math.Min(percent, 200));
        int best = 0;
        for (int i = 1; i < LegacyRumblePercents.Length; i++)
        {
            if (Math.Abs(LegacyRumblePercents[i] - percent) < Math.Abs(LegacyRumblePercents[best] - percent)) best = i;
        }
        if (percent > 0 && LegacyRumblePercents[best] == 0) best = 1;
        return best;
    }

    static bool ReadBool(string json, string key, bool fallback)
    {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() == "true" : fallback;
    }

    static ComboBox AddRow(Form form, string text, string[] items, int selected, ref int y)
    {
        var label = new Label { Text = text, AutoSize = true, Location = new Point(18, y + 4) };
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, y), Width = 290, AccessibleName = text };
        box.Items.AddRange(items);
        box.SelectedIndex = selected;
        form.Controls.Add(label);
        form.Controls.Add(box);
        y += 36;
        return box;
    }

    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        string bat = Path.Combine(root, "Run-AstroHybrid.bat");
        if (!File.Exists(bat) || !File.Exists(Path.Combine(root, @"Build\RunHybrid\AstroBot.exe")))
        {
            MessageBox.Show("Run-AstroHybrid.bat or Build\\RunHybrid was not found next to this launcher.", "AstroBot Launcher");
            return 1;
        }
        string settingsPath = Path.Combine(root, @"Build\astrohybrid-settings.json");
        string json = "";
        try { if (File.Exists(settingsPath)) json = File.ReadAllText(settingsPath); } catch { }

        var form = new Form
        {
            Text = "Astro Bot",
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(460, 500),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
        };
        try { form.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

        int y = 14;
        var logoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
        if (logoStream != null)
        {
            var logo = new PictureBox { Image = Image.FromStream(logoStream), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96), Location = new Point(18, y) };
            form.Controls.Add(logo);
        }
        form.Controls.Add(new Label { Text = "Astro Bot", Font = new Font("Segoe UI", 18f, FontStyle.Bold), AutoSize = true, Location = new Point(128, y + 18) });
        form.Controls.Add(new Label { Text = "PC native port (AstroHybrid)", AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(131, y + 58) });
        y += 112;

        var lighting = AddRow(form, "Lighting", LightingLabels, ReadInt(json, "lighting", 0, LightingLabels.Length), ref y);
        var fps = AddRow(form, "Frame rate", FpsLabels, json.Contains("\"fpsv\"") ? ReadInt(json, "fps", 0, FpsLabels.Length) : 0, ref y);
        var speed = AddRow(form, "Game speed", SpeedLabels, ReadInt(json, "speed", 5, SpeedLabels.Length), ref y);
        var overlay = AddRow(form, "Show stats", OverlayLabels, ReadInt(json, "overlay", 0, OverlayLabels.Length), ref y);
        var size = AddRow(form, "Window size", SizeLabels, ReadInt(json, "size", 2, SizeLabels.Length), ref y);
        var fullscreen = new CheckBox { Text = "Start in fullscreen", AutoSize = true, Checked = ReadBool(json, "fullscreen", true), Location = new Point(150, y) };
        form.Controls.Add(fullscreen);
        y += 34;
        var ultrawide = new CheckBox { Text = "Ultrawide 21:9 rendering", AutoSize = true, Checked = ReadBool(json, "ultrawide", false), Location = new Point(150, y) };
        form.Controls.Add(ultrawide);
        y += 34;
        form.Controls.Add(new Label { Text = "Upscaling and NVIDIA RTX", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true, Location = new Point(18, y) });
        y += 24;
        var upscale = AddRow(form, "Upscaling", UpscaleLabels, ReadInt(json, "upscale", 0, UpscaleLabels.Length), ref y);
        var framegen = AddRow(form, "Frame generation", FrameGenLabels, ReadInt(json, "fgmode", 0, FrameGenLabels.Length), ref y);
        var target = AddRow(form, "Frame gen target", TargetLabels, ReadInt(json, "fgtarget", 2, TargetLabels.Length), ref y);
        var targetNote = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(150, y - 6) };
        form.Controls.Add(targetNote);
        y += 16;
        Action updateNote = () =>
        {
            int mult, limit;
            FrameGenPlan(framegen.SelectedIndex, TargetValues[target.SelectedIndex], out mult, out limit);
            target.Enabled = framegen.SelectedIndex != 0;
            int goalShown = TargetValues[target.SelectedIndex];
            targetNote.Text = framegen.SelectedIndex == 0 ? "" : framegen.SelectedIndex == 1 && goalShown != 0
                ? string.Format("Game up to {0} FPS; x2 to x4 picked in game to stay at {1}+", limit, goalShown)
                : limit == 0
                ? string.Format("Game unlocked, x{0} on screen (e.g. 32 -> {1})", mult, 32 * mult)
                : string.Format("Game up to {0} FPS x{1} = {2} on screen", limit, mult, limit * mult);
        };
        framegen.SelectedIndexChanged += (s, e) => updateNote();
        target.SelectedIndexChanged += (s, e) => updateNote();
        updateNote();
        var lowlat = new CheckBox { Text = "NVIDIA low latency (Reflex)", AutoSize = true, Checked = ReadBool(json, "lowlatency", false), Location = new Point(150, y) };
        form.Controls.Add(lowlat);
        y += 30;
        form.Controls.Add(new Label
        {
            Location = new Point(18, y),
            Size = new Size(424, 46),
            ForeColor = SystemColors.GrayText,
            Text = "Frame generation multiplies the frames you see (30 FPS limit x2 = 60 on screen) and turns Reflex on itself. " +
                   "It runs only while the game window is focused. Do not also use NVIDIA Smooth Motion.",
        });
        y += 52;
        form.Controls.Add(new Label { Text = "Controls", Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true, Location = new Point(18, y) });
        y += 24;
        var pad = AddRow(form, "Controller", PadLabels, ReadInt(json, "pad", 0, PadLabels.Length), ref y);
        // Strength in percent (#36): "rumblepct" when the file has it, else the old preset index in "rumble".
        int rumblePercent = ReadIntRange(json, "rumblepct", -1, 0, 200);
        if (rumblePercent < 0)
        {
            int legacy = ReadInt(json, "rumble", 2, LegacyRumblePercents.Length);
            rumblePercent = LegacyRumblePercents[Math.Max(0, Math.Min(legacy, LegacyRumblePercents.Length - 1))];
        }
        int rumblePreset = Array.IndexOf(RumblePresets, rumblePercent);
        bool customRumble = rumblePreset < 0;
        if (customRumble) rumblePreset = RumblePresets.Length - 1;
        var rumble = AddRow(form, "Vibration", RumbleLabels, rumblePreset, ref y);
        var rumbleCustom = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 200,
            Value = rumblePercent,
            Width = 70,
            Location = new Point(150, y),
            Enabled = customRumble,
            AccessibleName = "Custom vibration strength in percent",
        };
        form.Controls.Add(rumbleCustom);
        form.Controls.Add(new Label { Text = "Custom strength (%) - pick Custom above to use it", AutoSize = true, Location = new Point(228, y + 4) });
        Func<int> chosenRumble = () => RumblePresets[rumble.SelectedIndex] >= 0 ? RumblePresets[rumble.SelectedIndex] : (int)rumbleCustom.Value;
        rumble.SelectedIndexChanged += (s, e) =>
        {
            rumbleCustom.Enabled = RumblePresets[rumble.SelectedIndex] < 0;
            if (!rumbleCustom.Enabled) rumbleCustom.Value = chosenRumble();
        };
        y += 30;
        var switchBoost = new CheckBox { Text = "Stronger vibration on Switch-style pads (third-party ones)", AutoSize = true, Checked = ReadBool(json, "switchboost", true), Location = new Point(150, y) };
        form.Controls.Add(switchBoost);
        y += 22;
        form.Controls.Add(new Label
        {
            Location = new Point(18, y),
            Size = new Size(424, 60),
            ForeColor = SystemColors.GrayText,
            Text = "Vibration sets Xbox, Switch and other pads; a DualSense keeps its own haptics. Pick Custom to type any strength from 0 to 200%. " +
                   "The Switch boost follows the strength (100% or more is what it used to be). " +
                   "Pads without motion sensors (Xbox): press View / Back to tilt with the right stick, again to stop.",
        });
        y += 64;
        var bestButton = new Button { Text = "Best settings for this PC", AutoSize = true, Location = new Point(18, y) };
        form.Controls.Add(bestButton);
        var bestNote = new Label { Location = new Point(18, y + 32), Size = new Size(424, 48), ForeColor = SystemColors.GrayText };
        form.Controls.Add(bestNote);
        Action applyBest = () =>
        {
            var best = PickBest(DetectPc());
            lighting.SelectedIndex = 0;
            fps.SelectedIndex = 0;
            speed.SelectedIndex = 0;
            size.SelectedIndex = best.Size;
            fullscreen.Checked = true;
            upscale.SelectedIndex = best.Upscale;
            framegen.SelectedIndex = best.FrameGen;
            target.SelectedIndex = best.Target < 0 ? 2 : best.Target;
            lowlat.Checked = best.LowLatency;
            pad.SelectedIndex = 0;
            bestNote.Text = "Set for " + best.Why;
        };
        bestButton.Click += (s, e) => applyBest();
        if (!File.Exists(settingsPath)) applyBest();
        y += 84;
        form.Controls.Add(new Label
        {
            Location = new Point(18, y),
            Size = new Size(424, 70),
            Text = "In game: F2 performance overlay, Ctrl+F2 changes the frame-rate limit, F11 or Alt+Enter toggles fullscreen.\r\n\r\n" +
                   "The game always runs in real time; pick a game speed below 100% for a slower game.",
        });

        var play = new Button { Text = "Play", Size = new Size(100, 30), Location = new Point(236, y + 76) };
        var cancel = new Button { Text = "Cancel", Size = new Size(100, 30), Location = new Point(342, y + 76), DialogResult = DialogResult.Cancel };
        form.ClientSize = new Size(460, y + 120);
        play.Click += (s, e) =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, string.Format(
                    "{{\r\n    \"lighting\":  {0},\r\n    \"fps\":  {1},\r\n    \"size\":  {2},\r\n    \"fullscreen\":  {3},\r\n    \"ultrawide\":  {4},\r\n    \"realspeed\":  {5},\r\n    \"speed\":  {6},\r\n    \"overlay\":  {7},\r\n    \"upscale\":  {8},\r\n    \"fgmode\":  {9},\r\n    \"lowlatency\":  {10},\r\n    \"fgtarget\":  {11},\r\n    \"pad\":  {12},\r\n    \"rumble\":  {13},\r\n    \"rumblepct\":  {14},\r\n    \"switchboost\":  {15},\r\n    \"fpsv\":  2\r\n}}",
                    lighting.SelectedIndex, fps.SelectedIndex, size.SelectedIndex,
                    fullscreen.Checked ? "true" : "false", ultrawide.Checked ? "true" : "false", "true", speed.SelectedIndex, overlay.SelectedIndex,
                    upscale.SelectedIndex, framegen.SelectedIndex, lowlat.Checked ? "true" : "false",
                    target.SelectedIndex, pad.SelectedIndex, LegacyRumbleIndex(chosenRumble()), chosenRumble(),
                    switchBoost.Checked ? "true" : "false"));
            }
            catch { }
            form.DialogResult = DialogResult.OK;
        };
        form.AcceptButton = play;
        form.CancelButton = cancel;
        form.Controls.Add(play);
        form.Controls.Add(cancel);
        if (form.ShowDialog() != DialogResult.OK) return 0;

        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/c \"\"" + bat + "\"\"")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Game builds made by the AstroHybrid app's setup from another game version record what they
        // support in astrohybrid-game.json: the faster-lighting exe may be missing, and real-time speed
        // (one global at a fixed address) is only known for 01.019.000. No file: the original install.
        string runHybrid = Path.Combine(root, @"Build\RunHybrid");
        string features = "";
        try { features = File.ReadAllText(Path.Combine(runHybrid, "astrohybrid-game.json")); } catch { }
        bool realTimeKnown = features.Length == 0 || Regex.IsMatch(features, "\"RealTimeSpeed\"\\s*:\\s*true");
        bool useUltrawide = ultrawide.Checked;
        string exeName = useUltrawide
            ? (lighting.SelectedIndex == 0 ? "AstroBot-NonRT-Ultrawide-SoftAssert.exe" : "AstroBot-Ultrawide.exe")
            : LightingExes[lighting.SelectedIndex];
        if (lighting.SelectedIndex == 0 && !File.Exists(Path.Combine(runHybrid, exeName)))
            exeName = useUltrawide ? "AstroBot-NonRT-Ultrawide.exe" : "AstroBot-NonRT.exe";
        psi.EnvironmentVariables["ASTROHYBRID_EXE"] = File.Exists(Path.Combine(runHybrid, exeName)) ? exeName : "AstroBot.exe";
        psi.EnvironmentVariables["ASTROHYBRID_FPS_LIMIT"] = FpsValues[fps.SelectedIndex];
        psi.EnvironmentVariables["KYTY_GPU_WIDTH"] = SizeValues[size.SelectedIndex, 0].ToString();
        psi.EnvironmentVariables["KYTY_GPU_HEIGHT"] = SizeValues[size.SelectedIndex, 1].ToString();
        psi.EnvironmentVariables["KYTY_GPU_FULLSCREEN"] = fullscreen.Checked ? "1" : "0";
        // Real-time game speed is on; keep commit-epoch fast path off and buffer-range fixes on for cloth/bubbles (#30, #35).
        psi.EnvironmentVariables["ASTROHYBRID_GAME_SPEED"] = realTimeKnown ? "1" : "0";
        psi.EnvironmentVariables["ASTROHYBRID_COMMIT_EPOCH_FASTPATH"] = "0";
        psi.EnvironmentVariables["KYTY_BUFFER_RANGE_FIXES"] = "1";
        psi.EnvironmentVariables["ASTROHYBRID_PAD_PREFER"] = PadValues[pad.SelectedIndex];
        // Strength in percent (#36). The Switch boost follows it: 96 of 255 at 100% (the old flat value),
        // less at lower strengths so a light setting stays light on Joy-Cons and Switch Pro pads.
        int rumbleStrength = chosenRumble();
        psi.EnvironmentVariables["ASTROHYBRID_RUMBLE_SCALE"] = rumbleStrength.ToString();
        psi.EnvironmentVariables["ASTROHYBRID_RUMBLE_SWITCH_FLOOR"] = switchBoost.Checked ? (96 * rumbleStrength / 100).ToString() : "0";
        psi.EnvironmentVariables["ASTROHYBRID_GAME_SPEED_PERCENT"] = SpeedValues[speed.SelectedIndex];
        psi.EnvironmentVariables["ASTROHYBRID_OVERLAY"] = OverlayValues[overlay.SelectedIndex];
        psi.EnvironmentVariables["ASTROHYBRID_WINDOW_TITLE"] = "Astro Bot";
        // NVIDIA RTX options (OVERNIGHT.md 2026-11-12): DLSS and frame generation need the CUDA address
        // window in the PS5 memory map and the NVIDIA runtime files next to the game.
        if (upscale.SelectedIndex == 1 || framegen.SelectedIndex != 0)
        {
            psi.EnvironmentVariables["ASTROHYBRID_CUDA_HOLE"] = "1";
            psi.EnvironmentVariables["ASTROHYBRID_DLSS_PATH"] = Path.Combine(root, @"Build\RunHybrid");
        }
        if (upscale.SelectedIndex == 1) psi.EnvironmentVariables["ASTROHYBRID_DLSS"] = "1";
        // AMD FSR and Intel XeSS (kyty_gpu.dll, dlss.h / xess.h): their runtimes next to the game.
        if (upscale.SelectedIndex == 2)
        {
            psi.EnvironmentVariables["ASTROHYBRID_FSR"] = "1";
            psi.EnvironmentVariables["ASTROHYBRID_FSR_PATH"] = Path.Combine(root, @"Build\RunHybrid");
        }
        if (upscale.SelectedIndex == 3)
        {
            psi.EnvironmentVariables["ASTROHYBRID_XESS"] = "1";
            // libxess.dll next to the game, else in the install folder (dropped there by hand).
            psi.EnvironmentVariables["ASTROHYBRID_XESS_PATH"] = File.Exists(Path.Combine(root, @"Build\RunHybrid\libxess.dll")) || !File.Exists(Path.Combine(root, "libxess.dll"))
                ? Path.Combine(root, @"Build\RunHybrid") : root;
        }
        if (framegen.SelectedIndex != 0)
        {
            int mult, limit;
            FrameGenPlan(framegen.SelectedIndex, TargetValues[target.SelectedIndex], out mult, out limit);
            psi.EnvironmentVariables["ASTROHYBRID_DLSSG"] = "1";
            psi.EnvironmentVariables["ASTROHYBRID_DLSSG_FRAMES"] = (mult - 1).ToString();
            if (framegen.SelectedIndex == 1 && TargetValues[target.SelectedIndex] != 0)
            {
                psi.EnvironmentVariables["ASTROHYBRID_DLSSG_TARGET"] = TargetValues[target.SelectedIndex].ToString();
                psi.EnvironmentVariables["ASTROHYBRID_DLSSG_MAX_FRAMES"] = "3";
            }
            psi.EnvironmentVariables["ASTROHYBRID_FPS_LIMIT"] = limit == 0 ? "unlimited" : limit.ToString();
        }
        if (lowlat.Checked) psi.EnvironmentVariables["ASTROHYBRID_LOW_LATENCY"] = "1";
        try
        {
            using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AstroBot Launcher");
            return 1;
        }
    }
}
