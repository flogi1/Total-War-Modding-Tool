using System.Globalization;
using System.Windows.Data;

namespace TotalWarModTool.App;

/// <summary>
/// Binds a radio button to an enum value: checked when the value equals the parameter. Checking writes the parameter; unchecking writes
/// nothing, because the radio of the new value writes it.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : Binding.DoNothing;
}
