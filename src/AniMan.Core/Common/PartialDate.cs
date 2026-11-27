using System.Globalization;

namespace AniMan.Core.Common;

/// <summary>
/// Airing/publishing dates are stored as text in the shape the source gave: a full ISO date
/// from MyAnimeList, or only the known part from AniList — "1989" (year) or "1989-04" (year
/// and month) (AM-036). Before that, a year-only date became "1 January" and showed as "Jan 1989".
/// </summary>
public static class PartialDate
{
    /// <summary>The stored form of a date of which only some parts are known; null without a valid year.</summary>
    public static string? ToStored(int? year, int? month, int? day)
    {
        if (year is not (>= 1 and <= 9999) || month is not (null or (>= 1 and <= 12)))
            return null;

        if (month is null)
            return year.Value.ToString("D4", CultureInfo.InvariantCulture);

        if (day is null)
            return string.Create(CultureInfo.InvariantCulture, $"{year.Value:D4}-{month.Value:D2}");

        if (day < 1 || day > DateTime.DaysInMonth(year.Value, month.Value))
            return null;

        return new DateTimeOffset(year.Value, month.Value, day.Value, 0, 0, 0, TimeSpan.Zero)
            .ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// "Apr 1989" in <paramref name="culture"/>, or just "1989" when only the year is known;
    /// null for blank or unreadable text. A full date is read as written, not shifted to local time.
    /// </summary>
    public static string? MonthYear(string? stored, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        var text = stored.Trim();

        if (text.Length == 4 && TryNumber(text, out var yearOnly) && yearOnly >= 1)
            return yearOnly.ToString(CultureInfo.InvariantCulture);

        if (text.Length == 7 && text[4] == '-' &&
            TryNumber(text[..4], out var year) && year >= 1 &&
            TryNumber(text[5..], out var month) && month is >= 1 and <= 12)
        {
            return new DateTime(year, month, 1).ToString("MMM yyyy", culture);
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full)
            ? full.DateTime.ToString("MMM yyyy", culture)
            : null;
    }

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
