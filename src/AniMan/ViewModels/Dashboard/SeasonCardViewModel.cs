using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using AniMan.Core.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels.Dashboard;

public partial class SeasonCardViewModel : ObservableObject
{
    private const int MaxCoverBytes = 10 * 1024 * 1024;
    private const int DecodeWidth = 160;

    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }

    [ObservableProperty] private BitmapImage? _coverImage;

    public async Task LoadCoverAsync(IHttpClientFactory httpClientFactory, CancellationToken ct = default)
    {
        if (!HttpUrlGuard.IsAllowedWebUrl(CoverUrl, out var uri) ||
            !HttpUrlGuard.IsSafeExternalHost(uri))
        {
            return;
        }

        var cacheKey = uri.AbsoluteUri;
        var cached = Imaging.CoverImageLoader.TryGetFromUrlCache(cacheKey, DecodeWidth);
        if (cached is not null)
        {
            CoverImage = cached;
            return;
        }

        try
        {
            var http = httpClientFactory.CreateClient("covers");
            var bytes = await http.GetByteArrayAsync(uri, ct);
            if (bytes.Length == 0 || bytes.Length > MaxCoverBytes)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
                return;

            await dispatcher.InvokeAsync(() =>
                CoverImage = Imaging.CoverImageLoader.FromBytes(bytes, DecodeWidth, cacheKey));
        }
        catch (OperationCanceledException) { throw; }
        catch { /* leave cover blank on failure */ }
    }
}

