using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AstroHybrid.App
{
    // Boolean to Visibility converter
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    // Inverse Boolean to Visibility converter
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return true;
        }
    }

    // Bytes to string converter (formats bytes as KB, MB, GB)
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
                return Convert((long)intBytes, targetType, parameter, culture);
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // Bool to status string converter
    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "Ready" : "Not Ready";
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // Bool to status brush converter
    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue
                    ? new SolidColorBrush(Color.FromRgb(0, 212, 170))  // Success color
                    : new SolidColorBrush(Color.FromRgb(255, 68, 68));  // Error color
            }
            return new SolidColorBrush(Color.FromRgb(106, 116, 144));  // Muted color
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // Controller type to icon converter
    public class ControllerTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string controllerType)
            {
                switch (controllerType.ToLowerInvariant())
                {
                    case "dualsense":
                    case "dualshock 4":
                        return ""; // PlayStation icon
                    case "xbox":
                        return ""; // Xbox icon
                    case "switch pro":
                        return ""; // Nintendo Switch icon
                    default:
                        return ""; // Generic gamepad icon
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}