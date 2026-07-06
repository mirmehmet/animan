using System.Windows;
using AniMan.Core.Domain;
using AniMan.Localization;
using Wpf.Ui;
using Wpf.Ui.Controls;
using SWC = System.Windows.Controls;

namespace AniMan.Views.Dialogs;

/// <summary>
/// The shared "add to library" status-selection dialog used by the Discover and
/// Detail (preview) pages.
/// </summary>
internal static class AddToLibraryDialog
{
    /// <summary>Shows the dialog; returns the chosen status id, or null when cancelled.</summary>
    public static async Task<int?> ShowAsync(
        IContentDialogService dialogService, string mediaTitle, bool isManga)
    {
        var statusItems = isManga
            ? new[]
            {
                (Id: TrackingStatusIds.PlanToRead, Name: LocalizationManager.Get("Status_PlanToRead")),
                (Id: TrackingStatusIds.Reading, Name: LocalizationManager.Get("Status_Reading")),
                (Id: TrackingStatusIds.Completed, Name: LocalizationManager.Get("Status_Completed")),
                (Id: TrackingStatusIds.OnHold, Name: LocalizationManager.Get("Status_OnHold")),
                (Id: TrackingStatusIds.Dropped, Name: LocalizationManager.Get("Status_Dropped"))
            }
            : new[]
            {
                (Id: TrackingStatusIds.PlanToWatch, Name: LocalizationManager.Get("Status_PlanToWatch")),
                (Id: TrackingStatusIds.Watching, Name: LocalizationManager.Get("Status_Watching")),
                (Id: TrackingStatusIds.Completed, Name: LocalizationManager.Get("Status_Completed")),
                (Id: TrackingStatusIds.OnHold, Name: LocalizationManager.Get("Status_OnHold")),
                (Id: TrackingStatusIds.Dropped, Name: LocalizationManager.Get("Status_Dropped"))
            };

        var comboBox = new SWC.ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 200
        };
        foreach (var (_, name) in statusItems)
            comboBox.Items.Add(name);
        comboBox.SelectedIndex = 0;

        var panel = new SWC.StackPanel();
        panel.Children.Add(new SWC.TextBlock
        {
            Text = mediaTitle,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new SWC.TextBlock
        {
            Text = LocalizationManager.Get("Discover_SelectStatus"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(comboBox);

        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Discover_AddToLibrary"),
            Content = panel,
            PrimaryButtonText = LocalizationManager.Get("Common_Add"),
            CloseButtonText = LocalizationManager.Get("Common_Cancel")
        };

        var result = await dialogService.ShowAsync(dialog, CancellationToken.None);
        return result == ContentDialogResult.Primary
            ? statusItems[comboBox.SelectedIndex].Id
            : null;
    }
}
