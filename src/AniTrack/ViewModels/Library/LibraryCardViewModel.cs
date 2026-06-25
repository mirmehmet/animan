using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniTrack.ViewModels.Library;

public partial class LibraryCardViewModel : ObservableObject
{
    public int LibraryItemId { get; init; }
    public int MalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverLocalPath { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int? Score { get; init; }
    public bool IsFavorite { get; init; }
    public int ProgressWatched { get; init; }
    public int? ProgressTotal { get; init; }

    [ObservableProperty]
    private BitmapImage? _coverImage;

    public string ProgressText => ProgressTotal.HasValue
        ? $"{ProgressWatched}/{ProgressTotal}"
        : $"{ProgressWatched}/?";

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
        catch { /* cover missing — keep null */ }
    }
}
