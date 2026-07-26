using AniMan.Core.Common;
using AniMan.Infrastructure.Tenrai;
using AniMan.Infrastructure.MediaSource.Dtos;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.MediaSource;

/// <summary>
/// Tries the primary media source and falls back to the secondary when it fails.
/// <para>
/// Because the switch is per request and there is no circuit breaker, the app returns to the
/// primary the moment it starts answering again — no state to reset and no timer to wait out.
/// This was written during the Jikan outage of July 2026 and kept after the move to Tenrai:
/// a single upstream going quiet should degrade the app, not stop it.
/// </para>
/// </summary>
public sealed class FallbackMediaClient(
    IMediaSourceClient primary,
    IMediaSourceClient secondary,
    ILogger<FallbackMediaClient> logger) : IMediaSourceClient
{
    public Task<Result<PagedResult<AnimeDto>>> SearchAnimeAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(SearchAnimeAsync),
            c => c.SearchAnimeAsync(query, limit, ct));

    public Task<Result<PagedResult<MangaDto>>> SearchMangaAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(SearchMangaAsync),
            c => c.SearchMangaAsync(query, limit, ct));

    public Task<Result<SingleResult<AnimeDto>>> GetAnimeFullAsync(
        int malId, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetAnimeFullAsync),
            c => c.GetAnimeFullAsync(malId, ct));

    public Task<Result<SingleResult<MangaDto>>> GetMangaFullAsync(
        int malId, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetMangaFullAsync),
            c => c.GetMangaFullAsync(malId, ct));

    public Task<Result<PagedResult<AnimeEpisodeDto>>> GetAnimeEpisodesAsync(
        int malId, int page = 1, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetAnimeEpisodesAsync),
            c => c.GetAnimeEpisodesAsync(malId, page, ct));

    public Task<Result<SingleResult<IReadOnlyList<StreamingDto>>>> GetAnimeStreamingAsync(
        int malId, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetAnimeStreamingAsync),
            c => c.GetAnimeStreamingAsync(malId, ct));

    public Task<Result<PagedResult<AnimeDto>>> GetCurrentSeasonAsync(
        CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetCurrentSeasonAsync),
            c => c.GetCurrentSeasonAsync(ct));

    public Task<Result<PagedResult<AnimeDto>>> GetTopAnimeAsync(
        int page = 1, CancellationToken ct = default) =>
        WithFallbackAsync(nameof(GetTopAnimeAsync),
            c => c.GetTopAnimeAsync(page, ct));

    public Task<Result<PagedResult<MangaDto>>> GetTopMangaAsync(
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
        string operation, Func<IMediaSourceClient, Task<Result<T>>> call)
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
