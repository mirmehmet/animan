using System.Windows.Controls;

namespace AniTrack.Views.Settings;

public partial class DeleteConfirmationContent : UserControl
{
    public event Action<string>? ConfirmTextChanged;

    public DeleteConfirmationContent(string word)
    {
        InitializeComponent();
        WordDisplay.Text = word;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) =>
        ConfirmTextChanged?.Invoke(ConfirmTextBox.Text);
}
