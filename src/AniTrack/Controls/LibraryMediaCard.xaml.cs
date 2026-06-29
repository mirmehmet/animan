using System.Windows;
using System.Windows.Media;

namespace AniTrack.Controls;

public partial class LibraryMediaCard : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty CoverSourceProperty =
        DependencyProperty.Register(nameof(CoverSource), typeof(ImageSource),
            typeof(LibraryMediaCard), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string),
            typeof(LibraryMediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusNameProperty =
        DependencyProperty.Register(nameof(StatusName), typeof(string),
            typeof(LibraryMediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusBackgroundProperty =
        DependencyProperty.Register(nameof(StatusBackground), typeof(Brush),
            typeof(LibraryMediaCard), new PropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty StatusForegroundProperty =
        DependencyProperty.Register(nameof(StatusForeground), typeof(Brush),
            typeof(LibraryMediaCard), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty ProgressTextProperty =
        DependencyProperty.Register(nameof(ProgressText), typeof(string),
            typeof(LibraryMediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProgressPercentProperty =
        DependencyProperty.Register(nameof(ProgressPercent), typeof(double),
            typeof(LibraryMediaCard), new PropertyMetadata(0.0));

    public static readonly DependencyProperty ProgressAccentProperty =
        DependencyProperty.Register(nameof(ProgressAccent), typeof(Brush),
            typeof(LibraryMediaCard), new PropertyMetadata(null));

    public ImageSource? CoverSource
    {
        get => (ImageSource?)GetValue(CoverSourceProperty);
        set => SetValue(CoverSourceProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string StatusName
    {
        get => (string)GetValue(StatusNameProperty);
        set => SetValue(StatusNameProperty, value);
    }

    public Brush StatusBackground
    {
        get => (Brush)GetValue(StatusBackgroundProperty);
        set => SetValue(StatusBackgroundProperty, value);
    }

    public Brush StatusForeground
    {
        get => (Brush)GetValue(StatusForegroundProperty);
        set => SetValue(StatusForegroundProperty, value);
    }

    public string ProgressText
    {
        get => (string)GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    public double ProgressPercent
    {
        get => (double)GetValue(ProgressPercentProperty);
        set => SetValue(ProgressPercentProperty, value);
    }

    public Brush? ProgressAccent
    {
        get => (Brush?)GetValue(ProgressAccentProperty);
        set => SetValue(ProgressAccentProperty, value);
    }

    public LibraryMediaCard()
    {
        InitializeComponent();
    }
}
