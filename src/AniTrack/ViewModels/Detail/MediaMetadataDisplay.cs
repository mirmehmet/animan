using System.Globalization;
using AniTrack.Localization;

namespace AniTrack.ViewModels.Detail;

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
        ["Not yet published"] = "MediaStatus_NotYetPublished",
    };

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

    private static string? FormatMonthYear(string? raw) =>
        !string.IsNullOrEmpty(raw) &&
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString("MMM yyyy", LocalizationManager.Culture)
            : null;
}
