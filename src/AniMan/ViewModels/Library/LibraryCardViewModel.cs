using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AniMan.Core.Domain.Enums;
using AniMan.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels.Library;

public partial class LibraryCardViewModel : ObservableObject
{
    private static readonly ConcurrentDictionary<(string Hex, double Factor), SolidColorBrush> BrushCache = new();

    private static readonly SolidColorBrush BgStatus12  = FrozenBrush(0x2a, 0x18, 0x40);
    private static readonly SolidColorBrush BgStatus3   = FrozenBrush(0x0e, 0x2e, 0x20);
    private static readonly SolidColorBrush BgStatus4   = FrozenBrush(0x2e, 0x24, 0x10);
    private static readonly SolidColorBrush BgStatus5   = FrozenBrush(0x2e, 0x10, 0x10);
    private static readonly SolidColorBrush BgStatusDef = FrozenBrush(0x1e, 0x1c, 0x28);

    private static readonly SolidColorBrush FgStatus12  = FrozenBrush(0x90, 0x60, 0xc8);
    private static readonly SolidColorBrush FgStatus3   = FrozenBrush(0x30, 0x90, 0x60);
    private static readonly SolidColorBrush FgStatus4   = FrozenBrush(0x8a, 0x70, 0x20);
    private static readonly SolidColorBrush FgStatus5   = FrozenBrush(0x80, 0x30, 0x30);
    private static readonly SolidColorBrush FgStatusDef = FrozenBrush(0x5a, 0x54, 0x78);

    private static readonly SolidColorBrush AccStatus12  = FrozenBrush(0x70, 0x40, 0xa8);
    private static readonly SolidColorBrush AccStatus3   = FrozenBrush(0x1a, 0x60, 0x40);
    private static readonly SolidColorBrush AccStatus4   = FrozenBrush(0x70, 0x58, 0x20);
    private static readonly SolidColorBrush AccStatus5   = FrozenBrush(0x60, 0x20, 0x20);
    private static readonly SolidColorBrush AccStatusDef = FrozenBrush(0x38, 0x30, 0x50);

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

    public string MalScoreText   => MalScore is { } s ? s.ToString("0.0") : "–";
    public string UserRatingText => UserRating is { } r ? r.ToString("0.0") : "–";

    public double ProgressPercent => ProgressTotal is > 0
        ? Math.Min(100.0 * ProgressWatched / ProgressTotal.Value, 100)
        : 0;

    public Brush StatusBackground => StatusColor is not null
        ? DimBrush(StatusColor, 0.15)
        : StatusId switch
        {
            1 or 2 => BgStatus12,
            3      => BgStatus3,
            4      => BgStatus4,
            5      => BgStatus5,
            _      => BgStatusDef
        };

    public Brush StatusForeground => StatusColor is not null
        ? ParseHexBrush(StatusColor)
        : StatusId switch
        {
            1 or 2 => FgStatus12,
            3      => FgStatus3,
            4      => FgStatus4,
            5      => FgStatus5,
            _      => FgStatusDef
        };

    public Brush ProgressAccent => StatusColor is not null
        ? DimBrush(StatusColor, 0.6)
        : StatusId switch
        {
            1 or 2 => AccStatus12,
            3      => AccStatus3,
            4      => AccStatus4,
            5      => AccStatus5,
            _      => AccStatusDef
        };

    public void LoadCoverImage()
    {
        var bmp = CoverImageLoader.FromFile(CoverLocalPath);
        if (bmp is not null) CoverImage = bmp;
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush ParseHexBrush(string hex) =>
        BrushCache.GetOrAdd((hex, 1.0), static key =>
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(key.Hex);
                return FrozenBrush(c.R, c.G, c.B);
            }
            catch
            {
                return FgStatusDef;
            }
        });

    // Returns a darkened version of the given hex color (factor = 0..1, lower = darker).
    private static SolidColorBrush DimBrush(string hex, double factor) =>
        BrushCache.GetOrAdd((hex, factor), static key =>
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(key.Hex);
                return FrozenBrush(
                    (byte)(c.R * key.Factor),
                    (byte)(c.G * key.Factor),
                    (byte)(c.B * key.Factor));
            }
            catch
            {
                return BgStatusDef;
            }
        });
}
