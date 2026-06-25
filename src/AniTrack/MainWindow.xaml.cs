using AniTrack.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniTrack;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel, INavigationService navigationService)
    {
        InitializeComponent();
        DataContext = viewModel;
        navigationService.SetNavigationControl(RootNavigation);
    }
}
