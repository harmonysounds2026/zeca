using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LAMG.UI.Converters;

/// <summary>
/// Maps <see cref="bool"/> to <see cref="Visibility"/>. WPF provides
/// a built-in version, but this one is consistent with our other
/// converters and lets us tweak behaviour if needed.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}
