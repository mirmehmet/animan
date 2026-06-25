using AniTrack.ViewModels.Detail;
using System.Windows.Controls;

namespace AniTrack.Views.Detail;

public partial class DetailPage : UserControl
{
    public DetailPage(DetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.AutoCompleteRequested += OnAutoCompleteRequested;
    }

    private async void OnAutoCompleteRequested(object? sender, TrackingAutoCompleteEventArgs e)
    {
        var dialog = new Wpf.Ui.Controls.ContentDialog
        {
            Title = "All episodes watched!",
            Content = "Mark this series as Completed?",
            PrimaryButtonText = "Yes, complete it",
            CloseButtonText = "Not yet"
        };

        var result = await dialog.ShowAsync();
        if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
        {
            var vm = (DetailViewModel)DataContext;
            await vm.UpdateStatusCommand.ExecuteAsync(3); // statusId=3 = Completed
        }
    }

    public async Task InitializeAsync(int libraryItemId)
    {
        var vm = (DetailViewModel)DataContext;
        await vm.InitializeAsync(libraryItemId);
    }
}
