using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Dashboard;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui;

namespace AniMan.Views.Dashboard;

public partial class DashboardPage : UserControl
{
    private readonly DashboardViewModel _viewModel;
    private readonly NavigationBag _bag;
    private readonly INavigationService _navigationService;
    private readonly ILogger<DashboardPage> _logger;

    public DashboardPage(
        DashboardViewModel viewModel,
        NavigationBag bag,
        INavigationService navigationService,
        ILogger<DashboardPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _bag = bag;
        _navigationService = navigationService;
        _logger = logger;
        DataContext = viewModel;

        viewModel.NavigateToLibraryItemRequested += OnNavigateToLibraryItemRequested;
        viewModel.NavigateToDiscoverRequested += OnNavigateToDiscoverRequested;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard page load failed");
        }
    }

    private void OnNavigateToLibraryItemRequested(object? sender, int libraryItemId)
    {
        var backLabel = LocalizationManager.Get("Nav_Dashboard");
        _bag.Put(new DetailNavigationArgs(0, MediaType.Anime, libraryItemId, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }

    private void OnNavigateToDiscoverRequested(object? sender, EventArgs e) =>
        _navigationService.Navigate(typeof(Discover.DiscoverPage));

    private void OnContinueItemClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryItemSummary item) return;
        _viewModel.OpenLibraryItemCommand.Execute(item.Id);
    }

    private void OnSeasonItemClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SeasonCardViewModel anime) return;
        var backLabel = LocalizationManager.Get("Nav_Dashboard");
        _bag.Put(new DetailNavigationArgs(anime.Id, MediaType.Anime, null, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }

    private void OnHeroClicked(object sender, MouseButtonEventArgs e)
    {
        var first = _viewModel.HeroItem;
        if (first is not null)
            _viewModel.OpenLibraryItemCommand.Execute(first.Id);
    }

    private void OnHorizontalScrollMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        OuterScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent
        });
    }
}
