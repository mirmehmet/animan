using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels.Discover;

public partial class DiscoverCardViewModel : ObservableObject
{
    private const int MaxCoverBytes = 10 * 1024 * 1024;
    private const int DecodeWidth = 160;

    public int MalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }
    public double? Score { get; init; }
    public string? Type { get; init; }
    public MediaType MediaType { get; init; }

    [ObservableProperty] private bool _isInLibrary;
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

            // BitmapImage inherits DispatcherObject — must be created on the UI (STA) thread.
            // After GetByteArrayAsync we're on a ThreadPool MTA thread, so marshal back.
            await dispatcher.InvokeAsync(() =>
                CoverImage = Imaging.CoverImageLoader.FromBytes(bytes, DecodeWidth, cacheKey));
        }
        catch (OperationCanceledException) { throw; }
        catch { /* leave cover blank on failure */ }
    }
}
