using AniMan.Core.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

/// <summary>
/// Owns the on-disk cover cache (<c>covers/{malId}_{mediatype}.jpg</c>).
/// Downloads never throw — a failed cover is logged and reported as <c>null</c>,
/// so callers can always proceed without an image.
/// </summary>
public sealed class CoverStore(
    IHttpClientFactory httpClientFactory,
    StoragePaths storagePaths,
    ILogger<CoverStore> logger)
{
    public string GetLocalPath(int malId, MediaType mediaType) =>
        Path.Combine(storagePaths.CoversPath,
            $"{malId}_{mediaType.ToString().ToLowerInvariant()}.jpg");

    /// <summary>Downloads <paramref name="url"/> to the canonical path, overwriting any stale copy.</summary>
    public async Task<string?> DownloadAsync(
        int malId, MediaType mediaType, string? url, CancellationToken ct = default)
    {
        if (url is null) return null;

        // The URL can come from an imported backup file — accept only absolute
        // http/https, never file:// or other schemes.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Rejected cover URL with unsupported scheme for {MalId}", malId);
            return null;
        }

        var filePath = GetLocalPath(malId, mediaType);
        try
        {
            var http = httpClientFactory.CreateClient("covers");
            var bytes = await http.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
            return filePath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cover download failed for {MalId} — continuing without cover", malId);
            return null;
        }
    }

    /// <summary>Returns the canonical path if the cover already exists on disk, otherwise downloads it.</summary>
    public async Task<string?> EnsureAsync(
        int malId, MediaType mediaType, string? url, CancellationToken ct = default)
    {
        var filePath = GetLocalPath(malId, mediaType);
        if (File.Exists(filePath)) return filePath;
        return await DownloadAsync(malId, mediaType, url, ct).ConfigureAwait(false);
    }
}
