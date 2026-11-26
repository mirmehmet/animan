using System.Net.Http;
using System.Windows.Media.Imaging;
using AniMan.Core.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels.Discover;

public partial class DiscoverCardViewModel : ObservableObject
{
    private const int DecodeWidth = 160;

    public int MalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }
    public double? Score { get; init; }
    public string? Type { get; init; }
    public MediaType MediaType { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAddButton))]
    private bool _isInLibrary;

    /// <summary>True while this title is being added; the "+" turns into a spinner and ignores clicks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAddButton))]
    private bool _isAdding;

    [ObservableProperty] private BitmapImage? _coverImage;

    public bool ShowAddButton => !IsInLibrary && !IsAdding;

    public async Task LoadCoverAsync(IHttpClientFactory httpClientFactory, CancellationToken ct = default)
    {
        if (await Imaging.CoverImageLoader.FromUrlAsync(httpClientFactory, CoverUrl, DecodeWidth, ct) is { } image)
            CoverImage = image;
    }
}
