using System.Windows;
using AniTrack.Core.Domain.Enums;
using AniTrack.ViewModels.Discover;
using Wpf.Ui;
using Wpf.Ui.Controls;
using SWC = System.Windows.Controls;

namespace AniTrack.Views.Discover;

public partial class DiscoverPage : SWC.UserControl
{
    private readonly DiscoverViewModel _vm;
    private readonly IContentDialogService _dialogService;

    public DiscoverPage(DiscoverViewModel viewModel, IContentDialogService dialogService)
    {
        InitializeComponent();
        _vm = viewModel;
        _dialogService = dialogService;
        DataContext = viewModel;
        viewModel.AddToLibraryRequested += OnAddToLibraryRequested;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await _vm.LoadCommand.ExecuteAsync(null);

    private async void OnAddToLibraryRequested(object? sender, AddToLibraryEventArgs e)
    {
        var card = e.Card;
        bool isManga = card.MediaType == MediaType.Manga;

        var statusItems = isManga
            ? new[] { (Id: 7, Name: "Plan to read"), (Id: 2, Name: "Reading"), (Id: 3, Name: "Completed"), (Id: 4, Name: "On-hold"), (Id: 5, Name: "Dropped") }
            : new[] { (Id: 6, Name: "Plan to watch"), (Id: 1, Name: "Watching"), (Id: 3, Name: "Completed"), (Id: 4, Name: "On-hold"), (Id: 5, Name: "Dropped") };

        var comboBox = new SWC.ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 200
        };
        foreach (var (_, name) in statusItems)
            comboBox.Items.Add(name);
        comboBox.SelectedIndex = 0;

        var panel = new SWC.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = card.Title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Select status:",
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(comboBox);

        var dialog = new ContentDialog
        {
            Title = "Add to Library",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel"
        };

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary) return;

        int selectedStatusId = statusItems[comboBox.SelectedIndex].Id;
        await _vm.AddToLibraryAsync(card.MalId, card.MediaType, selectedStatusId);
    }
}
