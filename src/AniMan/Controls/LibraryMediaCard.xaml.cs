using System.Windows;
using System.Windows.Media;
using AniMan.Core.Library;

namespace AniMan.Controls;

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

    public static readonly DependencyProperty ShowRatingsProperty =
        DependencyProperty.Register(nameof(ShowRatings), typeof(bool),
            typeof(LibraryMediaCard), new PropertyMetadata(false));

    public static readonly DependencyProperty MalScoreTextProperty =
        DependencyProperty.Register(nameof(MalScoreText), typeof(string),
            typeof(LibraryMediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty UserRatingTextProperty =
        DependencyProperty.Register(nameof(UserRatingText), typeof(string),
            typeof(LibraryMediaCard), new PropertyMetadata(string.Empty));

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

    public bool ShowRatings
    {
        get => (bool)GetValue(ShowRatingsProperty);
        set => SetValue(ShowRatingsProperty, value);
    }

    public string MalScoreText
    {
        get => (string)GetValue(MalScoreTextProperty);
        set => SetValue(MalScoreTextProperty, value);
    }

    public string UserRatingText
    {
        get => (string)GetValue(UserRatingTextProperty);
        set => SetValue(UserRatingTextProperty, value);
    }

    // ── Size (library cover-size slider) ──────────────────────────────────────

    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(nameof(CardWidth), typeof(double),
            typeof(LibraryMediaCard), new PropertyMetadata(150.0));

    public static readonly DependencyProperty CoverHeightProperty =
        DependencyProperty.Register(nameof(CoverHeight), typeof(double),
            typeof(LibraryMediaCard), new PropertyMetadata(200.0));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(CardDetail),
            typeof(LibraryMediaCard), new PropertyMetadata(CardDetail.Standard, OnDetailChanged));

    private static readonly DependencyPropertyKey IsCompactKey =
        DependencyProperty.RegisterReadOnly(nameof(IsCompact), typeof(bool),
            typeof(LibraryMediaCard), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsDetailedKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDetailed), typeof(bool),
            typeof(LibraryMediaCard), new PropertyMetadata(false));

    public static readonly DependencyProperty IsCompactProperty = IsCompactKey.DependencyProperty;
    public static readonly DependencyProperty IsDetailedProperty = IsDetailedKey.DependencyProperty;

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CoverHeight
    {
        get => (double)GetValue(CoverHeightProperty);
        set => SetValue(CoverHeightProperty, value);
    }

    /// <summary>How much the card shows besides the cover; drives <see cref="IsCompact"/>/<see cref="IsDetailed"/>.</summary>
    public CardDetail Detail
    {
        get => (CardDetail)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>Cover only (title/status appear on hover).</summary>
    public bool IsCompact => (bool)GetValue(IsCompactProperty);

    /// <summary>Two-line title and MAL score in the footer.</summary>
    public bool IsDetailed => (bool)GetValue(IsDetailedProperty);

    private static void OnDetailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (LibraryMediaCard)d;
        var detail = (CardDetail)e.NewValue;
        card.SetValue(IsCompactKey, detail == CardDetail.Compact);
        card.SetValue(IsDetailedKey, detail == CardDetail.Detailed);
    }

    public LibraryMediaCard()
    {
        InitializeComponent();
    }
}
