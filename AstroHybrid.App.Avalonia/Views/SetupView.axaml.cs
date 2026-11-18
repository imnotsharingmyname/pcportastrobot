using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class SetupView : UserControl, IPageRefresh
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;

    public SetupView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        GamePathBox.Text = settings.GamePath;
        Loaded += (_, _) => RefreshPage();
    }

    public void RefreshPage()
    {
        var ready = _backend.HybridReady;
        var dump = Directory.Exists(_settings.GamePath);

        RuntimeValue.Text = ready ? "Installed" : "Not installed";
        DumpValue.Text = dump ? "Found" : "Not found";
        GamePathValue.Text = string.IsNullOrWhiteSpace(_settings.GamePath) ? "—" : _settings.GamePath;
        HybridPathValue.Text = _backend.HybridDirectory;

        if (!GamePathBox.IsFocused)
        {
            GamePathBox.Text = _settings.GamePath;
        }


    }

    private async void Browse(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the extracted PPSA21567-app folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                GamePathBox.Text = path;
                SaveGamePath(path);
                RefreshPage();
                _backend.SetStatus("Game folder selected");
            }
        }
    }

    private void OnGamePathLostFocus(object? sender, RoutedEventArgs e)
    {
        SaveGamePath(GamePathBox.Text?.Trim() ?? "");
        RefreshPage();
    }

    private void SaveGamePath(string path)
    {
        _settings.GamePath = path;
        _backend.SaveSettings(_settings);
    }

    private async void Check(object? sender, RoutedEventArgs e)
    {
        var path = GamePathBox.Text?.Trim() ?? "";
        if (!Directory.Exists(path))
        {
            Append("Game folder not found.");
            _backend.SetStatus("Game dump not found");
            return;
        }

        SaveGamePath(path);
        SetBusy(true);
        _backend.SetStatus("Checking the game folder…", true);
        var result = await _backend.CheckAsync(path, onLine: Append);
        Append(result.Success ? "Check passed." : $"Check failed (exit {result.ExitCode}).");
        SetBusy(false);
        _backend.SetStatus(result.Success ? "Check passed" : $"Check failed (exit {result.ExitCode})");
        RefreshPage();
    }

    private async void Install(object? sender, RoutedEventArgs e)
    {
        var path = GamePathBox.Text?.Trim() ?? "";
        if (!Directory.Exists(path))
        {
            Append("Choose the extracted PPSA21567-app folder first.");
            _backend.SetStatus("Game dump not found");
            return;
        }

        SaveGamePath(path);
        SetBusy(true);

        try
        {
            if (!File.Exists(Path.Combine(_backend.RunDirectory, "AstroBot")))
            {
                Append("== relinking the base runtime");
                _backend.SetStatus("Building the base runtime…", true);
                var baseResult = await _backend.SetupAsync(path, false, Append);
                if (!baseResult.Success)
                {
                    Append($"Base setup failed (exit {baseResult.ExitCode}).");
                    _backend.SetStatus($"Base setup failed (exit {baseResult.ExitCode})");
                    return;
                }
            }

            Append("== laying out the hybrid runtime");
            _backend.SetStatus("Laying out the hybrid runtime…", true);
            var hybridResult = await _backend.SetupAsync(path, true, Append);
            if (hybridResult.Success)
            {
                Append("Install complete. Press Play in the bottom bar.");
                _backend.SetStatus("Install complete — ready to play");
            }
            else
            {
                Append($"Hybrid setup failed (exit {hybridResult.ExitCode}).");
                _backend.SetStatus($"Hybrid setup failed (exit {hybridResult.ExitCode})");
            }
        }
        finally
        {
            SetBusy(false);
            RefreshPage();
        }
    }

    private void SetBusy(bool busy)
    {
        Progress.IsVisible = busy;
        InstallButton.IsEnabled = !busy;
        CheckButton.IsEnabled = !busy;
        if (!busy)
        {
            _backend.SetStatus(_backend.StatusMessage);
        }
    }

    private void OpenLogs(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_backend.LogDirectory);
            Process.Start(new ProcessStartInfo("xdg-open")
            {
                ArgumentList = { _backend.LogDirectory },
                UseShellExecute = false,
            });
        }
        catch (Exception exception)
        {
            _backend.SetStatus($"Could not open the folder: {exception.Message}");
        }
    }

    private void Append(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OutputBox.Text = (OutputBox.Text ?? "") + line + Environment.NewLine;
            OutputBox.CaretIndex = OutputBox.Text?.Length ?? 0;
        });
    }
}
