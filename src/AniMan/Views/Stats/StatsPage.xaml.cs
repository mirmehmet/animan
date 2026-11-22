using System.Windows.Controls;
using AniMan.Core.Domain.Enums;
using AniMan.Navigation;
using AniMan.ViewModels.Stats;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace AniMan.Views.Stats;

public partial class StatsPage : UserControl
{
    private readonly INavigationService _navigationService;
    private readonly ILogger<StatsPage> _logger;

    public StatsPage(StatsViewModel viewModel, INavigationService navigationService, ILogger<StatsPage> logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _navigationService = navigationService;
        _logger = logger;

        // Singleton page and VM: subscribe once for the app's lifetime.
        viewModel.OpenLibraryRequested += OnOpenLibraryRequested;
    }

    // async void event handler: never let an exception escape to the dispatcher.
    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            await ((StatsViewModel)DataContext).LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stats page load failed");
        }
    }

    /// <summary>A chart element was clicked: open that library page with the matching filter.</summary>
    private void OnOpenLibraryRequested(object? sender, LibraryFilterRequest request)
    {
        LibraryNavigationState.PendingFilter = request;
        _navigationService.Navigate(request.MediaType == MediaType.Anime
            ? typeof(Library.AnimeLibraryPage)
            : typeof(Library.MangaLibraryPage));
    }
}
