using System.Globalization;
using System.Resources;
using System.Threading;

namespace AniTrack.Localization;

/// <summary>
/// Resolves localized strings from <c>Resources/Strings*.resx</c> for the active
/// UI culture. Language changes take effect on restart (the culture is fixed once
/// at startup), so <see cref="LocExtension"/> can resolve values at XAML load time.
/// </summary>
public static class LocalizationManager
{
    private static readonly ResourceManager ResourceManager =
        new("AniTrack.Resources.Strings", typeof(LocalizationManager).Assembly);

    /// <summary>Languages offered in Settings. Display names are endonyms (not translated).</summary>
    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("en", "English"),
        new("tr", "Türkçe")
    ];

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Applies <paramref name="languageCode"/> (e.g. "en", "tr") to the current and default threads.</summary>
    public static void SetCulture(string languageCode)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(languageCode);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.GetCultureInfo("en");
        }

        Culture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    /// <summary>Returns the localized string for <paramref name="key"/>, falling back to the key itself.</summary>
    public static string Get(string key) =>
        ResourceManager.GetString(key, Culture) ?? key;
}

public sealed record LanguageOption(string Code, string Display);
