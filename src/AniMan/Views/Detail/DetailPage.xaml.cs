using System.Diagnostics;
using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Detail;
using AniMan.Views.Library;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using SWC = System.Windows.Controls;

namespace AniMan.Views.Detail;

public partial class DetailPage : UserControl, INavigationAware
{
    private readonly IContentDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly NavigationBag _navigationBag;

    public DetailPage(DetailViewModel viewModel, IContentDialogService dialogService,
        NavigationBag navigationBag, INavigationService navigationService)
    {
        InitializeComponent();
        _dialogService = dialogService;
        _navigationService = navigationService;
        _navigationBag = navigationBag;
        DataContext = viewModel;
        viewModel.AutoCompleteRequested += OnAutoCompleteRequested;
        viewModel.AddToLibraryFromPreviewRequested += OnAddToLibraryFromPreviewRequested;
        viewModel.NoteEditRequested += OnNoteEditRequested;
        viewModel.NavigateToFavoritesRequested += OnNavigateToFavoritesRequested;
        viewModel.SoftDeleteCompleted += OnSoftDeleteCompleted;
    }

    // Fires on every navigation to this page (robust against page caching).
    public async Task OnNavigatedToAsync()
    {
        var args = _navigationBag.Consume<DetailNavigationArgs>();
        if (args is not null)
            await ((DetailViewModel)DataContext).InitializeFromArgsAsync(args);
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void OnGoBackClicked(object sender, RoutedEventArgs e) =>
        _navigationService.GoBack();

    private void OnStreamingPlatformClicked(object sender, RoutedEventArgs e)
    {
        if (sender is SWC.Button { Tag: string url } && !string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnAddToLibraryClicked(object sender, RoutedEventArgs e)
    {
        var vm = (DetailViewModel)DataContext;
        vm.RequestAddFromPreviewCommand.Execute(null);
    }

    private async void OnNoteEditRequested(object? sender, NoteEditEventArgs e)
    {
        var textBox = new SWC.TextBox
        {
            Text = e.CurrentText,
            AcceptsReturn = true,
            MinHeight = 100,
            MinWidth = 320,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Detail_NoteDialogTitle"),
            Content = textBox,
            PrimaryButtonText = LocalizationManager.Get("Common_Save"),
            CloseButtonText = LocalizationManager.Get("Common_Cancel")
        };

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary) return;

        var vm = (DetailViewModel)DataContext;
        await vm.SaveItemNoteAsync(e.EpisodeNumber, e.ChapterNumber, textBox.Text);
    }

    private async void OnAddToLibraryFromPreviewRequested(object? sender, (int MalId, MediaType MediaType) e)
    {
        bool isManga = e.MediaType == MediaType.Manga;

        var statusItems = isManga
            ? new[] {
                (Id: 7, Name: LocalizationManager.Get("Status_PlanToRead")),
                (Id: 2, Name: LocalizationManager.Get("Status_Reading")),
                (Id: 3, Name: LocalizationManager.Get("Status_Completed")),
                (Id: 4, Name: LocalizationManager.Get("Status_OnHold")),
                (Id: 5, Name: LocalizationManager.Get("Status_Dropped"))
              }
            : new[] {
                (Id: 6, Name: LocalizationManager.Get("Status_PlanToWatch")),
                (Id: 1, Name: LocalizationManager.Get("Status_Watching")),
                (Id: 3, Name: LocalizationManager.Get("Status_Completed")),
                (Id: 4, Name: LocalizationManager.Get("Status_OnHold")),
                (Id: 5, Name: LocalizationManager.Get("Status_Dropped"))
              };

        var comboBox = new SWC.ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 200
        };
        foreach (var (_, name) in statusItems)
            comboBox.Items.Add(name);
        comboBox.SelectedIndex = 0;

        var panel = new SWC.StackPanel();
        panel.Children.Add(new SWC.TextBlock
        {
            Text = ((DetailViewModel)DataContext).MediaSnapshot?.Title ?? string.Empty,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new SWC.TextBlock
        {
            Text = LocalizationManager.Get("Discover_SelectStatus"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(comboBox);

        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Discover_AddToLibrary"),
            Content = panel,
            PrimaryButtonText = LocalizationManager.Get("Common_Add"),
            CloseButtonText = LocalizationManager.Get("Common_Cancel")
        };

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary) return;

        int selectedStatusId = statusItems[comboBox.SelectedIndex].Id;
        var vm = (DetailViewModel)DataContext;
        await vm.AddToLibraryAndReloadAsync(e.MalId, e.MediaType, selectedStatusId);
    }

    private async void OnAutoCompleteRequested(object? sender, TrackingAutoCompleteEventArgs e)
    {
        var titleKey = e.IsManga ? "Detail_AutoCompleteTitleManga" : "Detail_AutoCompleteTitle";
        var contentKey = e.IsManga ? "Detail_AutoCompleteContentManga" : "Detail_AutoCompleteContent";

        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get(titleKey),
            Content = LocalizationManager.Get(contentKey),
            PrimaryButtonText = LocalizationManager.Get("Detail_AutoCompletePrimary"),
            CloseButtonText = LocalizationManager.Get("Detail_AutoCompleteClose")
        };

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result == ContentDialogResult.Primary)
        {
            var vm = (DetailViewModel)DataContext;
            await vm.UpdateStatusCommand.ExecuteAsync(3);
        }
    }

    public async Task InitializeAsync(int libraryItemId)
    {
        var vm = (DetailViewModel)DataContext;
        await vm.InitializeAsync(libraryItemId);
    }

    // ── New handlers ──────────────────────────────────────────────────────────

    private void OnNavigateToFavoritesRequested(object? sender, MediaType mediaType)
    {
        LibraryNavigationState.RequestFavorites(mediaType);
        if (mediaType == MediaType.Anime)
            _navigationService.Navigate(typeof(AnimeLibraryPage));
        else
            _navigationService.Navigate(typeof(MangaLibraryPage));
    }

    private void OnSoftDeleteCompleted(object? sender, EventArgs e) =>
        _navigationService.GoBack();

    private void OnStatusPillClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ButtonBase btn
            || btn.Tag is not string tagStr) return;
        if (!int.TryParse(tagStr, out int statusId)) return;
        var vm = (DetailViewModel)DataContext;
        vm.UpdateStatusCommand.Execute(statusId);
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        var vm = (DetailViewModel)DataContext;
        var title = vm.MediaSnapshot?.Title ?? string.Empty;

        var dialog = new ContentDialog
        {
            Title = string.Format(LocalizationManager.Get("Detail_DeleteTitle"), title),
            Content = LocalizationManager.Get("Detail_DeleteContent"),
            PrimaryButtonText = LocalizationManager.Get("Detail_DeleteConfirm"),
            CloseButtonText = LocalizationManager.Get("Common_Cancel")
        };

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result == ContentDialogResult.Primary)
            vm.SoftDeleteCommand.Execute(null);
    }

    private void OnRatingKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is SWC.TextBox tb)
        {
            var vm = (DetailViewModel)DataContext;
            vm.SetRatingCommand.Execute(tb.Text);
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }

    private void OnRatingLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is SWC.TextBox tb)
        {
            var vm = (DetailViewModel)DataContext;
            vm.SetRatingCommand.Execute(tb.Text);
        }
    }
}
