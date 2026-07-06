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

    // async void event handler: never let an exception escape to the dispatcher.
    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            await ((StatsViewModel)DataContext).LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Stats page load failed");
        }
    }
}
