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
        { "Prime Video",        (Color)ColorConverter.ConvertFromString("#00A8E0") },
        { "Hulu",               (Color)ColorConverter.ConvertFromString("#3DBB3D") },
        { "Disney+",            (Color)ColorConverter.ConvertFromString("#1E3A8A") },
        { "Disney Plus",        (Color)ColorConverter.ConvertFromString("#1E3A8A") },
        { "Bilibili Global",    (Color)ColorConverter.ConvertFromString("#00A1D6") },
        { "Bilibili TV",        (Color)ColorConverter.ConvertFromString("#00A1D6") },
        { "Bilibili",           (Color)ColorConverter.ConvertFromString("#00A1D6") },
        { "YouTube",            (Color)ColorConverter.ConvertFromString("#FF0000") },
        { "Shahid",             (Color)ColorConverter.ConvertFromString("#00B140") },
        { "Shahid VIP",         (Color)ColorConverter.ConvertFromString("#00B140") },
        { "Apple TV",           (Color)ColorConverter.ConvertFromString("#2D2D2D") },
        { "Apple TV Store",     (Color)ColorConverter.ConvertFromString("#2D2D2D") },
        { "Apple TV+",          (Color)ColorConverter.ConvertFromString("#2D2D2D") },
        { "HBO Max",            (Color)ColorConverter.ConvertFromString("#002BE7") },
        { "Max",                (Color)ColorConverter.ConvertFromString("#002BE7") },
        { "Bahamut Anime Crazy",(Color)ColorConverter.ConvertFromString("#00B4D8") },
        { "Catchplay",          (Color)ColorConverter.ConvertFromString("#FF4D00") },
        { "LINE TV",            (Color)ColorConverter.ConvertFromString("#00C300") },
        { "Google Play Movies", (Color)ColorConverter.ConvertFromString("#4285F4") },
        { "Google Play",        (Color)ColorConverter.ConvertFromString("#4285F4") },
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
