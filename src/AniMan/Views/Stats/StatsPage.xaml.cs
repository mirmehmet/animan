using System.Windows.Controls;
using AniMan.ViewModels.Stats;

namespace AniMan.Views.Stats;

public partial class StatsPage : UserControl
{
    public StatsPage(StatsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e) =>
        await ((StatsViewModel)DataContext).LoadCommand.ExecuteAsync(null);
}
