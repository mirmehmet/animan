using AniMan.Core.Common;
using AniMan.Infrastructure.Jikan;
using AniMan.Infrastructure.Jikan.Dtos;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.MediaSource;

/// <summary>
/// Tries the primary media source and falls back to the secondary when it fails.
/// <para>
/// Written for the Jikan outage that began 2026-07-10, where Jikan serves cached requests
/// normally but answers every cache miss with 504. Because the switch is per request and
/// there is no circuit breaker, the app returns to the primary the moment it starts
/// answering again — no state to reset and no timer to wait out.
/// </para>
/// </summary>
public sealed class FallbackMediaClient(
    IJikanClient primary,
    IJikanClient secondary,
    ILogger<FallbackMediaClient> logger) : IJikanClient
{
    public Task<Result<JikanPagedResult<JikanAnimeDto>>> SearchAnimeAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(SearchAnimeAsync),
            c => c.SearchAnimeAsync(query, limit, ct));

    public Task<Result<JikanPagedResult<JikanMangaDto>>> SearchMangaAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(SearchMangaAsync),
            c => c.SearchMangaAsync(query, limit, ct));

    public Task<Result<JikanSingleResult<JikanAnimeDto>>> GetAnimeFullAsync(
        int malId, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetAnimeFullAsync),
            c => c.GetAnimeFullAsync(malId, ct));

    public Task<Result<JikanSingleResult<JikanMangaDto>>> GetMangaFullAsync(
        int malId, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetMangaFullAsync),
            c => c.GetMangaFullAsync(malId, ct));

    public Task<Result<JikanPagedResult<JikanEpisodeDto>>> GetAnimeEpisodesAsync(
        int malId, int page = 1, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetAnimeEpisodesAsync),
            c => c.GetAnimeEpisodesAsync(malId, page, ct));

    public Task<Result<JikanSingleResult<IReadOnlyList<JikanStreamingDto>>>> GetAnimeStreamingAsync(
        int malId, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetAnimeStreamingAsync),
            c => c.GetAnimeStreamingAsync(malId, ct));

    public Task<Result<JikanPagedResult<JikanAnimeDto>>> GetCurrentSeasonAsync(
        CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetCurrentSeasonAsync),
            c => c.GetCurrentSeasonAsync(ct));

    public Task<Result<JikanPagedResult<JikanAnimeDto>>> GetTopAnimeAsync(
        int page = 1, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetTopAnimeAsync),
            c => c.GetTopAnimeAsync(page, ct));

    public Task<Result<JikanPagedResult<JikanMangaDto>>> GetTopMangaAsync(
        int page = 1, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetTopMangaAsync),
            c => c.GetTopMangaAsync(page, ct));

    /// <summary>
    /// Runs <paramref name="call"/> against the primary and, if it fails, against the
    /// secondary. When both fail the <b>primary's</b> error is returned: the UI already
    /// words that message for the user, and the secondary being unreachable too is not
    /// the more useful thing to report.
    /// </summary>
    private async Task<Result<T>> WithFallbackAsync<T>(
        string operation, Func<IJikanClient, Task<Result<T>>> call)
    {
        var primaryResult = await call(primary).ConfigureAwait(false);
        if (primaryResult.IsSuccess)
            return primaryResult;

        logger.LogInformation(
            "{Operation}: primary source failed ({Error}) — falling back to the secondary source",
            operation, primaryResult.Error);

        var secondaryResult = await call(secondary).ConfigureAwait(false);
        if (secondaryResult.IsSuccess)
        {
            logger.LogInformation("{Operation}: served by the secondary source", operation);
            return secondaryResult;
        }

        logger.LogWarning(
            "{Operation}: both sources failed (primary: {PrimaryError}; secondary: {SecondaryError})",
            operation, primaryResult.Error, secondaryResult.Error);

        return primaryResult;
    }
}
