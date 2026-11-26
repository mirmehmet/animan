using System.Windows.Controls;
using AniMan.Navigation;
using AniMan.ViewModels.Library;
using Microsoft.Extensions.Logging;

namespace AniMan.Views.Library;

/// <summary>The manga library: a navigation target that hosts the shared <see cref="LibraryView"/>.</summary>
public partial class MangaLibraryPage : UserControl
{
    public MangaLibraryPage(MangaLibraryViewModel viewModel, DetailNavigator detail, ILogger<MangaLibraryPage> logger)
    {
        InitializeComponent();
        View.Attach(viewModel, detail, logger);
    }
}
