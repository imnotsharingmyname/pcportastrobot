using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class AdvancedView : UserControl, IPageRefresh
{
    private static readonly string[] LightingLabels =
    {
        "OFF - Faster lighting (Non-RT, recommended)",
        "ON - Ray Tracing (Original lighting)",
    };

    private static readonly string[] SpeedLabels =
    {
        "100% - real time", "95%", "90%", "85%", "80%", "75% (recommended)",
    };

    private static readonly string[] LanguageLabels =
    {
        "Auto (desktop language)", "Japanese", "English (United States)", "French", "Spanish", "German",
        "Italian", "Dutch", "Portuguese (Portugal)", "Russian", "Korean", "Chinese (Traditional)",
        "Chinese (Simplified)", "Finnish", "Swedish", "Danish", "Norwegian", "Polish",
        "Portuguese (Brazil)", "English (United Kingdom)", "Turkish", "Spanish (Latin America)",
        "Arabic", "French (Canada)", "Czech", "Hungarian", "Greek", "Romanian", "Thai", "Vietnamese",
        "Indonesian", "Ukrainian",
    };

    private static readonly string[] PriorityLabels = { "normal", "abovenormal", "high" };
    private static readonly string[] PriorityValues = { "normal", "abovenormal", "high" };
    private static readonly string[] CpPriorityLabels = { "normal", "above normal", "high" };

    // WPF keeps the last 500 lines; a ring keeps the log bounded while the game runs.
    private const int MaxLogLines = 500;

    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private readonly LinkedList<string> _logLines = new();
    private bool _loading = true;
    private bool _logDirty;

    public AdvancedView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;

        LightingBox.ItemsSource = LightingLabels;
        SpeedBox.ItemsSource = SpeedLabels;
        LanguageBox.ItemsSource = LanguageLabels;
        PriorityBox.ItemsSource = PriorityLabels;
        CpPriorityBox.ItemsSource = CpPriorityLabels;

        _backend.GameOutput += OnGameOutput;
        Loaded += (_, _) => RefreshPage();
        Unloaded += (_, _) => _backend.GameOutput -= OnGameOutput;
    }

    public void RefreshPage()
    {
        _loading = true;

        LightingBox.SelectedIndex = Math.Clamp(_settings.Lighting, 0, LightingLabels.Length - 1);
        SpeedBox.SelectedIndex = Math.Clamp((100 - _settings.GameSpeedPercent) / 5, 0, 5);
        LanguageBox.SelectedIndex = Math.Clamp(_settings.Language + 1, 0, LanguageLabels.Length - 1);

        var priorityIndex = Array.IndexOf(PriorityValues, _settings.Priority);
        PriorityBox.SelectedIndex = priorityIndex >= 0 ? priorityIndex : 0;
        CpPriorityBox.SelectedIndex = Math.Clamp(_settings.CpPriority, 0, 2);

        HangWatchdogBox.IsChecked = _settings.HangWatchdog;
        MotionBox.IsChecked = _settings.PadMotion;
        TriggerBox.IsChecked = _settings.TriggerState;
        DsSpeakerBox.IsChecked = _settings.DsWirelessSpeaker;
        DsHapticsBox.IsChecked = _settings.DsWirelessHaptics;
        ShaderCacheBox.IsChecked = _settings.ShaderCache;
        DrawPrepBox.IsChecked = _settings.DrawPrep;
        SkipIntroBox.IsChecked = _settings.SkipIntro;

        DirectAudioBox.IsChecked = _settings.DirectAudio;
        AudioSinkBox.Text = _settings.AudioSink;

        RuntimeText.Text =
            $"project:    {_backend.Root}\n" +
            $"python:     {_backend.Python}\n" +
            $"runtime:    {_backend.HybridDirectory}\n" +
            $"hybrid:     {(_backend.HybridReady ? "installed" : "missing")}";

        _loading = false;
        FlushLog();
    }

    private void OnCommitted(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Lighting = Math.Max(0, LightingBox.SelectedIndex);
        _settings.GameSpeedPercent = 100 - Math.Max(0, SpeedBox.SelectedIndex) * 5;
        _settings.Language = Math.Max(0, LanguageBox.SelectedIndex) - 1;
        _settings.Priority = PriorityBox.SelectedIndex >= 0 ? PriorityValues[PriorityBox.SelectedIndex] : "normal";
        _settings.CpPriority = Math.Max(0, CpPriorityBox.SelectedIndex);

        Save();
    }

    private void OnCheckCommitted(object? sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.HangWatchdog = HangWatchdogBox.IsChecked == true;
        _settings.PadMotion = MotionBox.IsChecked == true;
        _settings.TriggerState = TriggerBox.IsChecked == true;
        _settings.DsWirelessSpeaker = DsSpeakerBox.IsChecked == true;
        _settings.DsWirelessHaptics = DsHapticsBox.IsChecked == true;
        _settings.ShaderCache = ShaderCacheBox.IsChecked == true;
        _settings.DrawPrep = DrawPrepBox.IsChecked == true;
        _settings.SkipIntro = SkipIntroBox.IsChecked == true;
        _settings.DirectAudio = DirectAudioBox.IsChecked == true;

        Save();
    }

    private void OnAudioSinkLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.AudioSink = AudioSinkBox.Text ?? "";
        Save();
    }

    private void Save()
    {
        _backend.SaveSettings(_settings);
        _backend.SetStatus("Settings saved");
    }

    private void OnGameOutput(string line)
    {
        lock (_logLines)
        {
            _logLines.AddLast(line);
            while (_logLines.Count > MaxLogLines)
            {
                _logLines.RemoveFirst();
            }

            if (_logDirty)
            {
                return;
            }

            _logDirty = true;
        }

        // Batch redraws instead of rebuilding the whole document per line.
        DispatcherTimer.RunOnce(FlushLog, TimeSpan.FromMilliseconds(100));
    }

    private void FlushLog()
    {
        lock (_logLines)
        {
            _logDirty = false;
            LogBox.Text = string.Join(Environment.NewLine, _logLines);
        }

        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void ClearLog(object? sender, RoutedEventArgs e)
    {
        lock (_logLines)
        {
            _logLines.Clear();
        }

        LogBox.Text = "";
    }
}
