using System.Net.Http;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels.Dashboard;

public partial class SeasonCardViewModel : ObservableObject
{
    private const int DecodeWidth = 160;

    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }

    [ObservableProperty] private BitmapImage? _coverImage;

    public async Task LoadCoverAsync(IHttpClientFactory httpClientFactory, CancellationToken ct = default)
    {
        if (await Imaging.CoverImageLoader.FromUrlAsync(httpClientFactory, CoverUrl, DecodeWidth, ct) is { } image)
            CoverImage = image;
    }
}

