using AniTrack.ViewModels.Library;
using System.Windows.Controls;

namespace AniTrack.Views.Library;

public partial class AnimeLibraryPage : UserControl
{
    public AnimeLibraryPage(AnimeLibraryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
