using AniMan.Core.Common;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniMan.Localization;

/// <summary>
/// The app's one way to report a failed single action (add to library, restore, empty trash):
/// a short snackbar with a localized title and the localized reason for <see cref="ErrorKind"/>.
/// Failures that leave a whole page empty use <c>ErrorStateControl</c> instead.
/// </summary>
public static class FailureNotice
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);

    public static void ShowFailure(this ISnackbarService snackbar, string titleKey, ErrorKind kind) =>
        snackbar.Show(
            LocalizationManager.Get(titleKey),
            ErrorText.For(kind),
            ControlAppearance.Danger,
            new SymbolIcon(SymbolRegular.ErrorCircle24),
            Duration);
}
