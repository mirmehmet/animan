using System.Windows;
using System.Windows.Input;

namespace AniTrack.Controls;

public partial class ErrorStateControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ErrorMessageProperty =
        DependencyProperty.Register(nameof(ErrorMessage), typeof(string),
            typeof(ErrorStateControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RetryCommandProperty =
        DependencyProperty.Register(nameof(RetryCommand), typeof(ICommand),
            typeof(ErrorStateControl), new PropertyMetadata(null));

    public static readonly DependencyProperty RetryTextProperty =
        DependencyProperty.Register(nameof(RetryText), typeof(string),
            typeof(ErrorStateControl), new PropertyMetadata("Retry"));

    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public string RetryText
    {
        get => (string)GetValue(RetryTextProperty);
        set => SetValue(RetryTextProperty, value);
    }

    public ErrorStateControl()
    {
        InitializeComponent();
    }
}
