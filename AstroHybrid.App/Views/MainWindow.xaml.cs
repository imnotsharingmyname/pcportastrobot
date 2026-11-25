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
            ClampToWorkArea();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
                Title = $"AstroHybrid v{version.Major}.{version.Minor}.{version.Build}";
        }

        private void ClampToWorkArea()
        {
            var work = SystemParameters.WorkArea;
            MaxWidth = work.Width;
            MaxHeight = work.Height;
            if (Width > work.Width * 0.94) Width = Math.Max(MinWidth, work.Width * 0.92);
            if (Height > work.Height * 0.94) Height = Math.Max(MinHeight, work.Height * 0.92);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (RootWindowBorder == null) return;
            RootWindowBorder.Padding = WindowState == WindowState.Maximized
                ? new Thickness(6)
                : new Thickness(0);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
        // One view instance per page, reused on every navigation. Creating a new view per
        // navigation also created a new view model each time, and every view model subscribes
        // to MainWindowViewModel.PropertyChanged without unsubscribing, so the subscriber list
        // (and the work done on every settings change) grew without bound as the user navigated.
        private readonly System.Collections.Generic.Dictionary<LauncherPage, UserControl> _pages =
            new System.Collections.Generic.Dictionary<LauncherPage, UserControl>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LauncherPage page)
            {
                if (!_pages.TryGetValue(page, out var view))
                {
                    view = page switch
                    {
                        LauncherPage.Home => (UserControl)new HomeView(),
                        LauncherPage.GraphicsSettings => new GraphicsSettingsView(),
                        LauncherPage.ControllerSettings => new ControllerSettingsView(),
                        LauncherPage.AdvancedSettings => new AdvancedSettingsView(),
                        LauncherPage.Installation => new InstallationView(),
                        _ => new HomeView()
                    };
                    _pages[page] = view;
                }
                return view;
            }
            if (!_pages.TryGetValue(LauncherPage.Home, out var fallback))
            {
                fallback = new HomeView();
                _pages[LauncherPage.Home] = fallback;
            }
            return fallback;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
