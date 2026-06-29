using AniMan.Core.Interfaces;
using AniMan.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniMan;

public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        IContentDialogService contentDialogService,
        ISettingsService settingsService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _navigationService = navigationService;
        _settingsService = settingsService;
        navigationService.SetNavigationControl(RootNavigation);
        contentDialogService.SetDialogHost(RootContentDialogHost);
        Loaded += OnWindowLoaded;
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
