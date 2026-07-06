using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Discover;
using Wpf.Ui;
using Wpf.Ui.Controls;
using SWC = System.Windows.Controls;

namespace AniMan.Views.Discover;

public partial class DiscoverPage : SWC.UserControl
{
    private readonly DiscoverViewModel _vm;
    private readonly IContentDialogService _dialogService;
    private readonly NavigationBag _bag;
    private readonly INavigationService _navigationService;

    public DiscoverPage(DiscoverViewModel viewModel, IContentDialogService dialogService,
        NavigationBag bag, INavigationService navigationService)
    {
        InitializeComponent();
        _vm = viewModel;
        _dialogService = dialogService;
        _bag = bag;
        _navigationService = navigationService;
        DataContext = viewModel;
        // The VM is a singleton but this page is transient: subscribe/unsubscribe
        // per Loaded/Unloaded so dead page instances don't accumulate handlers.
        Unloaded += (_, _) =>
        {
            _vm.AddToLibraryRequested -= OnAddToLibraryRequested;
            _vm.IsPageActive = false;
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.AddToLibraryRequested += OnAddToLibraryRequested;
        _vm.IsPageActive = true;

        // A nav re-click may have kicked off ResetToInitialAsync already;
        // don't start a second load on top of it.
        if (_vm.IsLoading) return;

        if (_vm.Cards.Count > 0)
        {
            // Returning to preserved state (e.g. back from a detail page):
            // keep tab/query/results, just re-sync ✓ markers and scroll position.
            await _vm.RefreshLibraryFlagsAsync();
            CardsScrollViewer.ScrollToVerticalOffset(_vm.SavedScrollOffset);
        }
        else
        {
            await _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm.SearchCommand.Execute(null);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.ExtentHeight > 0 && sv.VerticalOffset >= sv.ExtentHeight * 0.85)
            _vm.LoadMoreCommand.Execute(null);
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DiscoverCardViewModel card) return;
        _vm.SavedScrollOffset = CardsScrollViewer.VerticalOffset;
        var backLabel = "← " + LocalizationManager.Get("Discover_Title");
        _bag.Put(new DetailNavigationArgs(card.MalId, card.MediaType, null, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }

    private async void OnAddToLibraryRequested(object? sender, AddToLibraryEventArgs e)
    {
        var card = e.Card;
        var statusId = await Dialogs.AddToLibraryDialog.ShowAsync(
            _dialogService, card.Title, card.MediaType == MediaType.Manga);
        if (statusId is null) return;

        await _vm.AddToLibraryAsync(card.MalId, card.MediaType, statusId.Value);
    }
}
