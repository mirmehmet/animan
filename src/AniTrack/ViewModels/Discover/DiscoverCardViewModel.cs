using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using AniTrack.Core.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniTrack.ViewModels.Discover;

public partial class DiscoverCardViewModel : ObservableObject
{
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
        if (string.IsNullOrEmpty(CoverUrl)) return;
        try
        {
            var http = httpClientFactory.CreateClient("covers");
            var bytes = await http.GetByteArrayAsync(CoverUrl, ct);

            // BitmapImage inherits DispatcherObject — must be created on the UI (STA) thread.
            // After GetByteArrayAsync we're on a ThreadPool MTA thread, so marshal back.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;   // set before StreamSource
                img.StreamSource = new MemoryStream(bytes);
                img.DecodePixelWidth = 160;
                img.EndInit();
                img.Freeze();
                CoverImage = img;
            });
        }
        catch (OperationCanceledException) { throw; }
        catch { /* leave cover blank on failure */ }
    }
}
