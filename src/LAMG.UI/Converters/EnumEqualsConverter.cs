using System.Globalization;
using System.Windows.Data;

namespace LAMG.UI.Converters;

/// <summary>
/// Two-way converter used by the sidebar radio buttons.
///   <c>Convert</c>  : returns <c>true</c> when bound value equals the parameter.
///   <c>ConvertBack</c>: when the radio button becomes checked, returns the
///                     parameter so the bound enum updates.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}
