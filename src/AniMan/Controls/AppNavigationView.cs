using System.Windows;
using Wpf.Ui.Controls;

namespace AniMan.Controls;

/// <summary>Marks a page that is created fresh for every visit (DI transient) and must not outlive it.</summary>
public interface ITransientPage;

/// <summary>
/// WPF-UI's NavigationView with one fix: it no longer keeps every visited transient page alive.
/// <para>
/// NavigationView records each navigated page <i>instance</i> in its protected
/// <c>PageToNavigationItemDictionary</c> and never removes it. That is harmless for the
/// singleton tab pages (one entry each) but leaks every DetailPage ever opened — measured:
/// three detail visits left three DetailPage/DetailViewModel graphs on the heap. Its content
/// presenter is also a WPF Frame whose back stack holds old page objects; AniMan navigates
/// with its own back buttons, so that stack is never used and is cleared too.
/// </para>
/// </summary>
public class AppNavigationView : NavigationView
{
    protected override void OnNavigated(object page)
    {
        base.OnNavigated(page);

        var stale = PageToNavigationItemDictionary.Keys
            .Where(p => p is ITransientPage && !ReferenceEquals(p, page))
            .ToList();
        foreach (var oldPage in stale)
            PageToNavigationItemDictionary.Remove(oldPage);

        if (NavigationViewContentPresenter is { } presenter)
        {
            while (presenter.CanGoBack)
                presenter.RemoveBackEntry();
        }
    }
}
