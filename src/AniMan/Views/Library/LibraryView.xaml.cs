using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);

            // Arriving from Stats with a filter (e.g. a clicked genre or year).
            if (LibraryNavigationState.TakeFilter(_viewModel.MediaType) is { } request)
                _viewModel.ApplyExternalFilter(request);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Type} library page load failed", _viewModel.MediaType);
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || (sender as FrameworkElement)?.DataContext is not LibraryCardViewModel card) return;
        _detail?.Open(card.MalId, card.MediaType, card.LibraryItemId, _viewModel.PageTitleKey);
    }
}
