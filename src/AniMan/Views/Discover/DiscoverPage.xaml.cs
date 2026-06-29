using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Discover;
using Wpf.Ui;
using Wpf.Ui.Controls;
using SWC = System.Windows.Controls;

namespace AniMan.Views.Discover;

public partial class DiscoverPage : SWC.UserControl
{
    private readonly DiscoverViewModel _vm;
    private readonly IContentDialogService _dialogService;
    private readonly NavigationBag _bag;
    private readonly INavigationService _navigationService;

    public DiscoverPage(DiscoverViewModel viewModel, IContentDialogService dialogService,
        NavigationBag bag, INavigationService navigationService)
    {
        InitializeComponent();
        _vm = viewModel;
        _dialogService = dialogService;
        _bag = bag;
        _navigationService = navigationService;
        DataContext = viewModel;
        viewModel.AddToLibraryRequested += OnAddToLibraryRequested;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await _vm.LoadCommand.ExecuteAsync(null);

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm.SearchCommand.Execute(null);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.ExtentHeight > 0 && sv.VerticalOffset >= sv.ExtentHeight * 0.85)
            _vm.LoadMoreCommand.Execute(null);
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DiscoverCardViewModel card) return;
        var backLabel = "← " + LocalizationManager.Get("Discover_Title");
        _bag.Put(new DetailNavigationArgs(card.MalId, card.MediaType, null, backLabel));
        _navigationService.Navigate(typeof(Detail.DetailPage));
    }

    private async void OnAddToLibraryRequested(object? sender, AddToLibraryEventArgs e)
    {
        var card = e.Card;
        bool isManga = card.MediaType == MediaType.Manga;

        var statusItems = isManga
            ? new[] { (Id: 7, Name: LocalizationManager.Get("Status_PlanToRead")), (Id: 2, Name: LocalizationManager.Get("Status_Reading")), (Id: 3, Name: LocalizationManager.Get("Status_Completed")), (Id: 4, Name: LocalizationManager.Get("Status_OnHold")), (Id: 5, Name: LocalizationManager.Get("Status_Dropped")) }
            : new[] { (Id: 6, Name: LocalizationManager.Get("Status_PlanToWatch")), (Id: 1, Name: LocalizationManager.Get("Status_Watching")), (Id: 3, Name: LocalizationManager.Get("Status_Completed")), (Id: 4, Name: LocalizationManager.Get("Status_OnHold")), (Id: 5, Name: LocalizationManager.Get("Status_Dropped")) };

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
            Text = card.Title,
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
        await _vm.AddToLibraryAsync(card.MalId, card.MediaType, selectedStatusId);
    }
}
