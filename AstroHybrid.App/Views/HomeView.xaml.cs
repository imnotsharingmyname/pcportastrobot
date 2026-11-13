using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            // Built before the controls, so every binding sees this page's view model at once.
            if (MainWindowViewModel.Current is MainWindowViewModel main)
            {
                DataContext = new HomeViewModel(main);
            }
            InitializeComponent();
        }
    }

    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                if (status.Contains("Ready") || status.Contains("Installed"))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA"));
                if (status.Contains("Not"))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFAA00"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}