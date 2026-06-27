using AniTrack.Navigation;
using AniTrack.ViewModels.Dashboard;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui;

namespace AniTrack.Views.Dashboard;

public partial class DashboardPage : UserControl
{
    private readonly DashboardViewModel _viewModel;
    private readonly NavigationBag _bag;
    private readonly INavigationService _navigationService;

    public DashboardPage(DashboardViewModel viewModel, NavigationBag bag,
        INavigationService navigationService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _bag = bag;
        _navigationService = navigationService;
        DataContext = viewModel;

        viewModel.NavigateToLibraryItemRequested += OnNavigateToLibraryItemRequested;
        viewModel.NavigateToDiscoverRequested += OnNavigateToDiscoverRequested;

        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnNavigateToLibraryItemRequested(object? sender, int libraryItemId)
    {
        var backLabel = "← Dashboard";
        _bag.Put(new DetailNavigationArgs(0, Core.Domain.Enums.MediaType.Anime, libraryItemId, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }

    private void OnNavigateToDiscoverRequested(object? sender, EventArgs e) =>
        _navigationService.Navigate(typeof(Discover.DiscoverPage));

    private void OnContinueItemClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryItemSummary item) return;
        _viewModel.OpenLibraryItemCommand.Execute(item.Id);
    }

    private void OnHeroClicked(object sender, MouseButtonEventArgs e)
    {
        var first = _viewModel.ContinueItems.FirstOrDefault();
        if (first is not null)
            _viewModel.OpenLibraryItemCommand.Execute(first.Id);
    }
}
