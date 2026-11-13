using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

namespace AstroHybrid.App.ViewModels
{
    public class SetupWizardViewModel : ViewModelBase
    {
        private readonly GameValidator _gameValidator;
        private readonly BuildManager _buildManager;
        private readonly SettingsManager _settingsManager;
        private readonly string _rootDirectory;

        private string _gameDumpPath = "";
        private GameInfo? _gameInfo;
        private bool _isValidating;
        private bool _isInstalling;
        private string _statusMessage = "Welcome to AstroHybrid Setup";
        private int _progressPercent;
        private SetupStep? _currentStep;
        private bool _canProceed;
        private string _validationError = "";
        private bool _showError;

        public SetupWizardViewModel(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            _gameValidator = new GameValidator();
            _buildManager = new BuildManager(rootDirectory);
            _settingsManager = new SettingsManager(rootDirectory);

            _buildManager.ProgressChanged += msg => StatusMessage = msg;
            _buildManager.ProgressPercentChanged += pct => ProgressPercent = pct;
            _buildManager.ErrorOccurred += err =>
            {
                ValidationError = err;
                ShowError = true;
            };

            BrowseCommand = new RelayCommand(BrowseForGameFolder);
            ValidateCommand = new RelayCommand(async () => await ValidateGameDumpAsync(), () => !IsValidating && !string.IsNullOrWhiteSpace(GameDumpPath));
            InstallCommand = new RelayCommand(async () => await InstallAsync(), () => !IsInstalling && GameInfo?.IsValid == true);
            CancelCommand = new RelayCommand(Cancel);
        }

        public string GameDumpPath
        {
            get => _gameDumpPath;
            set => SetProperty(ref _gameDumpPath, value);
        }

        public GameInfo? GameInfo
        {
            get => _gameInfo;
            set => SetProperty(ref _gameInfo, value);
        }

        public bool IsValidating
        {
            get => _isValidating;
            set => SetProperty(ref _isValidating, value);
        }

        public bool IsInstalling
        {
            get => _isInstalling;
            set => SetProperty(ref _isInstalling, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int ProgressPercent
        {
            get => _progressPercent;
            set => SetProperty(ref _progressPercent, value);
        }

        public SetupStep? CurrentStep
        {
            get => _currentStep;
            set => SetProperty(ref _currentStep, value);
        }

        public bool CanProceed
        {
            get => _canProceed;
            set => SetProperty(ref _canProceed, value);
        }

        public string ValidationError
        {
            get => _validationError;
            set => SetProperty(ref _validationError, value);
        }

        public bool ShowError
        {
            get => _showError;
            set => SetProperty(ref _showError, value);
        }

        public ICommand BrowseCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? SetupCompleted;
        public event Action? SetupCancelled;

        private void BrowseForGameFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select your Astro Bot PS5 game dump folder (containing eboot.bin)",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                GameDumpPath = dialog.SelectedPath;
                ValidateCommand.Execute(null);
            }
        }

        private async Task ValidateGameDumpAsync()
        {
            if (string.IsNullOrWhiteSpace(GameDumpPath))
                return;

            IsValidating = true;
            ShowError = false;
            ValidationError = "";
            StatusMessage = "Validating game dump...";
            ProgressPercent = 10;
            CanProceed = false;

            try
            {
                var info = await _gameValidator.ValidateGameDumpAsync(GameDumpPath);
                GameInfo = info;

                if (info.IsValid)
                {
                    StatusMessage = "Game dump validated successfully!";
                    ProgressPercent = 100;
                    CanProceed = true;
                }
                else
                {
                    StatusMessage = "Validation failed";
                    ValidationError = info.ValidationError ?? "Unknown error";
                    ShowError = true;
                    ProgressPercent = 0;
                }
            }
            catch (Exception ex)
            {
                ValidationError = $"Validation error: {ex.Message}";
                ShowError = true;
                StatusMessage = "Validation error";
                ProgressPercent = 0;
            }
            finally
            {
                IsValidating = false;
            }
        }

        private async Task InstallAsync()
        {
            if (GameInfo?.IsValid != true)
                return;

            IsInstalling = true;
            ShowError = false;
            StatusMessage = "Preparing installation...";
            ProgressPercent = 0;
            CanProceed = false;

            try
            {
                bool success;
                var setup = new GameSetup(_rootDirectory);
                if (setup.IsReleaseLayout)
                {
                    var progress = new Progress<(int Percent, string Message)>(p => { ProgressPercent = p.Percent; StatusMessage = p.Message; });
                    await setup.RunAsync(GameDumpPath, progress);
                    success = true;
                }
                else
                {
                    success = await _buildManager.BuildRunHybridAsync(GameDumpPath);
                }

                if (success)
                {
                    // Save settings
                    var settings = _settingsManager.Load();
                    settings.GameDumpPath = GameDumpPath;
                    settings.GameVersion = GameInfo.Version;
                    settings.GameTitleId = GameInfo.TitleId;
                    settings.FirstRunComplete = true;
                    settings.ApplyBestOnStart = true;
                    _settingsManager.Save(settings);

                    StatusMessage = "Installation complete!";
                    ProgressPercent = 100;

                    // Notify completion
                    await Task.Delay(1000);
                    SetupCompleted?.Invoke();
                }
                else
                {
                    StatusMessage = "Installation failed";
                }
            }
            catch (Exception ex)
            {
                ValidationError = $"Installation error: {ex.Message}";
                ShowError = true;
                StatusMessage = "Installation error";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private void Cancel()
        {
            SetupCancelled?.Invoke();
        }
    }
}