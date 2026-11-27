using System.Globalization;
using System.Text;

namespace AniMan.Core.Library;

/// <summary>
/// How library titles are searched and sorted. Titles are mostly romaji or English, typed
/// on Turkish or English keyboards, so the four Turkish i letters (I, ı, İ, i) count as one
/// letter in both, whatever the UI language.
/// </summary>
public static class TitleText
{
    /// <summary>
    /// Folded form for searching: accents removed ("Pokémon", "Kyōjin", "Çılgın" → "pokemon",
    /// "kyojin", "cilgin"), every i variant → "i", lower case. Same result in every culture.
    /// </summary>
    public static string SearchKey(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // FormD splits "é" into "e" + combining accent and "İ" into "I" + combining dot.
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch is 'I' or 'ı' ? 'i' : char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>True when <paramref name="query"/> occurs in the title behind <paramref name="titleKey"/>.</summary>
    /// <param name="titleKey">The title already folded with <see cref="SearchKey"/>.</param>
    public static bool Matches(string titleKey, string? query)
    {
        var needle = SearchKey(query?.Trim());
        return needle.Length == 0 || titleKey.Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sort key for <see cref="SortComparer"/>: the i variants become "i" so "ichigo",
    /// "Initial D" and "İnuyasha" sit together even under Turkish rules (where "I" is "ı").
    /// Accents are kept; the culture decides where they go.
    /// </summary>
    public static string SortKey(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is 'I' or 'ı' or 'İ' ? 'i' : source[i];
        });
    }

    /// <summary>A→Z by the UI language's rules (Turkish puts Ç after C), ignoring case. Compare <see cref="SortKey"/>s.</summary>
    public static StringComparer SortComparer(CultureInfo culture) =>
        StringComparer.Create(culture, CompareOptions.IgnoreCase);
}
