using System.Globalization;
using System.Windows.Data;
using AniMan.Localization;

namespace AniMan.Converters;

[ValueConversion(typeof(DateTime?), typeof(string))]
public class DaysRemainingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime deletedAt) return "?";
        var remaining = Math.Max(0, 30 - (int)(DateTime.UtcNow - deletedAt).TotalDays);
        var fmt = LocalizationManager.Get("Settings_TrashDaysLeft");
        return string.Format(fmt, remaining);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
