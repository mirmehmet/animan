using System.Globalization;
using System.Windows.Data;

namespace AniMan.Converters;

/// <summary>
/// Two-way converter: (int value == int.Parse(parameter)) ↔ bool.
/// Used to bind RadioButton.IsChecked to an integer ViewModel property,
/// e.g. ActiveStatusId, by passing the status id as ConverterParameter.
/// </summary>
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string s && int.TryParse(s, out int p))
            return value is int v && v == p;
        return value?.Equals(parameter) ?? false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && int.TryParse(s, out int p))
            return p;
        return Binding.DoNothing;
    }
}
