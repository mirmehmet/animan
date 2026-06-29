using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace AniMan.ViewModels.Stats;

public record StatusBarViewModel(string Name, int Count, double Fraction, string? Color)
{
    // Per-status bar color: use the DB hex if set, else a locale-safe name map.
    // Evaluated by the binding on the UI thread; frozen for safety.
    public SolidColorBrush Brush
    {
        get
        {
            MediaColor c;
            if (!string.IsNullOrWhiteSpace(Color))
            {
                try { c = (MediaColor)ColorConverter.ConvertFromString(Color); }
                catch { c = ForName(); }
            }
            else c = ForName();

            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    private MediaColor ForName() => Name switch
    {
        "Completed"                       => MediaColor.FromRgb(0x3F, 0xA4, 0x68), // green
        "On-hold"                         => MediaColor.FromRgb(0xC9, 0xA2, 0x3E), // amber
        "Dropped"                         => MediaColor.FromRgb(0xD0, 0x58, 0x4F), // red
        "Plan to watch" or "Plan to read" => MediaColor.FromRgb(0x6B, 0x71, 0x80), // gray
        _                                 => MediaColor.FromRgb(0x8B, 0x5C, 0xF6), // Watching/Reading → violet
    };
}
