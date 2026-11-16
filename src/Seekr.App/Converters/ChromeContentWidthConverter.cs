using System.Globalization;
using System.Windows.Data;
namespace Seekr.App.Converters;
public sealed class ChromeContentWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && double.IsFinite(width) ? Math.Max(240, width - 230) : 600d;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
