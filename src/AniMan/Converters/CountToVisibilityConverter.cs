using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AniMan.Converters;

[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasItems = value is int count && count > 0;
        bool invert = parameter is string s &&
                      s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool visible = invert ? !hasItems : hasItems;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
