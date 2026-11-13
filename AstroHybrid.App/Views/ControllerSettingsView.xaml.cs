using AstroHybrid.App.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Views
{
    public partial class ControllerSettingsView : UserControl
    {
        public ControllerSettingsView()
        {
            // Built before the controls, so every binding sees this page's view model at once.
            if (MainWindowViewModel.Current is MainWindowViewModel main)
            {
                DataContext = new ControllerSettingsViewModel(main);
            }
            InitializeComponent();
        }
    }

    public class ControllerTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string type)
            {
                return type switch
                {
                    "DualSense" => "🎮",
                    "DualShock 4" => "🎮",
                    "Xbox" => "🎮",
                    "Switch Pro" => "🎮",
                    _ => "🎮"
                };
            }
            return "🎮";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}