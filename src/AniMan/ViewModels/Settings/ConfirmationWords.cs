namespace AniMan.ViewModels.Settings;

internal static class ConfirmationWords
{
    private static readonly string[] Words =
        ["nakama", "sayonara", "owari", "shounen", "sekai", "kioku"];

    public static string GetRandom() => Words[Random.Shared.Next(Words.Length)];
}
