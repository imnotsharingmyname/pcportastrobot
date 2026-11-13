using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App.Views
{
    public partial class SetupWizard : Window
    {
        private SetupWizardViewModel? _viewModel;

        public SetupWizard(string rootDirectory)
        {
            InitializeComponent();
            _viewModel = new SetupWizardViewModel(rootDirectory);
            DataContext = _viewModel;
            _viewModel.SetupCompleted += OnSetupCompleted;
            _viewModel.SetupCancelled += OnSetupCancelled;

            // Update UI based on validation state
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SetupWizardViewModel.GameInfo))
                {
                    UpdatePanels();
                }
                else if (e.PropertyName == nameof(SetupWizardViewModel.IsInstalling) ||
                         e.PropertyName == nameof(SetupWizardViewModel.GameDumpPath) ||
                         e.PropertyName == nameof(SetupWizardViewModel.CanProceed))
                {
                    UpdatePanels();
                }
            };
            UpdatePanels();
        }

        private void UpdatePanels()
        {
            if (_viewModel == null) return;

            bool hasGameInfo = _viewModel.GameInfo?.IsValid == true;
            bool isInstalling = _viewModel.IsInstalling;

            WelcomePanel.Visibility = (!hasGameInfo && !isInstalling && string.IsNullOrEmpty(_viewModel.GameDumpPath)) ? Visibility.Visible : Visibility.Collapsed;
            GameSelectionPanel.Visibility = !isInstalling ? Visibility.Visible : Visibility.Collapsed;
            GameInfoBorder.Visibility = hasGameInfo ? Visibility.Visible : Visibility.Collapsed;
            InstallPanel.Visibility = isInstalling ? Visibility.Visible : Visibility.Collapsed;
            // After a successful install the wizard closes by itself; there is no separate "complete" page.
            CompletePanel.Visibility = Visibility.Collapsed;

            NextButton.Visibility = (!hasGameInfo && !isInstalling) ? Visibility.Visible : Visibility.Collapsed;
            InstallButton.Visibility = (hasGameInfo && !isInstalling) ? Visibility.Visible : Visibility.Collapsed;
            FinishButton.Visibility = Visibility.Collapsed;
        }

        private void OnSetupCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                DialogResult = true;
                Close();
            });
        }

        private void OnSetupCancelled()
        {
            Dispatcher.Invoke(() =>
            {
                DialogResult = false;
                Close();
            });
        }
    }

    // Converters
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && !b) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v != Visibility.Visible;
        }
    }

    public class BytesToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
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
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}