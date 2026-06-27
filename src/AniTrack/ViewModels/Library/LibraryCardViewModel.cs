using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AniTrack.Core.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniTrack.ViewModels.Library;

public partial class LibraryCardViewModel : ObservableObject
{
    public int LibraryItemId { get; init; }
    public int MalId { get; init; }
    public MediaType MediaType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverLocalPath { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public string? StatusColor { get; init; }
    public int? Score { get; init; }
    public decimal? UserRating { get; init; }
    public double? MalScore { get; init; }
    public bool IsFavorite { get; init; }
    public int ProgressWatched { get; init; }
    public int? ProgressTotal { get; init; }

    [ObservableProperty]
    private BitmapImage? _coverImage;

    public string ProgressText => ProgressTotal.HasValue
        ? $"{ProgressWatched} / {ProgressTotal}"
        : $"{ProgressWatched} / ?";

    public double ProgressPercent => ProgressTotal is > 0
        ? Math.Min(100.0 * ProgressWatched / ProgressTotal.Value, 100)
        : 0;

    public Brush StatusBackground => StatusColor is not null
        ? DimBrush(StatusColor, 0.15)
        : StatusId switch
        {
            1 or 2 => new SolidColorBrush(Color.FromRgb(0x2a, 0x18, 0x40)),
            3      => new SolidColorBrush(Color.FromRgb(0x0e, 0x2e, 0x20)),
            4      => new SolidColorBrush(Color.FromRgb(0x2e, 0x24, 0x10)),
            5      => new SolidColorBrush(Color.FromRgb(0x2e, 0x10, 0x10)),
            _      => new SolidColorBrush(Color.FromRgb(0x1e, 0x1c, 0x28))
        };

    public Brush StatusForeground => StatusColor is not null
        ? ParseHexBrush(StatusColor)
        : StatusId switch
        {
            1 or 2 => new SolidColorBrush(Color.FromRgb(0x90, 0x60, 0xc8)),
            3      => new SolidColorBrush(Color.FromRgb(0x30, 0x90, 0x60)),
            4      => new SolidColorBrush(Color.FromRgb(0x8a, 0x70, 0x20)),
            5      => new SolidColorBrush(Color.FromRgb(0x80, 0x30, 0x30)),
            _      => new SolidColorBrush(Color.FromRgb(0x5a, 0x54, 0x78))
        };

    public Brush ProgressAccent => StatusColor is not null
        ? DimBrush(StatusColor, 0.6)
        : StatusId switch
        {
            1 or 2 => new SolidColorBrush(Color.FromRgb(0x70, 0x40, 0xa8)),
            3      => new SolidColorBrush(Color.FromRgb(0x1a, 0x60, 0x40)),
            4      => new SolidColorBrush(Color.FromRgb(0x70, 0x58, 0x20)),
            5      => new SolidColorBrush(Color.FromRgb(0x60, 0x20, 0x20)),
            _      => new SolidColorBrush(Color.FromRgb(0x38, 0x30, 0x50))
        };

    public void LoadCoverImage()
    {
        if (CoverLocalPath is null || !File.Exists(CoverLocalPath)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(CoverLocalPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            CoverImage = bmp;
        }
        catch { /* cover stays blank — non-critical */ }
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static SolidColorBrush ParseHexBrush(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0x5a, 0x54, 0x78));
        }
    }

    // Returns a darkened version of the given hex color (factor = 0..1, lower = darker).
    private static SolidColorBrush DimBrush(string hex, double factor)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(Color.FromRgb(
                (byte)(c.R * factor),
                (byte)(c.G * factor),
                (byte)(c.B * factor)));
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0x1e, 0x1c, 0x28));
        }
    }
}
