using System.Globalization;

namespace AniMan.Core.Common;

/// <summary>
/// Culture-independent parser for user 0.0–10.0 ratings, accepting both '.' and ','
/// as decimal separators so Turkish ("8,5") and English ("8.5") inputs behave identically.
/// </summary>
public static class RatingParser
{
    public static bool TryParse(string? text, out decimal? rating)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            rating = null;
            return true;
        }

        var normalized = text.Trim().Replace(',', '.');
        if (!decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            rating = null;
            return false;
        }

        rating = Math.Round(Math.Clamp(parsed, 0m, 10m), 1, MidpointRounding.AwayFromZero);
        return true;
    }
}

