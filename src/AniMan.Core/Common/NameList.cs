using System.Text.Json;

namespace AniMan.Core.Common;

/// <summary>
/// Genres, themes, demographics and authors are stored as a JSON array of names in one text
/// column. This is the one place that reads and writes that shape.
/// </summary>
public static class NameList
{
    /// <summary>The names in <paramref name="json"/>; an empty list when it is blank or not a JSON array of strings.</summary>
    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The stored form of <paramref name="names"/>.</summary>
    public static string ToJson(IEnumerable<string> names) => JsonSerializer.Serialize(names);

    /// <summary>
    /// For untrusted input (a backup file): returns <paramref name="json"/> if it is a JSON array
    /// of names, the same array without null or blank entries if it has some, or <c>null</c> if it
    /// is not a JSON array of strings at all — so a damaged file cannot plant text that later
    /// fails to parse.
    /// </summary>
    public static string? Clean(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var names = JsonSerializer.Deserialize<List<string?>>(json);
            if (names is null) return null;
            if (names.All(n => !string.IsNullOrWhiteSpace(n))) return json; // already clean: keep as written

            return ToJson(names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
