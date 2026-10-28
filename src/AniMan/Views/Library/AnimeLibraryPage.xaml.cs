using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Library;
using Microsoft.Extensions.Logging;
using Wpf.Ui;

namespace AniMan.Views.Library;

public partial class AnimeLibraryPage : UserControl
{
    private readonly AnimeLibraryViewModel _viewModel;
    private readonly NavigationBag _bag;
    private readonly INavigationService _navigationService;
    private readonly ILogger<AnimeLibraryPage> _logger;

    public AnimeLibraryPage(
        AnimeLibraryViewModel viewModel,
        NavigationBag bag,
        INavigationService navigationService,
        ILogger<AnimeLibraryPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _bag = bag;
        _navigationService = navigationService;
        _logger = logger;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
            if (LibraryNavigationState.PendingAnimeStatusId.HasValue)
            {
                _viewModel.ActiveStatusId = LibraryNavigationState.PendingAnimeStatusId.Value;
                LibraryNavigationState.PendingAnimeStatusId = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anime library page load failed");
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryCardViewModel card) return;
        var backLabel = "← " + LocalizationManager.Get("Nav_Anime");
        _bag.Put(new DetailNavigationArgs(card.MalId, card.MediaType, card.LibraryItemId, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }
}
