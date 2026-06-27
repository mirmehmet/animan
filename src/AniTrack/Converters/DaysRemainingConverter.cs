using System.Globalization;
using System.Windows.Data;

namespace AniTrack.Converters;

[ValueConversion(typeof(DateTime?), typeof(string))]
public class DaysRemainingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime deletedAt) return "?";
        var remaining = 30 - (int)(DateTime.UtcNow - deletedAt).TotalDays;
        return remaining > 0 ? remaining.ToString() : "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
