using CommunityToolkit.Mvvm.ComponentModel;

namespace AniTrack.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentPage = "anime";
}
