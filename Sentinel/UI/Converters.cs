using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sentinel.UI
{
    /// <summary>true → Collapsed, false → Visible.</summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool && (bool)value ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility && (Visibility)value != Visibility.Visible;
    }

    /// <summary>Non-null (and non-empty string) → Visible.</summary>
    public sealed class NotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visible = value != null && !(value is string && string.IsNullOrWhiteSpace((string)value));
            if (parameter as string == "invert") visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Inverts a bool.</summary>
    public sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool && (bool)value);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool && (bool)value);
    }
}
