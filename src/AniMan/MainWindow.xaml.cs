using System.ComponentModel;
using AniMan.Core.Interfaces;
using AniMan.ViewModels;
using AniMan.ViewModels.Discover;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniMan;

public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private readonly DiscoverViewModel _discoverViewModel;
    private readonly ILogger<MainWindow> _logger;
    private bool _flushedOnClose;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        IContentDialogService contentDialogService,
        ISettingsService settingsService,
        DiscoverViewModel discoverViewModel,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _navigationService = navigationService;
        _settingsService = settingsService;
        _discoverViewModel = discoverViewModel;
        _logger = logger;
        navigationService.SetNavigationControl(RootNavigation);
        contentDialogService.SetDialogHost(RootContentDialogHost);
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    private async void OnDiscoverNavClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            // Re-clicking Discover while already on it resets to the initial view
            // (Anime tab, top list). From any other page the click navigates normally
            // and the preserved state is kept.
            if (_discoverViewModel.IsPageActive)
                await _discoverViewModel.ResetToInitialAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discover navigation reset failed");
        }
    }

    private async void OnWindowLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial startup page navigation failed");
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_flushedOnClose)
            return;

        if (Views.Detail.DetailPage.ActiveInstance?.TryGetTarget(out var detailPage) == true &&
            detailPage.HasPendingEdits)
        {
            e.Cancel = true;
            _flushedOnClose = true;
            try
            {
                await detailPage.FlushPendingEditsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush pending detail edits on window close");
            }

            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
                // Application shutdown is already in progress.
            }
        }
    }
}
