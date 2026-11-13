using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }

    // Converters
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
                return Enum.Parse(typeof(LauncherPage), parameter.ToString()!);
            return Binding.DoNothing;
        }
    }

    public class BoolToProgressConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? 1 : 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PageVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LauncherPage page && parameter is string targetPage)
            {
                return page.ToString() == targetPage ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Template Selector for page content
    public class PageTemplateSelector : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LauncherPage page)
            {
                return page switch
                {
                    LauncherPage.Home => new HomeView(),
                    LauncherPage.GraphicsSettings => new GraphicsSettingsView(),
                    LauncherPage.ControllerSettings => new ControllerSettingsView(),
                    LauncherPage.AdvancedSettings => new AdvancedSettingsView(),
                    LauncherPage.Installation => new InstallationView(),
                    _ => new HomeView()
                };
            }
            return new HomeView();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}