using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AstroHybrid.App.Models;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App.ViewModels
{
    public class InstallationViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainViewModel;

        public InstallationViewModel(MainWindowViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += (sender, e) => OnPropertyChanged(string.Empty);
            RepairCommand = new RelayCommand(() => _mainViewModel.RepairInstallationCommand.Execute(null));
            RebuildCommand = new RelayCommand(() => _mainViewModel.RebuildInstallationCommand.Execute(null));
            ChangeLocationCommand = new RelayCommand(() => _mainViewModel.ChangeInstallLocationCommand.Execute(null));
            OpenInstallFolderCommand = new RelayCommand(() => _mainViewModel.OpenInstallFolderCommand.Execute(null));
            OpenLogFolderCommand = new RelayCommand(() => _mainViewModel.OpenLogFolderCommand.Execute(null));
            UninstallCommand = new RelayCommand(() => _mainViewModel.UninstallCommand.Execute(null));
            RunSetupAgainCommand = new RelayCommand(RunSetupAgain);
        }

        public InstallationState? State => _mainViewModel.InstallationState;

        public string InstallPath => _mainViewModel.InstallationState?.InstallPath ?? "Not installed";

        public string GameDumpPath => _mainViewModel.InstallationState?.GameDumpPath ?? "Not configured";

        public string GameVersion => _mainViewModel.InstallationState?.GameVersion ?? "Unknown";

        public string GameTitleId => _mainViewModel.InstallationState?.GameTitleId ?? "Unknown";

        public string InstalledAt => _mainViewModel.InstallationState?.InstalledAt?.ToString("yyyy-MM-dd HH:mm") ?? "Never";

        public string InstallSize => _mainViewModel.InstallationState?.InstallSizeBytes > 0
            ? FormatBytes(_mainViewModel.InstallationState.InstallSizeBytes)
            : "Unknown";

        public bool IsInstalled => _mainViewModel.InstallationState?.IsInstalled == true;

        public string AnyPs5Version => _mainViewModel.Settings.InstallationManifest?.AnyPs5Version ?? "Unknown";

        public string KytyVersion => _mainViewModel.Settings.InstallationManifest?.KytyVersion ?? "Unknown";

        public ICommand RepairCommand { get; }
        public ICommand RebuildCommand { get; }
        public ICommand ChangeLocationCommand { get; }
        public ICommand OpenInstallFolderCommand { get; }
        public ICommand OpenLogFolderCommand { get; }
        public ICommand UninstallCommand { get; }
        public ICommand RunSetupAgainCommand { get; }

        private void RunSetupAgain()
        {
            _mainViewModel.Settings.FirstRunComplete = false;
            _mainViewModel.SettingsManager.Save(_mainViewModel.Settings);
            _mainViewModel.CurrentPage = LauncherPage.Home;
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:F1} {suffixes[i]}";
        }
    }
}