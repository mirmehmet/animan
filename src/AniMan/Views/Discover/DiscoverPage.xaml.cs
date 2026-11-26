using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Discover;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using SWC = System.Windows.Controls;

namespace AniMan.Views.Discover;

public partial class DiscoverPage : SWC.UserControl
{
    private readonly DiscoverViewModel _vm;
    private readonly IContentDialogService _dialogService;
    private readonly ISnackbarService _snackbar;
    private readonly DetailNavigator _detail;
    private readonly ILogger<DiscoverPage> _logger;

    // Set while the view swaps tabs and restores that tab's offset, so the
    // intermediate ScrollChanged events don't overwrite the saved position.
    private bool _restoringScroll;

    public DiscoverPage(
        DiscoverViewModel viewModel,
        IContentDialogService dialogService,
        ISnackbarService snackbar,
        DetailNavigator detail,
        ILogger<DiscoverPage> logger)
    {
        InitializeComponent();
        _vm = viewModel;
        _dialogService = dialogService;
        _snackbar = snackbar;
        _detail = detail;
        _logger = logger;
        DataContext = viewModel;

        Unloaded += (_, _) =>
        {
            _vm.AddToLibraryRequested -= OnAddToLibraryRequested;
            _vm.ScrollToTopRequested -= OnScrollToTopRequested;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.IsPageActive = false;
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Guard against repeated Loaded events without an intervening Unloaded.
            _vm.AddToLibraryRequested -= OnAddToLibraryRequested;
            _vm.AddToLibraryRequested += OnAddToLibraryRequested;
            _vm.ScrollToTopRequested -= OnScrollToTopRequested;
            _vm.ScrollToTopRequested += OnScrollToTopRequested;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.IsPageActive = true;

            // A nav re-click may have kicked off ResetToInitialAsync already;
            // don't start a second load on top of it.
            if (_vm.Current.IsLoading) return;

            if (_vm.Current.Cards.Count > 0)
            {
                // Returning to preserved state (e.g. back from a detail page):
                // keep tab/query/results, just re-sync ✓ markers and scroll position.
                await _vm.RefreshLibraryFlagsAsync();
                RestoreScroll(_vm.Current.SavedScrollOffset);
            }
            else
            {
                await _vm.LoadCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discover page load failed");
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm.SearchCommand.Execute(null);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_restoringScroll)
            _vm.Current.SavedScrollOffset = e.VerticalOffset;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tab switched: show the new tab where the user left it.
        if (e.PropertyName == nameof(DiscoverViewModel.Current))
            RestoreScroll(_vm.Current.SavedScrollOffset);
    }

    private void OnScrollToTopRequested(object? sender, EventArgs e) => RestoreScroll(0);

    /// <summary>Scrolls once the new cards have been laid out (layout runs at a higher
    /// dispatcher priority than Loaded), ignoring the offset changes the swap itself causes.</summary>
    private void RestoreScroll(double offset)
    {
        _restoringScroll = true;
        Dispatcher.BeginInvoke(() =>
        {
            CardsScrollViewer.ScrollToVerticalOffset(offset);
            _restoringScroll = false;
        }, DispatcherPriority.Loaded);
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DiscoverCardViewModel card) return;
        _detail.Open(card.MalId, card.MediaType, null, "Discover_Title");
    }

    private async void OnAddToLibraryRequested(object? sender, AddToLibraryEventArgs e)
    {
        var card = e.Card;
        if (card.IsAdding) return; // a second click while the first add is still running

        try
        {
            var statusId = await Dialogs.AddToLibraryDialog.ShowAsync(
                _dialogService, card.Title, card.MediaType == MediaType.Manga);
            if (statusId is null) return;

            card.IsAdding = true;
            var result = await _vm.AddToLibraryAsync(card.MalId, card.MediaType, statusId.Value);
            if (!result.IsSuccess)
                _snackbar.ShowFailure("Notify_AddToLibraryFailed", result.Kind);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add to library dialog failed from Discover");
            _snackbar.ShowFailure("Notify_AddToLibraryFailed", Core.Common.ErrorKind.Unexpected);
        }
        finally
        {
            card.IsAdding = false;
        }
    }
}
