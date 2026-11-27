using System.Collections.Concurrent;
using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.MediaSource;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

public sealed class CatalogService(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IMediaSourceClient sourceClient,
    ISettingsService settingsService,
    BackgroundWork backgroundWork,
    ILogger<CatalogService> logger) : ICatalogService
{
    private const int MaxEpisodePages = 100;

    public static void ClearCaches() => NegativeStreamingCache.Clear();

    /// <summary>
    /// Runs <paramref name="work"/> while holding the catalog write gate, so no cache refresh
    /// writes in between (used by the full data reset). <paramref name="work"/> must not call
    /// back into CatalogService writes — the gate is not re-entrant.
    /// </summary>
    internal static async Task RunExclusiveAsync(Func<Task> work, CancellationToken ct)
    {
        await WriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public async Task<Result<IReadOnlyList<CachedAnime>>> SearchAnimeAsync(
        string query, CancellationToken ct = default)
    {
        try
        {
            var apiResult = await sourceClient.SearchAnimeAsync(query, ct: ct).ConfigureAwait(false);
            if (!apiResult.IsSuccess)
                return Result<IReadOnlyList<CachedAnime>>.Failure(apiResult.Error!, apiResult.Kind);

            var items = apiResult.Value!.Data ?? [];
            var anime = await UpsertAnimeListAsync(items, ct).ConfigureAwait(false);
            return Result<IReadOnlyList<CachedAnime>>.Success(anime);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "SearchAnime failed for '{Query}'", query);
            return Result<IReadOnlyList<CachedAnime>>.Failure("Search failed. Please try again.");
        }
    }

    public async Task<Result<IReadOnlyList<CachedManga>>> SearchMangaAsync(
        string query, CancellationToken ct = default)
    {
        try
        {
            var apiResult = await sourceClient.SearchMangaAsync(query, ct: ct).ConfigureAwait(false);
            if (!apiResult.IsSuccess)
                return Result<IReadOnlyList<CachedManga>>.Failure(apiResult.Error!, apiResult.Kind);

            var items = apiResult.Value!.Data ?? [];
            var manga = await UpsertMangaListAsync(items, ct).ConfigureAwait(false);
            return Result<IReadOnlyList<CachedManga>>.Success(manga);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "SearchManga failed for '{Query}'", query);
            return Result<IReadOnlyList<CachedManga>>.Failure("Search failed. Please try again.");
        }
    }

    public async Task<Result<CachedAnime>> GetAnimeAsync(
        int malId, bool force = false, CancellationToken ct = default)
    {
        if (malId <= 0)
            return Result<CachedAnime>.Failure("Invalid anime ID.");

        try
        {
            if (force)
                return await FetchAndCacheAnimeAsync(malId, ct).ConfigureAwait(false);

            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var cached = await db.Anime.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == malId, ct).ConfigureAwait(false);

            if (cached is not null)
            {
                if (cached.DataVersion >= MediaDataVersion.Current &&
                    !await IsStaleAsync(cached.FetchedAt, ct).ConfigureAwait(false))
                    return Result<CachedAnime>.Success(cached);

                QueueBackgroundRefresh("anime", malId,
                    bg => FetchAndCacheAnimeAsync(malId, bg));
                return Result<CachedAnime>.Success(cached);
            }

            return await FetchAndCacheAnimeAsync(malId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetAnime failed for {Id}", malId);
            return Result<CachedAnime>.Failure("Failed to load anime details.");
        }
    }

    public async Task<Result<CachedManga>> GetMangaAsync(
        int malId, bool force = false, CancellationToken ct = default)
    {
        if (malId <= 0)
            return Result<CachedManga>.Failure("Invalid manga ID.");

        try
        {
            if (force)
                return await FetchAndCacheMangaAsync(malId, ct).ConfigureAwait(false);

            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var cached = await db.Manga.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == malId, ct).ConfigureAwait(false);

            if (cached is not null)
            {
                if (cached.DataVersion >= MediaDataVersion.Current &&
                    !await IsStaleAsync(cached.FetchedAt, ct).ConfigureAwait(false))
                    return Result<CachedManga>.Success(cached);

                QueueBackgroundRefresh("manga", malId,
                    bg => FetchAndCacheMangaAsync(malId, bg));
                return Result<CachedManga>.Success(cached);
            }

            return await FetchAndCacheMangaAsync(malId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetManga failed for {Id}", malId);
            return Result<CachedManga>.Failure("Failed to load manga details.");
        }
    }

    public async Task<Result<IReadOnlyList<CachedEpisode>>> GetAnimeEpisodesAsync(
        int malId, CancellationToken ct = default)
    {
        if (malId <= 0)
            return Result<IReadOnlyList<CachedEpisode>>.Failure("Invalid anime ID.");

        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var anime = await db.Anime.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == malId, ct).ConfigureAwait(false);

            if (anime is null)
            {
                var animeResult = await GetAnimeAsync(malId, force: false, ct).ConfigureAwait(false);
                if (!animeResult.IsSuccess)
                    return Result<IReadOnlyList<CachedEpisode>>.Failure(animeResult.Error!, animeResult.Kind);
                anime = animeResult.Value!;
            }

            var cached = await db.Episodes.AsNoTracking()
                .Where(e => e.AnimeId == malId)
                .OrderBy(e => e.EpisodeNumber)
                .ToListAsync(ct).ConfigureAwait(false);

            if (cached.Count > 0)
            {
                // Freshness follows the episodes' own fetch time: the anime row is re-stamped
                // whenever the title shows up in a season/search/top list, which would otherwise
                // keep an airing show's episode list "fresh" forever.
                // Stale-while-revalidate, like the anime/manga rows: answer from the cache at once
                // (also offline) and refresh in the background.
                var oldestFetch = cached.Min(e => e.FetchedAt);
                if (await IsStaleAsync(oldestFetch, ct).ConfigureAwait(false))
                {
                    var totalEpisodes = anime.TotalEpisodes;
                    QueueBackgroundRefresh("episodes", malId,
                        bg => FetchAndCacheEpisodesAsync(malId, totalEpisodes, cached, bg));
                }

                return Result<IReadOnlyList<CachedEpisode>>.Success(cached);
            }

            return await FetchAndCacheEpisodesAsync(malId, anime.TotalEpisodes, cached, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetAnimeEpisodes failed for {Id}", malId);
            return Result<IReadOnlyList<CachedEpisode>>.Failure("Failed to load episode list.");
        }
    }

    public async Task<Result<IReadOnlyList<CachedAnime>>> GetCurrentSeasonAsync(
        CancellationToken ct = default)
    {
        try
        {
            var result = await sourceClient.GetCurrentSeasonAsync(ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result<IReadOnlyList<CachedAnime>>.Failure(result.Error!, result.Kind);

            var anime = await UpsertAnimeListAsync(result.Value!.Data ?? [], ct).ConfigureAwait(false);
            return Result<IReadOnlyList<CachedAnime>>.Success(anime);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetCurrentSeason failed");
            return Result<IReadOnlyList<CachedAnime>>.Failure("Failed to load season data.");
        }
    }

    public async Task<Result<PagedList<CachedAnime>>> GetTopAnimeAsync(
        int page = 1, CancellationToken ct = default)
    {
        try
        {
            var result = await sourceClient.GetTopAnimeAsync(page, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result<PagedList<CachedAnime>>.Failure(result.Error!, result.Kind);

            var anime = await UpsertAnimeListAsync(result.Value!.Data ?? [], ct).ConfigureAwait(false);
            return Result<PagedList<CachedAnime>>.Success(
                new PagedList<CachedAnime>(anime, result.Value.Pagination?.HasNextPage ?? false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTopAnime page {Page} failed", page);
            return Result<PagedList<CachedAnime>>.Failure("Failed to load top anime.");
        }
    }

    public async Task<Result<PagedList<CachedManga>>> GetTopMangaAsync(
        int page = 1, CancellationToken ct = default)
    {
        try
        {
            var result = await sourceClient.GetTopMangaAsync(page, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result<PagedList<CachedManga>>.Failure(result.Error!, result.Kind);

            var manga = await UpsertMangaListAsync(result.Value!.Data ?? [], ct).ConfigureAwait(false);
            return Result<PagedList<CachedManga>>.Success(
                new PagedList<CachedManga>(manga, result.Value.Pagination?.HasNextPage ?? false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTopManga page {Page} failed", page);
            return Result<PagedList<CachedManga>>.Failure("Failed to load top manga.");
        }
    }

    public async Task<Result<IReadOnlyList<CachedAnimeStreaming>>> GetAnimeStreamingAsync(
        int malId, bool force = false, CancellationToken ct = default)
    {
        if (malId <= 0)
            return Result<IReadOnlyList<CachedAnimeStreaming>>.Failure("Invalid anime ID.");

        try
        {
            if (!force)
            {
                await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                var rows = await db.AnimeStreaming.AsNoTracking()
                    .Where(s => s.AnimeId == malId).ToListAsync(ct).ConfigureAwait(false);

                if (rows.Count > 0)
                    return Result<IReadOnlyList<CachedAnimeStreaming>>.Success(rows);

                if (NegativeStreamingCache.TryGetValue(malId, out var lastChecked) &&
                    DateTime.UtcNow - lastChecked < TimeSpan.FromHours(24))
                {
                    return Result<IReadOnlyList<CachedAnimeStreaming>>.Success(rows);
                }
            }

            return await FetchStreamingSharedAsync(malId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetAnimeStreaming failed for {Id}", malId);
            return Result<IReadOnlyList<CachedAnimeStreaming>>.Failure("Failed to load streaming info.");
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetGenresAsync(
        int malId, string mediaType, CancellationToken ct = default)
    {
        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var names = await db.MediaGenres
                .Where(mg => mg.MediaId == malId && mg.MediaType == mediaType)
                .Join(db.Genres, mg => mg.GenreId, g => g.Id, (_, g) => g.Name)
                .ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<string>>.Success(names);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetGenres failed for {Id}/{Type}", malId, mediaType);
            return Result<IReadOnlyList<string>>.Failure("Failed to load genres.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // The service is registered transient — the in-flight set and write gate must be
    // static to deduplicate refreshes and serialize catalog writes across instances.
    private static readonly ConcurrentDictionary<(string Kind, int MalId), byte> InFlightRefreshes = new();
    private static readonly ConcurrentDictionary<int, DateTime> NegativeStreamingCache = new();
    private static readonly ConcurrentDictionary<int, Task<Result<IReadOnlyList<CachedAnimeStreaming>>>> StreamingFetches = new();
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    /// <summary>
    /// Fetches an anime's streaming links once even when several callers ask at the same time
    /// (the detail page and a refresh, say): the first caller fetches, the others await its
    /// result. If the first caller gives up (its page closed), a caller still waiting fetches
    /// for itself instead of failing. While the fetch runs, the background refresh for the same
    /// title stands down.
    /// </summary>
    private async Task<Result<IReadOnlyList<CachedAnimeStreaming>>> FetchStreamingSharedAsync(
        int malId, CancellationToken ct)
    {
        while (true)
        {
            var mine = new TaskCompletionSource<Result<IReadOnlyList<CachedAnimeStreaming>>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var shared = StreamingFetches.GetOrAdd(malId, mine.Task);
            if (shared != mine.Task)
            {
                try
                {
                    return await shared.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    continue; // the fetching caller was cancelled, not this one: fetch again
                }
            }

            var ownsInFlight = InFlightRefreshes.TryAdd(("streaming", malId), 0);
            try
            {
                var result = await FetchAndCacheStreamingAsync(malId, ct).ConfigureAwait(false);
                mine.SetResult(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                mine.SetCanceled(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                // Handed to waiters as a failed result rather than an exception, so a fetch
                // nobody else awaited cannot surface later as an unobserved task exception.
                logger.LogError(ex, "Streaming fetch failed for {Id}", malId);
                var failure = Result<IReadOnlyList<CachedAnimeStreaming>>.Failure("Failed to load streaming info.");
                mine.SetResult(failure);
                return failure;
            }
            finally
            {
                StreamingFetches.TryRemove(new KeyValuePair<int, Task<Result<IReadOnlyList<CachedAnimeStreaming>>>>(malId, mine.Task));
                if (ownsInFlight)
                    InFlightRefreshes.TryRemove(("streaming", malId), out _);
            }
        }
    }

    /// <summary>
    /// Runs a fire-and-forget cache refresh at most once per (kind, id) at a time. It gets the
    /// app's shutdown token and is logged, not thrown, if it fails (see <see cref="BackgroundWork"/>).
    /// </summary>
    private void QueueBackgroundRefresh(string kind, int malId, Func<CancellationToken, Task> refresh)
    {
        if (backgroundWork.Stopping.IsCancellationRequested || !InFlightRefreshes.TryAdd((kind, malId), 0))
            return;

        backgroundWork.Run($"{kind} refresh for {malId}", async ct =>
        {
            try
            {
                await refresh(ct).ConfigureAwait(false);
            }
            finally
            {
                InFlightRefreshes.TryRemove((kind, malId), out _);
            }
        });
    }

    /// <summary>
    /// Runs a cache write that must never sink an already-successful API response.
    /// The fetched data is the product; the cache is only an optimisation, so a
    /// write failure is logged and swallowed. Cancellation still propagates — that
    /// is the caller giving up, not the cache failing.
    /// </summary>
    private async Task TryCacheAsync(string what, Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Caching {What} failed — serving fetched data uncached", what);
        }
    }

    private async Task<bool> IsStaleAsync(DateTime fetchedAt, CancellationToken ct)
    {
        var refreshDays = await settingsService.GetCacheRefreshDaysAsync(ct).ConfigureAwait(false);
        return fetchedAt.AddDays(refreshDays) < DateTime.UtcNow;
    }

    private async Task<Result<CachedAnime>> FetchAndCacheAnimeAsync(
        int malId, CancellationToken ct)
    {
        var apiResult = await sourceClient.GetAnimeFullAsync(malId, ct).ConfigureAwait(false);
        if (!apiResult.IsSuccess || apiResult.Value?.Data is null)
            return Result<CachedAnime>.Failure(apiResult.Error ?? "No data from the media source", apiResult.Kind);

        var anime = MediaMapper.ToAnime(apiResult.Value.Data);
        // Answer with the stored row after the merge: when the fallback source filled only
        // gaps, the MAL data already cached is the better answer (snapshots are built from it).
        var stored = anime;
        await TryCacheAsync($"anime {malId}",
            async () => stored = await UpsertAnimeAsync(anime, apiResult.Value.Data, ct).ConfigureAwait(false))
            .ConfigureAwait(false);
        return Result<CachedAnime>.Success(stored);
    }

    private async Task<Result<CachedManga>> FetchAndCacheMangaAsync(
        int malId, CancellationToken ct)
    {
        var apiResult = await sourceClient.GetMangaFullAsync(malId, ct).ConfigureAwait(false);
        if (!apiResult.IsSuccess || apiResult.Value?.Data is null)
            return Result<CachedManga>.Failure(apiResult.Error ?? "No data from the media source", apiResult.Kind);

        var manga = MediaMapper.ToManga(apiResult.Value.Data);
        var stored = manga;
        await TryCacheAsync($"manga {malId}",
            async () => stored = await UpsertMangaAsync(manga, apiResult.Value.Data, ct).ConfigureAwait(false))
            .ConfigureAwait(false);
        return Result<CachedManga>.Success(stored);
    }

    private async Task<Result<IReadOnlyList<CachedEpisode>>> FetchAndCacheEpisodesAsync(
        int malId, int? totalEpisodes, IReadOnlyList<CachedEpisode> existingCached, CancellationToken ct)
    {
        var allEpisodes = new List<CachedEpisode>();
        var failedMidway = false;
        string? failureError = null;
        int page = 1;

        while (page <= MaxEpisodePages)
        {
            var result = await sourceClient.GetAnimeEpisodesAsync(malId, page, ct).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value?.Data is null)
            {
                failedMidway = true;
                failureError = result.Error;
                break;
            }

            if (result.Value.Data.Count == 0)
            {
                // If an earlier page reported HasNextPage == true and we still haven't reached
                // totalEpisodes, an empty page (e.g. AniList fallback on page > 1) means the
                // multi-page list was truncated rather than legitimately empty.
                if (page > 1 && (totalEpisodes is null || allEpisodes.Count < totalEpisodes.Value))
                    failedMidway = true;
                break;
            }

            var episodes = result.Value.Data.Select(e => MediaMapper.ToEpisode(e, malId));
            allEpisodes.AddRange(episodes);

            if (result.Value.Pagination?.HasNextPage != true) break;
            page++;
        }

        allEpisodes = allEpisodes
            .Where(e => e.EpisodeNumber > 0)
            .DistinctBy(e => e.EpisodeNumber)
            .OrderBy(e => e.EpisodeNumber)
            .ToList();

        if (failedMidway)
        {
            if (existingCached.Count > 0)
            {
                logger.LogWarning(
                    "Episode refresh failed at page {Page} for anime {Id}; preserving {Count} cached episodes",
                    page, malId, existingCached.Count);
                return Result<IReadOnlyList<CachedEpisode>>.Success(existingCached);
            }

            if (allEpisodes.Count == 0)
                return Result<IReadOnlyList<CachedEpisode>>.Failure(failureError ?? "Failed to load episode list.");

            logger.LogWarning(
                "Episode fetch truncated at page {Page} for anime {Id}; serving {Count} episodes without overwriting cache",
                page, malId, allEpisodes.Count);
            return Result<IReadOnlyList<CachedEpisode>>.Success(allEpisodes);
        }

        // An empty answer is "nothing to add", never "the episodes are gone": the fallback
        // source has no episode lists at all, so it must not replace titled cached episodes.
        if (allEpisodes.Count == 0 && existingCached.Count > 0)
        {
            logger.LogDebug("Source returned no episodes for anime {Id}; keeping {Count} cached episodes",
                malId, existingCached.Count);
            return Result<IReadOnlyList<CachedEpisode>>.Success(existingCached);
        }

        // Placeholder fallback: the source lists no episodes but we know the total
        if (allEpisodes.Count == 0 && totalEpisodes > 0)
        {
            for (int i = 1; i <= totalEpisodes; i++)
                allEpisodes.Add(MediaMapper.ToPlaceholderEpisode(malId, i));
        }

        await TryCacheAsync($"episodes for anime {malId}", async () =>
        {
            await WriteGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                if (!await db.Anime.AnyAsync(a => a.Id == malId, ct).ConfigureAwait(false))
                {
                    logger.LogDebug("Skipping episode cache write for anime {Id}: parent anime not in catalog", malId);
                    return;
                }

                var existing = db.Episodes.Where(e => e.AnimeId == malId);
                db.Episodes.RemoveRange(existing);
                db.Episodes.AddRange(allEpisodes);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                logger.LogInformation("Cached {Count} episodes for anime {Id}", allEpisodes.Count, malId);
            }
            finally
            {
                WriteGate.Release();
            }
        }).ConfigureAwait(false);

        return Result<IReadOnlyList<CachedEpisode>>.Success(allEpisodes);
    }

    /// <summary>Writes <paramref name="anime"/> through <see cref="EntityMerge"/> and returns the stored row.</summary>
    private async Task<CachedAnime> UpsertAnimeAsync(CachedAnime anime, AnimeDto dto, CancellationToken ct)
    {
        CachedAnime stored = anime;
        await WriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            var existing = await db.Anime.FindAsync([anime.Id], ct).ConfigureAwait(false);
            var fillOnly = false;
            if (existing is null)
            {
                db.Anime.Add(anime);
            }
            else
            {
                fillOnly = EntityMerge.FillOnly(existing.DataVersion, anime.DataVersion);
                EntityMerge.Apply(db.Entry(existing), anime, fillOnly);
                stored = existing;
            }

            // The fallback source's genre names differ from MAL's; they never replace MAL's links.
            if (!fillOnly)
                await UpsertGenresAsync(db, [(anime.Id, MediaMapper.ExtractAnimeGenres(dto))], "anime", ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }

        // Fetch streaming info outside WriteGate so background tasks never block or re-enter.
        QueueBackgroundRefresh("streaming", anime.Id,
            bg => FetchAndCacheStreamingAsync(anime.Id, bg));
        return stored;
    }

    /// <summary>Writes <paramref name="manga"/> through <see cref="EntityMerge"/> and returns the stored row.</summary>
    private async Task<CachedManga> UpsertMangaAsync(CachedManga manga, MangaDto dto, CancellationToken ct)
    {
        CachedManga stored = manga;
        await WriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            var existing = await db.Manga.FindAsync([manga.Id], ct).ConfigureAwait(false);
            var fillOnly = false;
            if (existing is null)
            {
                db.Manga.Add(manga);
            }
            else
            {
                fillOnly = EntityMerge.FillOnly(existing.DataVersion, manga.DataVersion);
                EntityMerge.Apply(db.Entry(existing), manga, fillOnly);
                stored = existing;
            }

            if (!fillOnly)
                await UpsertGenresAsync(db, [(manga.Id, MediaMapper.ExtractMangaGenres(dto))], "manga", ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }

        return stored;
    }

    /// <summary>
    /// Batched genre upsert for one or more media items: one query for existing
    /// genres and a diff against existing links, instead of two round-trips per item.
    /// </summary>
    private static async Task UpsertGenresAsync(
        CatalogDbContext db,
        IReadOnlyList<(int MediaId, IReadOnlyList<CachedGenre> Genres)> items,
        string mediaType,
        CancellationToken ct)
    {
        await ResolveGenreIdsByNameAsync(db, items, ct).ConfigureAwait(false);

        // Items with no genres keep their existing links (same as the old per-item
        // early return).
        var withGenres = items
            .Select(i => (i.MediaId, Genres: i.Genres
                .Where(g => g.Id > 0 && !string.IsNullOrWhiteSpace(g.Name))
                .DistinctBy(g => g.Id)
                .ToList()))
            .Where(i => i.Genres.Count > 0)
            .ToList();
        if (withGenres.Count == 0) return;

        var genreIds = withGenres.SelectMany(i => i.Genres.Select(g => g.Id)).Distinct().ToList();
        var existingGenreIds = (await db.Genres
            .Where(g => genreIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();

        // Include genres already staged in this unit of work but not yet saved to DB.
        // Without this, a genre shared by multiple anime in the same batch (e.g. "Action")
        // would be Add()ed twice → EF "already tracked" InvalidOperationException.
        foreach (var entry in db.ChangeTracker.Entries<CachedGenre>()
            .Where(e => e.State == EntityState.Added))
            existingGenreIds.Add(entry.Entity.Id);

        foreach (var genre in withGenres.SelectMany(i => i.Genres))
            if (existingGenreIds.Add(genre.Id))
                db.Genres.Add(genre);

        var mediaIds = withGenres.Select(i => i.MediaId).ToList();
        var existingLinks = await db.MediaGenres
            .Where(mg => mediaIds.Contains(mg.MediaId) && mg.MediaType == mediaType)
            .ToListAsync(ct).ConfigureAwait(false);

        var desiredPairs = withGenres
            .SelectMany(i => i.Genres.Select(g => (i.MediaId, GenreId: g.Id)))
            .ToHashSet();

        foreach (var link in existingLinks)
        {
            if (!desiredPairs.Remove((link.MediaId, link.GenreId)))
                db.MediaGenres.Remove(link);
        }

        var stagedPairs = db.ChangeTracker.Entries<CachedMediaGenre>()
            .Where(e => e.State == EntityState.Added && e.Entity.MediaType == mediaType)
            .Select(e => (e.Entity.MediaId, e.Entity.GenreId))
            .ToHashSet();

        foreach (var (mediaId, genreId) in desiredPairs)
        {
            if (stagedPairs.Add((mediaId, genreId)))
            {
                db.MediaGenres.Add(new CachedMediaGenre
                {
                    MediaId = mediaId,
                    MediaType = mediaType,
                    GenreId = genreId
                });
            }
        }
    }

    /// <summary>
    /// First id handed out to a genre that arrived without one. MyAnimeList's genre ids are
    /// small two- and three-digit numbers, so this range can never collide with them.
    /// </summary>
    private const int GeneratedGenreIdBase = 10000;

    /// <summary>
    /// Gives an id to genres that arrived with <c>Id == 0</c>, which is how a source without
    /// MyAnimeList genre ids (AniList) reports them. An existing genre of the same name keeps
    /// its id so "Action" stays a single row whichever source supplied it — otherwise the
    /// Stats page would show the same genre twice.
    /// </summary>
    /// <remarks>
    /// Matching is by name only, not by media type: the table's key is the id alone, so a
    /// genre row is already shared between anime and manga.
    /// </remarks>
    private static async Task ResolveGenreIdsByNameAsync(
        CatalogDbContext db,
        IReadOnlyList<(int MediaId, IReadOnlyList<CachedGenre> Genres)> items,
        CancellationToken ct)
    {
        var unresolved = items
            .SelectMany(i => i.Genres)
            .Where(g => g.Id == 0 && !string.IsNullOrWhiteSpace(g.Name))
            .ToList();
        if (unresolved.Count == 0) return;

        // Load the full genre dictionary (a few dozen rows at most) so matching is truly
        // case-insensitive even on SQLite's default case-sensitive TEXT collation.
        var allStored = await db.Genres.AsNoTracking()
            .Select(g => new { g.Id, g.Name })
            .ToListAsync(ct).ConfigureAwait(false);

        var byName = allStored
            .GroupBy(g => g.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Genres added earlier in this same unit of work are not queryable yet.
        var staged = db.ChangeTracker.Entries<CachedGenre>()
            .Where(e => e.State == EntityState.Added && e.Entity.Id != 0)
            .Select(e => e.Entity)
            .ToList();
        foreach (var genre in staged)
            byName.TryAdd(genre.Name.Trim(), genre.Id);

        // Continue after the highest generated id, counting both saved and staged rows.
        var maxStored = allStored
            .Where(g => g.Id >= GeneratedGenreIdBase)
            .Select(g => g.Id)
            .DefaultIfEmpty(0)
            .Max();

        var maxStaged = staged
            .Where(g => g.Id >= GeneratedGenreIdBase)
            .Select(g => g.Id)
            .DefaultIfEmpty(0)
            .Max();

        var nextId = Math.Max(GeneratedGenreIdBase, Math.Max(maxStored, maxStaged) + 1);

        foreach (var genre in unresolved)
        {
            var trimmed = genre.Name.Trim();
            genre.Name = trimmed;
            if (byName.TryGetValue(trimmed, out var existingId))
            {
                genre.Id = existingId;
                continue;
            }

            genre.Id = nextId++;
            byName[trimmed] = genre.Id;
        }
    }

    private async Task<List<CachedAnime>> UpsertAnimeListAsync(
        IReadOnlyList<AnimeDto> dtos, CancellationToken ct)
    {
        // seasons/now can return duplicate MAL IDs — filter invalid IDs and deduplicate before tracking.
        var uniqueDtos = dtos
            .Where(d => d.MalId > 0)
            .GroupBy(d => d.MalId)
            .Select(g => g.First())
            .ToList();
        var mapped = uniqueDtos.Select(MediaMapper.ToAnime).ToList();
        if (mapped.Count == 0)
            return mapped;

        // Mapping is complete above; caching below is best-effort so a database
        // failure cannot discard results the API already returned successfully.
        await TryCacheAsync("anime list", async () =>
        {
            await WriteGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

                var ids = mapped.Select(a => a.Id).ToList();
                var existingMap = await db.Anime
                    .Where(a => ids.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);

                var genreWrites = new List<(int, IReadOnlyList<CachedGenre>)>();
                for (var i = 0; i < mapped.Count; i++)
                {
                    var anime = mapped[i];
                    var fillOnly = false;
                    if (existingMap.TryGetValue(anime.Id, out var existing))
                    {
                        // List entries are often thinner than the full record: merge, never blank out.
                        fillOnly = EntityMerge.FillOnly(existing.DataVersion, anime.DataVersion);
                        EntityMerge.Apply(db.Entry(existing), anime, fillOnly);
                        mapped[i] = existing;
                    }
                    else
                    {
                        db.Anime.Add(anime);
                    }

                    if (!fillOnly)
                        genreWrites.Add((anime.Id, MediaMapper.ExtractAnimeGenres(uniqueDtos[i])));
                }

                await UpsertGenresAsync(db, genreWrites, "anime", ct).ConfigureAwait(false);

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                WriteGate.Release();
            }
        }).ConfigureAwait(false);

        return mapped;
    }

    private async Task<List<CachedManga>> UpsertMangaListAsync(
        IReadOnlyList<MangaDto> dtos, CancellationToken ct)
    {
        var uniqueDtos = dtos
            .Where(d => d.MalId > 0)
            .GroupBy(d => d.MalId)
            .Select(g => g.First())
            .ToList();
        var mapped = uniqueDtos.Select(MediaMapper.ToManga).ToList();
        if (mapped.Count == 0)
            return mapped;

        await TryCacheAsync("manga list", async () =>
        {
            await WriteGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

                var ids = mapped.Select(m => m.Id).ToList();
                var existingMap = await db.Manga
                    .Where(m => ids.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);

                var genreWrites = new List<(int, IReadOnlyList<CachedGenre>)>();
                for (var i = 0; i < mapped.Count; i++)
                {
                    var manga = mapped[i];
                    var fillOnly = false;
                    if (existingMap.TryGetValue(manga.Id, out var existing))
                    {
                        fillOnly = EntityMerge.FillOnly(existing.DataVersion, manga.DataVersion);
                        EntityMerge.Apply(db.Entry(existing), manga, fillOnly);
                        mapped[i] = existing;
                    }
                    else
                    {
                        db.Manga.Add(manga);
                    }

                    if (!fillOnly)
                        genreWrites.Add((manga.Id, MediaMapper.ExtractMangaGenres(uniqueDtos[i])));
                }

                await UpsertGenresAsync(db, genreWrites, "manga", ct).ConfigureAwait(false);

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                WriteGate.Release();
            }
        }).ConfigureAwait(false);

        return mapped;
    }

    private async Task<Result<IReadOnlyList<CachedAnimeStreaming>>> FetchAndCacheStreamingAsync(
        int malId, CancellationToken ct)
    {
        var result = await sourceClient.GetAnimeStreamingAsync(malId, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
            return Result<IReadOnlyList<CachedAnimeStreaming>>.Failure(result.Error ?? "Failed to load streaming info.", result.Kind);

        var data = result.Value?.Data ?? [];
        var streaming = data
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => MediaMapper.ToStreaming(s, malId))
            .DistinctBy(s => s.PlatformName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (streaming.Count == 0)
        {
            NegativeStreamingCache[malId] = DateTime.UtcNow;
        }
        else
        {
            NegativeStreamingCache.TryRemove(malId, out _);
        }

        await TryCacheAsync($"streaming for anime {malId}", async () =>
        {
            // Ensure parent anime exists BEFORE taking WriteGate so GetAnimeAsync -> UpsertAnimeAsync
            // never deadlocks on WriteGate.
            bool hasParent;
            await using (var checkDb = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                hasParent = await checkDb.Anime.AnyAsync(a => a.Id == malId, ct).ConfigureAwait(false);
            }

            if (!hasParent)
            {
                var parentResult = await GetAnimeAsync(malId, force: false, ct).ConfigureAwait(false);
                if (!parentResult.IsSuccess)
                {
                    logger.LogDebug("Skipping streaming cache write for anime {Id}: parent anime not in catalog", malId);
                    return;
                }
            }

            await WriteGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                if (!await db.Anime.AnyAsync(a => a.Id == malId, ct).ConfigureAwait(false))
                {
                    logger.LogDebug("Skipping streaming cache write for anime {Id}: parent anime not in catalog", malId);
                    return;
                }

                var existing = db.AnimeStreaming.Where(s => s.AnimeId == malId);
                db.AnimeStreaming.RemoveRange(existing);
                db.AnimeStreaming.AddRange(streaming);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                WriteGate.Release();
            }
        }).ConfigureAwait(false);

        return Result<IReadOnlyList<CachedAnimeStreaming>>.Success(streaming);
    }
}
