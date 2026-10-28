using AniMan.Core.Common;
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
    public const int MaxCoverBytes = 10 * 1024 * 1024; // 10 MB

    public string GetLocalPath(int malId, MediaType mediaType) =>
        Path.Combine(storagePaths.CoversPath,
            $"{malId}_{mediaType.ToString().ToLowerInvariant()}.jpg");

    /// <summary>
    /// Returns the canonical cover path if a non-empty file already exists on disk; otherwise <c>null</c>.
    /// </summary>
    public string? GetExistingLocalPath(int malId, MediaType mediaType)
    {
        var filePath = GetLocalPath(malId, mediaType);
        return HasNonEmptyFile(filePath) ? filePath : null;
    }

    /// <summary>Downloads <paramref name="url"/> to the canonical path atomically, overwriting any stale copy.</summary>
    public async Task<string?> DownloadAsync(
        int malId, MediaType mediaType, string? url, CancellationToken ct = default)
    {
        if (url is null) return null;

        // The URL can come from an imported backup file or upstream API — accept only
        // safe external http/https hosts, never file://, localhost, or private LAN/metadata IPs.
        if (!HttpUrlGuard.IsAllowedWebUrl(url, out var uri) ||
            !HttpUrlGuard.IsSafeExternalHost(uri))
        {
            logger.LogWarning("Rejected unsafe or unsupported cover URL for {MalId}", malId);
            return null;
        }

        var filePath = GetLocalPath(malId, mediaType);
        var tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(storagePaths.CoversPath);

            var http = httpClientFactory.CreateClient("covers");
            var bytes = await http.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > MaxCoverBytes)
            {
                logger.LogWarning("Rejected cover payload of invalid size ({Bytes} bytes) for {MalId}",
                    bytes.Length, malId);
                return null;
            }

            await File.WriteAllBytesAsync(tmpPath, bytes, ct).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);
            return filePath;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDeleteFile(tmpPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteFile(tmpPath);
            logger.LogWarning(ex, "Cover download failed for {MalId} — continuing without cover", malId);
            return null;
        }
    }

    /// <summary>Returns the canonical path if a valid non-empty cover exists on disk, otherwise downloads it.</summary>
    public async Task<string?> EnsureAsync(
        int malId, MediaType mediaType, string? url, CancellationToken ct = default)
    {
        if (GetExistingLocalPath(malId, mediaType) is { } existing)
            return existing;

        return await DownloadAsync(malId, mediaType, url, ct).ConfigureAwait(false);
    }

    /// <summary>Deletes the canonical cover file for the given media item if present on disk.</summary>
    public void Delete(int malId, MediaType mediaType) =>
        TryDeleteFile(GetLocalPath(malId, mediaType));

    /// <summary>Removes cover files in <c>covers/</c> that are not present in <paramref name="referencedFullPaths"/>.</summary>
    public void SweepUnreferenced(ISet<string> referencedFullPaths)
    {
        if (!Directory.Exists(storagePaths.CoversPath))
            return;

        foreach (var file in Directory.GetFiles(storagePaths.CoversPath, "*.jpg"))
        {
            var fullPath = Path.GetFullPath(file);
            if (!referencedFullPaths.Contains(fullPath))
                TryDeleteFile(fullPath);
        }
    }

    private static bool HasNonEmptyFile(string path) =>
        new FileInfo(path) is { Exists: true, Length: > 0 };

    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete cover file {Path}", filePath);
        }
    }
}
