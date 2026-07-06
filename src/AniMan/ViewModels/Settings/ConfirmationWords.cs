namespace AniMan.ViewModels.Settings;

internal static class ConfirmationWords
{
    // Longer (9-13 letter) farewell/ending-themed romaji words — long enough that
    // typing one out forces the user to notice what they're confirming.
    // sayounara = goodbye, wasuremono = a forgotten thing, otsukaresama = end-of-effort
    // greeting, ketsubetsu = final parting, shoumetsu = vanishing, zetsumetsu = extinction,
    // saishuukai = final episode, kanketsuhen = concluding volume, soushitsu = total loss,
    // monowasure = forgetfulness.
    private static readonly string[] Words =
    [
        "sayounara", "wasuremono", "otsukaresama", "ketsubetsu", "shoumetsu",
        "zetsumetsu", "saishuukai", "kanketsuhen", "soushitsu", "monowasure"
    ];

    public static string GetRandom() => Words[Random.Shared.Next(Words.Length)];
}
