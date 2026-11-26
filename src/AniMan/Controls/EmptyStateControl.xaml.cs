using System.Windows;
using Wpf.Ui.Controls;

namespace AniMan.Controls;

public partial class EmptyStateControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(SymbolRegular),
            typeof(EmptyStateControl), new PropertyMetadata(SymbolRegular.MoviesAndTv24));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string),
            typeof(EmptyStateControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string),
            typeof(EmptyStateControl), new PropertyMetadata(string.Empty));

    public SymbolRegular Icon
    {
        get => (SymbolRegular)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public EmptyStateControl()
    {
        InitializeComponent();
    }
}
