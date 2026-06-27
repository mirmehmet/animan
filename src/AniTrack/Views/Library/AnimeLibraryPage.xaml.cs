using AniTrack.Localization;
using AniTrack.Navigation;
using AniTrack.ViewModels.Library;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui;

namespace AniTrack.Views.Library;

public partial class AnimeLibraryPage : UserControl
{
    private readonly AnimeLibraryViewModel _viewModel;
    private readonly NavigationBag _bag;
    private readonly INavigationService _navigationService;

    public AnimeLibraryPage(AnimeLibraryViewModel viewModel, NavigationBag bag,
        INavigationService navigationService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _bag = bag;
        _navigationService = navigationService;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            await viewModel.LoadCommand.ExecuteAsync(null);
            if (LibraryNavigationState.PendingAnimeStatusId.HasValue)
            {
                viewModel.ActiveStatusId = LibraryNavigationState.PendingAnimeStatusId.Value;
                LibraryNavigationState.PendingAnimeStatusId = null;
            }
        };
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryCardViewModel card) return;
        var backLabel = "← " + LocalizationManager.Get("Nav_Anime");
        _bag.Put(new DetailNavigationArgs(card.MalId, card.MediaType, card.LibraryItemId, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }
}
