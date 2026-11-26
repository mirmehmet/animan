using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using Wpf.Ui;

namespace AniMan.Navigation;

/// <summary>Opens the detail page for a title; the one place that builds its navigation arguments.</summary>
public sealed class DetailNavigator(NavigationBag bag, INavigationService navigation)
{
    /// <param name="malId">MAL id; may be 0 when <paramref name="libraryItemId"/> identifies the title.</param>
    /// <param name="libraryItemId">The library entry, or <c>null</c> to look the title up (and preview it if it is not in the library).</param>
    /// <param name="backPageKey">Resource key of the page name shown on the detail page's back button.</param>
    public void Open(int malId, MediaType mediaType, int? libraryItemId, string backPageKey)
    {
        bag.Put(new DetailNavigationArgs(malId, mediaType, libraryItemId, LocalizationManager.Get(backPageKey)));
        navigation.Navigate(typeof(Views.Detail.DetailPage));
    }
}
