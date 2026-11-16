using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Seekr.App.Utils;
using Seekr.App.ViewModels;

namespace Seekr.App.Converters;

[ValueConversion(typeof(ResultItem), typeof(ImageSource))]
public sealed class FileIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ResultItem item)
        {
            return null;
        }

        return item.IsDirectory && System.IO.Path.GetPathRoot(item.FullPath)?.TrimEnd('\\') == item.FullPath.TrimEnd('\\')
            ? FilePresentation.GetPathIcon(item.FullPath) : FilePresentation.GetIcon(item.IsDirectory, item.Extension);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
