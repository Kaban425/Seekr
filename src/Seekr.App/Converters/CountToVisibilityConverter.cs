using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Seekr.App.Converters;

[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var inverted = parameter is "Inverted";
        var visible = count > 0;
        return visible ^ inverted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
