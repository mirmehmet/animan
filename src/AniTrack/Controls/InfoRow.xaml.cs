using System.Windows;

namespace AniTrack.Controls;

public partial class InfoRow : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string),
            typeof(InfoRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string),
            typeof(InfoRow), new PropertyMetadata(null, OnValueChanged));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((InfoRow)d).Visibility = string.IsNullOrEmpty((string?)e.NewValue)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public InfoRow()
    {
        InitializeComponent();
    }
}
