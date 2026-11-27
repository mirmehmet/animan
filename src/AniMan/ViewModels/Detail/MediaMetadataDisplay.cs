using System.Globalization;
using AniMan.Core.Common;
using AniMan.Localization;

namespace AniMan.ViewModels.Detail;

internal static class MediaMetadataDisplay
{
    private static readonly Dictionary<string, string> SeasonKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winter"] = "Season_Winter", ["spring"] = "Season_Spring",
        ["summer"] = "Season_Summer", ["fall"] = "Season_Fall",
    };

    private static readonly Dictionary<string, string> StatusKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Finished Airing"]   = "MediaStatus_FinishedAiring",
        ["Currently Airing"]  = "MediaStatus_CurrentlyAiring",
        ["Not yet aired"]     = "MediaStatus_NotYetAired",
        ["Publishing"]        = "MediaStatus_Publishing",
        ["Finished"]          = "MediaStatus_Finished",
        ["On Hiatus"]         = "MediaStatus_OnHiatus",
        ["Discontinued"]      = "MediaStatus_Discontinued",
        ["Cancelled"]         = "MediaStatus_Cancelled",
        ["Not yet published"] = "MediaStatus_NotYetPublished",
    };

    /// <summary>
    /// The English title, or empty when there is none or it only repeats the main title
    /// ("Berserk" / "Berserk") — empty hides the line on the detail page.
    /// </summary>
    public static string EnglishTitle(string? title, string? english) =>
        string.IsNullOrWhiteSpace(english) ||
        string.Equals(english.Trim(), title?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : english.Trim();

    /// <summary>"24 min / ep" for series, "1 hr 45 min" for single-episode titles; empty when unknown.</summary>
    public static string Duration(int? minutesPerEpisode, int? totalEpisodes)
    {
        if (minutesPerEpisode is not { } minutes || minutes <= 0) return string.Empty;

        var length = minutes >= 60
            ? string.Format(LocalizationManager.Get("Detail_DurationHoursMinutes"), minutes / 60, minutes % 60)
            : string.Format(LocalizationManager.Get("Detail_DurationMinutes"), minutes);

        return totalEpisodes == 1
            ? length
            : string.Format(LocalizationManager.Get("Detail_DurationPerEpisode"), length);
    }

    public static string Season(string? season, int? year)
    {
        if (string.IsNullOrEmpty(season)) return "";
        var name = SeasonKeys.TryGetValue(season, out var key) ? LocalizationManager.Get(key) : season;
        return year is int y ? $"{name} {y}" : name;
    }

    public static string Status(string? raw) =>
        string.IsNullOrEmpty(raw) ? "" :
        StatusKeys.TryGetValue(raw, out var key) ? LocalizationManager.Get(key) : raw;

    public static string AiringRange(string? from, string? to)
    {
        var f = FormatMonthYear(from);
        var t = FormatMonthYear(to);
        if (f is null && t is null) return "";
        if (f is not null && t is not null) return $"{f} – {t}";
        return f ?? t!;
    }

    /// <summary>"Apr 1989", or "1989" when AniList knew only the year (AM-036).</summary>
    private static string? FormatMonthYear(string? raw) =>
        PartialDate.MonthYear(raw, LocalizationManager.Culture);
}
