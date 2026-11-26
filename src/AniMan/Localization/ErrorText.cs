using AniMan.Core.Common;

namespace AniMan.Localization;

/// <summary>
/// The sentence the user sees for a failed operation. Services describe failures in English
/// for the log; the UI never shows that text and picks a localized one by <see cref="ErrorKind"/>.
/// Every kind has an <c>Error_{Kind}</c> entry in both resx files (guarded by a test).
/// </summary>
public static class ErrorText
{
    public static string For(ErrorKind kind) => LocalizationManager.Get("Error_" + kind);
}
