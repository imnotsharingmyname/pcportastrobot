using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class SetupView : UserControl
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;

    public SetupView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;
        GamePathBox.Text = settings.GamePath;
        Loaded += (_, _) => GamePathBox.Text = _settings.GamePath;
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
            }
        }
    }

    private async void Check(object? sender, RoutedEventArgs e)
    {
        var path = GamePathBox.Text?.Trim() ?? "";
        if (!Directory.Exists(path))
        {
            Append("Game folder not found.");
            return;
        }

        SetBusy(true);
        var result = await _backend.CheckAsync(path, onLine: Append);
        Append(result.Success ? "Check passed." : $"Check failed (exit {result.ExitCode}).");
        SetBusy(false);
    }

    private async void Install(object? sender, RoutedEventArgs e)
    {
        var path = GamePathBox.Text?.Trim() ?? "";
        if (!Directory.Exists(path))
        {
            Append("Choose the extracted PPSA21567-app folder first.");
            return;
        }

        _settings.GamePath = path;
        _backend.SaveSettings(_settings);
        SetBusy(true);

        try
        {
            if (!File.Exists(Path.Combine(_backend.RunDirectory, "AstroBot")))
            {
                Append("== relinking the base runtime");
                var baseResult = await _backend.SetupAsync(path, false, Append);
                if (!baseResult.Success)
                {
                    Append($"Base setup failed (exit {baseResult.ExitCode}).");
                    return;
                }
            }

            Append("== laying out the hybrid runtime");
            var hybridResult = await _backend.SetupAsync(path, true, Append);
            Append(hybridResult.Success
                ? "Install complete. Return to Home and press Play."
                : $"Hybrid setup failed (exit {hybridResult.ExitCode}).");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Progress.IsVisible = busy;
        InstallButton.IsEnabled = !busy;
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
