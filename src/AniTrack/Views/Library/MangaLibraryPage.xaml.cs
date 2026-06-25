using AniTrack.ViewModels.Library;
using System.Windows.Controls;

namespace AniTrack.Views.Library;

public partial class MangaLibraryPage : UserControl
{
    public MangaLibraryPage(MangaLibraryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
