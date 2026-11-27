using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AniMan.Diagnostics;
using AniMan.Navigation;
using AniMan.ViewModels.Library;
using Microsoft.Extensions.Logging;

namespace AniMan.Views.Library;

/// <summary>
/// The library page body shared by anime and manga (audit AM-097: the two pages were
/// line-for-line copies). The hosting page hands over its view model with <see cref="Attach"/>.
/// </summary>
public partial class LibraryView : UserControl
{
    private LibraryViewModelBase? _viewModel;
    private DetailNavigator? _detail;
    private ILogger? _logger;

    // A field initializer runs before InitializeComponent, so the first visit's time includes building this view.
    private readonly PageTiming _timing = new();

    public LibraryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void Attach(LibraryViewModelBase viewModel, DetailNavigator detail, ILogger logger)
    {
        _viewModel = viewModel;
        _detail = detail;
        _logger = logger;
        _timing.Page = $"Library {viewModel.MediaType}";
        _timing.Logger = logger;
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        try
        {
            var visit = _timing.BeginVisit();
            await _viewModel.LoadCommand.ExecuteAsync(null);
            visit.DataLoaded();

            // Arriving from Stats with a filter (e.g. a clicked genre or year).
            if (LibraryNavigationState.TakeFilter(_viewModel.MediaType) is { } request)
                _viewModel.ApplyExternalFilter(request);

            await visit.LogWhenRenderedAsync(Dispatcher);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Type} library page load failed", _viewModel.MediaType);
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenCard(sender);

    /// <summary>A card reached with Tab opens with Enter or Space, like a button (AM-128).</summary>
    private void Card_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true; // Space would otherwise scroll the page
        OpenCard(sender);
    }

    private void OpenCard(object sender)
    {
        if (_viewModel is null || (sender as FrameworkElement)?.DataContext is not LibraryCardViewModel card) return;
        _detail?.Open(card.MalId, card.MediaType, card.LibraryItemId, _viewModel.PageTitleKey);
    }
}
