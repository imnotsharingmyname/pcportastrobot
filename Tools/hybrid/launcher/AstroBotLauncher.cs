// AstroBot Launcher: one window to pick the settings, then it starts AstroHybrid
// (Run-AstroHybrid.bat, no console window). Settings are shared with launcher.ps1
// (Build\astrohybrid-settings.json). Environment variables are set for this launch only.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class AstroBotLauncher
{
    static readonly string[] LightingLabels = { "Faster lighting (recommended)", "Original lighting (slower)" };
    static readonly string[] LightingExes = { "AstroBot-NonRT.exe", "AstroBot.exe" };
    static readonly string[] FpsLabels = { "30 FPS - steady (recommended)", "35 FPS", "45 FPS", "50 FPS", "55 FPS", "60 FPS", "No limit" };
    static readonly string[] FpsValues = { "30", "35", "45", "50", "55", "60", "unlimited" };
    static readonly string[] SpeedLabels = { "100% - real time", "95%", "90%", "85%", "80%", "75% (recommended)" };
    static readonly string[] SpeedValues = { "100", "95", "90", "85", "80", "75" };
    static readonly string[] OverlayLabels = { "Off (F2 shows it)", "FPS only", "FPS + CPU, GPU, VRAM, RAM", "Everything (all stats)" };
    static readonly string[] OverlayValues = { "", "fps", "compact", "full" };
    static readonly string[] SizeLabels = { "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440" };
    static readonly int[,] SizeValues = { { 1280, 720 }, { 1600, 900 }, { 1920, 1080 }, { 2560, 1440 } };

    static int ReadInt(string json, string key, int fallback, int count)
    {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\\d+)");
        int v;
        return m.Success && int.TryParse(m.Groups[1].Value, out v) && v >= 0 && v < count ? v : fallback;
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
        y += 28;
        var realspeed = new CheckBox { Text = "Real-time game speed (no slow motion below 60 FPS)", AutoSize = true, Checked = ReadBool(json, "realspeed", true), Location = new Point(150, y) };
        form.Controls.Add(realspeed);
        y += 34;
        form.Controls.Add(new Label
        {
            Location = new Point(18, y),
            Size = new Size(424, 70),
            Text = "In game: F2 performance overlay, Ctrl+F2 changes the frame-rate limit, F11 or Alt+Enter toggles fullscreen.\r\n\r\n" +
                   "If real time feels too fast, pick a game speed below 100%.",
        });

        var play = new Button { Text = "Play", Size = new Size(100, 30), Location = new Point(236, 426) };
        var cancel = new Button { Text = "Cancel", Size = new Size(100, 30), Location = new Point(342, 426), DialogResult = DialogResult.Cancel };
        play.Click += (s, e) =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, string.Format(
                    "{{\r\n    \"lighting\":  {0},\r\n    \"fps\":  {1},\r\n    \"size\":  {2},\r\n    \"fullscreen\":  {3},\r\n    \"realspeed\":  {4},\r\n    \"speed\":  {5},\r\n    \"overlay\":  {6},\r\n    \"fpsv\":  2\r\n}}",
                    lighting.SelectedIndex, fps.SelectedIndex, size.SelectedIndex,
                    fullscreen.Checked ? "true" : "false", realspeed.Checked ? "true" : "false", speed.SelectedIndex, overlay.SelectedIndex));
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
        psi.EnvironmentVariables["ASTROHYBRID_EXE"] = LightingExes[lighting.SelectedIndex];
        psi.EnvironmentVariables["ASTROHYBRID_FPS_LIMIT"] = FpsValues[fps.SelectedIndex];
        psi.EnvironmentVariables["KYTY_GPU_WIDTH"] = SizeValues[size.SelectedIndex, 0].ToString();
        psi.EnvironmentVariables["KYTY_GPU_HEIGHT"] = SizeValues[size.SelectedIndex, 1].ToString();
        psi.EnvironmentVariables["KYTY_GPU_FULLSCREEN"] = fullscreen.Checked ? "1" : "0";
        psi.EnvironmentVariables["ASTROHYBRID_GAME_SPEED"] = realspeed.Checked ? "1" : "0";
        psi.EnvironmentVariables["ASTROHYBRID_GAME_SPEED_PERCENT"] = SpeedValues[speed.SelectedIndex];
        psi.EnvironmentVariables["ASTROHYBRID_OVERLAY"] = OverlayValues[overlay.SelectedIndex];
        psi.EnvironmentVariables["ASTROHYBRID_WINDOW_TITLE"] = "Astro Bot";
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
