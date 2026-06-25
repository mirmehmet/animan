using AniTrack.ViewModels.Settings;
using System.Windows.Controls;

namespace AniTrack.Views.Settings;

public partial class SettingsPage : UserControl
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
