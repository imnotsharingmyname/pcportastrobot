using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AstroHybrid.App.Views
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
                return visibility == Visibility.Visible;
            return false;
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
                return visibility != Visibility.Visible;
            return true;
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
            if (value is int intBytes)
            {
                string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
                int i = 0;
                double size = intBytes;
                while (size >= 1024 && i < suffixes.Length - 1)
                {
                    size /= 1024;
                    i++;
                }
                return $"{size:F1} {suffixes[i]}";
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? "Installed" : "Not Installed";
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue
                    ? Application.Current.FindResource("SuccessBrush")
                    : Application.Current.FindResource("ErrorBrush");
            return Application.Current.FindResource("TextMutedBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class ControllerTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string type)
            {
                switch (type.ToLowerInvariant())
                {
                    case "dualsense":
                    case "playstation":
                        return "🎮";
                    case "xbox":
                        return "🎮";
                    case "switch":
                    case "switch pro":
                        return "🎮";
                    case "generic":
                    case "unknown":
                    default:
                        return "🎮";
                }
            }
            return "🎮";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}