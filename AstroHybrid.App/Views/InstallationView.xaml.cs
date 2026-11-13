using AstroHybrid.App.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AstroHybrid.App.Views
{
    public partial class InstallationView : UserControl
    {
        public InstallationView()
        {
            // Built before the controls, so every binding sees this page's view model at once.
            if (MainWindowViewModel.Current is MainWindowViewModel main)
            {
                DataContext = new InstallationViewModel(main);
            }
            InitializeComponent();
        }
    }

    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? "Installed and Ready" : "Not Installed";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return new SolidColorBrush(b ? (Color)ColorConverter.ConvertFromString("#00D4AA") : (Color)ColorConverter.ConvertFromString("#FF4444"));
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFAA00"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}