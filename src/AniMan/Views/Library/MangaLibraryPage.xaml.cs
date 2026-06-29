using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Library;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui;

namespace AniMan.Views.Library;

public partial class MangaLibraryPage : UserControl
{
    private readonly MangaLibraryViewModel _viewModel;
    private readonly NavigationBag _bag;
    private readonly INavigationService _navigationService;

    public MangaLibraryPage(MangaLibraryViewModel viewModel, NavigationBag bag,
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
            if (LibraryNavigationState.PendingMangaStatusId.HasValue)
            {
                viewModel.ActiveStatusId = LibraryNavigationState.PendingMangaStatusId.Value;
                LibraryNavigationState.PendingMangaStatusId = null;
            }
        };
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryCardViewModel card) return;
        var backLabel = "← " + LocalizationManager.Get("Nav_Manga");
        _bag.Put(new DetailNavigationArgs(card.MalId, card.MediaType, card.LibraryItemId, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }
}
