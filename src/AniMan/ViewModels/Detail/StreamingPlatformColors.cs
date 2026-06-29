using System.Windows.Media;

namespace AniMan.ViewModels.Detail;

internal static class StreamingPlatformColors
{
    private static readonly Dictionary<string, Color> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Crunchyroll",        (Color)ColorConverter.ConvertFromString("#F47521") },
        { "Netflix",            (Color)ColorConverter.ConvertFromString("#E50914") },
        { "HIDIVE",             (Color)ColorConverter.ConvertFromString("#00AEEF") },
        { "Funimation",         (Color)ColorConverter.ConvertFromString("#410099") },
        { "Amazon Prime Video", (Color)ColorConverter.ConvertFromString("#00A8E0") },
        { "Hulu",               (Color)ColorConverter.ConvertFromString("#3DBB3D") },
        { "Disney+",            (Color)ColorConverter.ConvertFromString("#1E3A8A") },
    };

    private static readonly Color Fallback = (Color)ColorConverter.ConvertFromString("#555759");

    public static SolidColorBrush GetBrush(string platformName)
    {
        var color = Map.TryGetValue(platformName, out var c) ? c : Fallback;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
