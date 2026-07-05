using AniMan.Core.Interfaces;
using AniMan.ViewModels;
using AniMan.ViewModels.Discover;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniMan;

public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private readonly DiscoverViewModel _discoverViewModel;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        IContentDialogService contentDialogService,
        ISettingsService settingsService,
        DiscoverViewModel discoverViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _navigationService = navigationService;
        _settingsService = settingsService;
        _discoverViewModel = discoverViewModel;
        navigationService.SetNavigationControl(RootNavigation);
        contentDialogService.SetDialogHost(RootContentDialogHost);
        Loaded += OnWindowLoaded;
    }

    private async void OnDiscoverNavClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        // Re-clicking Discover while already on it resets to the initial view
        // (Anime tab, top list). From any other page the click navigates normally
        // and the preserved state is kept.
        if (_discoverViewModel.IsPageActive)
            await _discoverViewModel.ResetToInitialAsync();
    }

    private async void OnWindowLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var startupPage = await _settingsService.GetStartupPageAsync();
        var pageType = startupPage switch
        {
            "Anime"    => typeof(Views.Library.AnimeLibraryPage),
            "Manga"    => typeof(Views.Library.MangaLibraryPage),
            "Discover" => typeof(Views.Discover.DiscoverPage),
            _          => typeof(Views.Dashboard.DashboardPage)
        };
        _navigationService.Navigate(pageType);
    }
}
