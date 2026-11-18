using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AstroHybrid.App.Avalonia.Services;

namespace AstroHybrid.App.Avalonia.Views;

public partial class InstallationView : UserControl, IPageRefresh
{
    private readonly Backend _backend;
    private readonly AppSettings _settings;
    private bool _confirmRemove;

    public InstallationView(Backend backend, AppSettings settings)
    {
        InitializeComponent();
        _backend = backend;
        _settings = settings;

        Loaded += (_, _) => RefreshPage();
    }

    public void RefreshPage()
    {
        var installed = _backend.HybridReady;
        var dump = Directory.Exists(_settings.GamePath);

        EmptyStateCard.IsVisible = !installed;
        StatusCard.IsVisible = installed || dump;
        SizeCard.IsVisible = installed;
        ToolsCard.IsVisible = installed;

        StatusValue.Text = installed ? "Installed" : dump ? "Not installed" : "Game dump not found";
        StatusValue.Foreground = this.FindResource(installed ? "SuccessBrush" : "WarningBrush") as IBrush;

        InstallPathValue.Text = _backend.HybridDirectory;
        DumpValue.Text = dump ? _settings.GamePath : "Not configured";

        var info = _backend.GameInfo;
        VersionValue.Text = info.Version.Length > 0 ? info.Version : "—";
        TitleIdValue.Text = info.TitleId.Length > 0 ? info.TitleId : "PPSA21567";
        InstalledValue.Text = _backend.InstalledAt;

        var (anyPs5, kyty) = _backend.ToolVersions();
        AnyPs5Value.Text = anyPs5;
        KytyValue.Text = kyty;

        SizeValue.Text = "…";
        _ = LoadSizeAsync();

        _confirmRemove = false;
        RemoveButton.Content = "Remove Runtime";
    }

    private async System.Threading.Tasks.Task LoadSizeAsync()
    {
        var size = await _backend.InstallSizeAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
            SizeValue.Text = size.Length > 0 ? size : "—");
    }

    private async void RunSetup(object? sender, RoutedEventArgs e)
    {
        var window = new SetupWindow(_backend, _settings);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
        RefreshPage();
    }

    private void OpenInstallFolder(object? sender, RoutedEventArgs e) => Open(_backend.HybridDirectory);

    private void OpenLogFolder(object? sender, RoutedEventArgs e) => Open(_backend.LogDirectory);

    private static void Open(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false });
            }
        }
        catch
        {
            // Opening a folder is best effort.
        }
    }

    private void RemoveRuntime(object? sender, RoutedEventArgs e)
    {
        if (!_confirmRemove)
        {
            _confirmRemove = true;
            RemoveButton.Content = "Remove Runtime — click again to confirm";
            return;
        }

        _backend.RemoveRuntime();
        RefreshPage();
    }
}
