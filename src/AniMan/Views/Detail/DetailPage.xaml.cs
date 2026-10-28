using System.Diagnostics;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Detail;
using AniMan.Views.Library;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
        viewModel.SoftDeleteCompleted += OnSoftDeleteCompleted;
    }

    // Fires on every navigation to this page (robust against page caching).
    public async Task OnNavigatedToAsync()
    {
        var args = _navigationBag.Consume<DetailNavigationArgs>();
        if (args is not null)
            await ((DetailViewModel)DataContext).InitializeFromArgsAsync(args);
    }

    public async Task OnNavigatedFromAsync()
    {
        if (DataContext is DetailViewModel vm)
            await vm.SaveSeriesNoteCommand.ExecuteAsync(null);
    }

    private void OnGoBackClicked(object sender, RoutedEventArgs e) =>
        _navigationService.GoBack();

    private void OnStreamingPlatformClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not SWC.Button { Tag: string url }) return;

        // Streaming URLs come from the API and user overrides — only launch http(s).
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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
        var vm = (DetailViewModel)DataContext;
        var statusId = await Dialogs.AddToLibraryDialog.ShowAsync(
            _dialogService, vm.MediaSnapshot?.Title ?? string.Empty, e.MediaType == MediaType.Manga);
        if (statusId is null) return;

        await vm.AddToLibraryAndReloadAsync(e.MalId, e.MediaType, statusId.Value);
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
            await vm.UpdateStatusCommand.ExecuteAsync(TrackingStatusIds.Completed);
        }
    }

    // ── New handlers ──────────────────────────────────────────────────────────

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

    private async void OnRatingKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not SWC.TextBox tb)
            return;

        e.Handled = true;
        var moved = tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        if (!moved)
            await CommitRatingAsync(tb);
    }

    private async void OnRatingLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is SWC.TextBox tb)
            await CommitRatingAsync(tb);
    }

    private async Task CommitRatingAsync(SWC.TextBox tb)
    {
        try
        {
            if (DataContext is DetailViewModel vm)
            {
                await vm.SetRatingCommand.ExecuteAsync(tb.Text);
                BindingOperations.GetBindingExpression(tb, SWC.TextBox.TextProperty)?.UpdateTarget();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.ForContext<DetailPage>().Error(ex, "Rating commit failed");
        }
    }

    private async void OnSeriesNoteLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is DetailViewModel vm)
            await vm.SaveSeriesNoteCommand.ExecuteAsync(null);
    }

    private void OnPagePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not SWC.TextBox focusedTextBox)
            return;

        for (var current = e.OriginalSource as DependencyObject; current is not null; current = GetParentObject(current))
        {
            if (current is SWC.TextBox)
                return;
        }

        var scope = FocusManager.GetFocusScope(focusedTextBox);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        if (current is ContentElement contentElement)
            return ContentOperations.GetParent(contentElement) ?? (contentElement as FrameworkContentElement)?.Parent;

        return VisualTreeHelper.GetParent(current);
    }
}
