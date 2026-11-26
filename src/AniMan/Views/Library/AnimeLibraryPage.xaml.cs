using System.Windows.Controls;
using AniMan.Navigation;
using AniMan.ViewModels.Library;
using Microsoft.Extensions.Logging;

namespace AniMan.Views.Library;

/// <summary>The anime library: a navigation target that hosts the shared <see cref="LibraryView"/>.</summary>
public partial class AnimeLibraryPage : UserControl
{
    public AnimeLibraryPage(AnimeLibraryViewModel viewModel, DetailNavigator detail, ILogger<AnimeLibraryPage> logger)
    {
        InitializeComponent();
        View.Attach(viewModel, detail, logger);
    }
}
