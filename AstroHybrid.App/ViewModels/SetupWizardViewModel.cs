using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
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
        private readonly Func<string, IProgress<ArchiveProgress>?, CancellationToken, Task<GameInfo>> _validateSelectedSource;
        private CancellationTokenSource? _validationCts;
        private int _validationGeneration;

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
            : this(rootDirectory, null)
        {
        }

        internal SetupWizardViewModel(string rootDirectory,
            Func<string, IProgress<ArchiveProgress>?, CancellationToken, Task<GameInfo>>? validateSelectedSource)
        {
            _rootDirectory = rootDirectory;
            _gameValidator = new GameValidator();
            _buildManager = new BuildManager(rootDirectory);
            _settingsManager = new SettingsManager(rootDirectory);
            _validateSelectedSource = validateSelectedSource ?? ((path, progress, token) => _gameValidator.ValidateGameDumpAsync(path, progress, token));

            _buildManager.ProgressChanged += msg => StatusMessage = msg;
            _buildManager.ProgressPercentChanged += pct => ProgressPercent = pct;
            _buildManager.ErrorOccurred += err =>
            {
                ValidationError = err;
                ShowError = true;
            };

            BrowseCommand = new RelayCommand(BrowseForGameFolder);
            BrowseArchiveCommand = new RelayCommand(BrowseForGameArchive);
            ValidateCommand = new RelayCommand(async () => await ValidateGameDumpAsync(), () => !IsValidating && !string.IsNullOrWhiteSpace(GameDumpPath));
            InstallCommand = new RelayCommand(async () => await InstallAsync(), () => !IsInstalling && GameInfo?.IsValid == true);
            // Setup cannot be stopped half-way (the game program would be left half-written); Cancel waits for it.
            CancelCommand = new RelayCommand(Cancel, () => !IsInstalling);
        }

        public string GameDumpPath
        {
            get => _gameDumpPath;
            set
            {
                // A pasted path may come with quotes; a changed path needs a new check.
                var path = (value ?? "").Trim().Trim('"').Trim();
                if (SetProperty(ref _gameDumpPath, path))
                {
                    CancelValidation();
                    IsValidating = false;
                    GameInfo = null;
                    CanProceed = false;
                    ShowError = false;
                }
            }
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
            set
            {
                if (SetProperty(ref _canProceed, value))
                    (InstallCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
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
        public ICommand BrowseArchiveCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? SetupCompleted;
        public event Action? SetupCancelled;

        private void BrowseForGameFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select your Astro Bot PS5 game dump folder (containing eboot.bin)",
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    GameDumpPath = dialog.SelectedPath;
                    ValidateCommand.Execute(null);
                }
            }
        }

        private void BrowseForGameArchive()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select your Astro Bot ZArchive",
                Filter = "ZArchive files (*.zar)|*.zar|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                GameDumpPath = dialog.FileName;
                ValidateCommand.Execute(null);
            }
        }

        private async Task ValidateGameDumpAsync()
        {
            if (string.IsNullOrWhiteSpace(GameDumpPath)) return;
            CancelValidation();
            var generation = _validationGeneration;
            var selectedPath = GameDumpPath;
            var cts = new CancellationTokenSource();
            _validationCts = cts;
            GameInfo = null;
            IsValidating = true;
            ShowError = false;
            ValidationError = "";
            StatusMessage = "Validating game source...";
            ProgressPercent = 0;
            CanProceed = false;
            var progress = new Progress<ArchiveProgress>(p =>
            {
                if (generation != _validationGeneration || selectedPath != GameDumpPath) return;
                ProgressPercent = p.Percent;
                StatusMessage = p.TotalBytes > 0
                    ? $"{p.Message} ({FormatBytes(p.ProcessedBytes)} / {FormatBytes(p.TotalBytes)})"
                    : p.Message;
            });

            try
            {
                var info = await _validateSelectedSource(selectedPath, progress, cts.Token);
                if (generation != _validationGeneration || selectedPath != GameDumpPath || cts.IsCancellationRequested) return;
                GameInfo = info;
                if (info.IsValid)
                {
                    StatusMessage = "Game source validated successfully!";
                    ProgressPercent = 100;
                    CanProceed = true;
                    if (!string.IsNullOrEmpty(info.Warning))
                        MessageBox.Show(info.Warning, "AstroHybrid - Game version", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    StatusMessage = "Validation failed";
                    ValidationError = info.ValidationError ?? "Unknown error";
                    ShowError = true;
                    ProgressPercent = 0;
                }
            }
            catch (OperationCanceledException)
            {
                if (generation == _validationGeneration)
                {
                    StatusMessage = "Validation cancelled";
                    ProgressPercent = 0;
                }
            }
            catch (Exception ex)
            {
                if (generation != _validationGeneration || selectedPath != GameDumpPath) return;
                ValidationError = $"Validation error: {ex.Message}";
                ShowError = true;
                StatusMessage = "Validation error";
                ProgressPercent = 0;
            }
            finally
            {
                if (generation == _validationGeneration)
                {
                    IsValidating = false;
                    if (ReferenceEquals(_validationCts, cts)) _validationCts = null;
                }
                cts.Dispose();
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.0} {units[unit]}";
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

        private void CancelValidation()
        {
            _validationGeneration++;
            _validationCts?.Cancel();
            _validationCts?.Dispose();
            _validationCts = null;
        }

        private void Cancel()
        {
            CancelValidation();
            IsValidating = false;
            GameInfo = null;
            CanProceed = false;
            ShowError = false;
            SetupCancelled?.Invoke();
        }
    }
}
