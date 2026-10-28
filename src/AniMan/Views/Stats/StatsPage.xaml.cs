using System.Windows.Controls;
using AniMan.ViewModels.Stats;
using Microsoft.Extensions.Logging;

namespace AniMan.Views.Stats;

public partial class StatsPage : UserControl
{
    private readonly ILogger<StatsPage> _logger;

    public StatsPage(StatsViewModel viewModel, ILogger<StatsPage> logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _logger = logger;
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
            _logger.LogError(ex, "Stats page load failed");
        }
    }
}
