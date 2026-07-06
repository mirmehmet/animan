using System.Collections.Concurrent;
using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Jikan;
using AniMan.Infrastructure.Jikan.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

public sealed class CatalogService(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IJikanClient jikanClient,
    ISettingsService settingsService,
    ILogger<CatalogService> logger) : ICatalogService
{
    public async Task<Result<IReadOnlyList<CachedAnime>>> SearchAnimeAsync(
        string query, CancellationToken ct = default)
    {
        try
        {
            var apiResult = await jikanClient.SearchAnimeAsync(query, ct: ct).ConfigureAwait(false);
            if (!apiResult.IsSuccess)
                return Result<IReadOnlyList<CachedAnime>>.Failure(apiResult.Error!);

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
            var apiResult = await jikanClient.SearchMangaAsync(query, ct: ct).ConfigureAwait(false);
            if (!apiResult.IsSuccess)
                return Result<IReadOnlyList<CachedManga>>.Failure(apiResult.Error!);

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

    public async Task<Result<CachedAnime>> GetAnimeAsync(int malId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var cached = await db.Anime.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == malId, ct).ConfigureAwait(false);

            if (cached is not null)
            {
                if (!await IsStaleAsync(cached.FetchedAt, ct).ConfigureAwait(false))
                    return Result<CachedAnime>.Success(cached);

                QueueBackgroundRefresh("anime", malId,
                    () => FetchAndCacheAnimeAsync(malId, CancellationToken.None));
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

    public async Task<Result<CachedManga>> GetMangaAsync(int malId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var cached = await db.Manga.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == malId, ct).ConfigureAwait(false);

            if (cached is not null)
            {
                if (!await IsStaleAsync(cached.FetchedAt, ct).ConfigureAwait(false))
                    return Result<CachedManga>.Success(cached);

                QueueBackgroundRefresh("manga", malId,
                    () => FetchAndCacheMangaAsync(malId, CancellationToken.None));
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
        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var anime = await db.Anime.AsNoTracking().FirstOrDefaultAsync(a => a.Id == malId, ct).ConfigureAwait(false);
            if (anime is null)
                return Result<IReadOnlyList<CachedEpisode>>.Failure($"Anime {malId} not in catalog");

            var cached = await db.Episodes.AsNoTracking()
                .Where(e => e.AnimeId == malId)
                .OrderBy(e => e.EpisodeNumber)
                .ToListAsync(ct).ConfigureAwait(false);

            if (cached.Count > 0 && !await IsStaleAsync(anime.FetchedAt, ct).ConfigureAwait(false))
                return Result<IReadOnlyList<CachedEpisode>>.Success(cached);

            return await FetchAndCacheEpisodesAsync(malId, anime.TotalEpisodes, ct).ConfigureAwait(false);
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
            var result = await jikanClient.GetCurrentSeasonAsync(ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result<IReadOnlyList<CachedAnime>>.Failure(result.Error!);

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

    public async Task<Result<IReadOnlyList<CachedAnime>>> GetTopAnimeAsync(
        int page = 1, CancellationToken ct = default)
    {
        try
        {
            var result = await jikanClient.GetTopAnimeAsync(page, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result<IReadOnlyList<CachedAnime>>.Failure(result.Error!);

            var anime = await UpsertAnimeListAsync(result.Value!.Data ?? [], ct).ConfigureAwait(false);
            return Result<IReadOnlyList<CachedAnime>>.Success(anime);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTopAnime page {Page} failed", page);
            return Result<IReadOnlyList<CachedAnime>>.Failure("Failed to load top anime.");
        }
    }

    public async Task<Result<IReadOnlyList<CachedManga>>> GetTopMangaAsync(
        int page = 1, CancellationToken ct = default)
    {
        try
        {
            var result = await jikanClient.GetTopMangaAsync(page, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return Result<IReadOnlyList<CachedManga>>.Failure(result.Error!);

            var manga = await UpsertMangaListAsync(result.Value!.Data ?? [], ct).ConfigureAwait(false);
            return Result<IReadOnlyList<CachedManga>>.Success(manga);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTopManga page {Page} failed", page);
            return Result<IReadOnlyList<CachedManga>>.Failure("Failed to load top manga.");
        }
    }

    public async Task<Result<IReadOnlyList<CachedAnimeStreaming>>> GetAnimeStreamingAsync(
        int malId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var rows = await db.AnimeStreaming.AsNoTracking()
                .Where(s => s.AnimeId == malId).ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<CachedAnimeStreaming>>.Success(rows);
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

    private int? _cacheRefreshDays;

    // The service is registered transient — the in-flight set must be static to
    // deduplicate refreshes across instances (e.g. rapid re-navigation).
    private static readonly ConcurrentDictionary<(string Kind, int MalId), byte> InFlightRefreshes = new();

    /// <summary>
    /// Runs a fire-and-forget cache refresh at most once per (kind, id) at a time,
    /// observing (and logging) any failure so nothing reaches the unobserved-task pool.
    /// </summary>
    private void QueueBackgroundRefresh(string kind, int malId, Func<Task> refresh)
    {
        if (!InFlightRefreshes.TryAdd((kind, malId), 0))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await refresh().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background {Kind} refresh failed for {Id}", kind, malId);
            }
            finally
            {
                InFlightRefreshes.TryRemove((kind, malId), out _);
            }
        });
    }

    private async Task<bool> IsStaleAsync(DateTime fetchedAt, CancellationToken ct)
    {
        _cacheRefreshDays ??= await settingsService.GetCacheRefreshDaysAsync(ct).ConfigureAwait(false);
        return fetchedAt.AddDays(_cacheRefreshDays.Value) < DateTime.UtcNow;
    }

    private async Task<Result<CachedAnime>> FetchAndCacheAnimeAsync(
        int malId, CancellationToken ct)
    {
        var apiResult = await jikanClient.GetAnimeFullAsync(malId, ct).ConfigureAwait(false);
        if (!apiResult.IsSuccess || apiResult.Value?.Data is null)
            return Result<CachedAnime>.Failure(apiResult.Error ?? "No data from Jikan");

        var anime = JikanMapper.ToAnime(apiResult.Value.Data);
        await UpsertAnimeAsync(anime, apiResult.Value.Data, ct).ConfigureAwait(false);
        return Result<CachedAnime>.Success(anime);
    }

    private async Task<Result<CachedManga>> FetchAndCacheMangaAsync(
        int malId, CancellationToken ct)
    {
        var apiResult = await jikanClient.GetMangaFullAsync(malId, ct).ConfigureAwait(false);
        if (!apiResult.IsSuccess || apiResult.Value?.Data is null)
            return Result<CachedManga>.Failure(apiResult.Error ?? "No data from Jikan");

        var manga = JikanMapper.ToManga(apiResult.Value.Data);
        await UpsertMangaAsync(manga, apiResult.Value.Data, ct).ConfigureAwait(false);
        return Result<CachedManga>.Success(manga);
    }

    private async Task<Result<IReadOnlyList<CachedEpisode>>> FetchAndCacheEpisodesAsync(
        int malId, int? totalEpisodes, CancellationToken ct)
    {
        var allEpisodes = new List<CachedEpisode>();
        int page = 1;

        while (true)
        {
            var result = await jikanClient.GetAnimeEpisodesAsync(malId, page, ct).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value?.Data is null) break;

            var episodes = result.Value.Data.Select(e => JikanMapper.ToEpisode(e, malId));
            allEpisodes.AddRange(episodes);

            if (result.Value.Pagination?.HasNextPage != true) break;
            page++;
        }

        // Placeholder fallback: Jikan has no episodes listed but we know the total
        if (allEpisodes.Count == 0 && totalEpisodes > 0)
        {
            for (int i = 1; i <= totalEpisodes; i++)
                allEpisodes.Add(JikanMapper.ToPlaceholderEpisode(malId, i));
        }

        await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = db.Episodes.Where(e => e.AnimeId == malId);
        db.Episodes.RemoveRange(existing);
        db.Episodes.AddRange(allEpisodes);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogInformation("Cached {Count} episodes for anime {Id}", allEpisodes.Count, malId);
        return Result<IReadOnlyList<CachedEpisode>>.Success(allEpisodes);
    }

    private async Task UpsertAnimeAsync(CachedAnime anime, JikanAnimeDto dto, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Anime.FindAsync([anime.Id], ct).ConfigureAwait(false);
        if (existing is null)
            db.Anime.Add(anime);
        else
            db.Entry(existing).CurrentValues.SetValues(anime);

        await UpsertGenresAsync(db, [(anime.Id, JikanMapper.ExtractAnimeGenres(dto))], "anime", ct).ConfigureAwait(false);

        // Refresh streaming
        var existingStreaming = db.AnimeStreaming.Where(s => s.AnimeId == anime.Id);
        db.AnimeStreaming.RemoveRange(existingStreaming);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Fetch streaming info
        QueueBackgroundRefresh("streaming", anime.Id,
            () => FetchAndCacheStreamingAsync(anime.Id, CancellationToken.None));
    }

    private async Task UpsertMangaAsync(CachedManga manga, JikanMangaDto dto, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Manga.FindAsync([manga.Id], ct).ConfigureAwait(false);
        if (existing is null)
            db.Manga.Add(manga);
        else
            db.Entry(existing).CurrentValues.SetValues(manga);

        await UpsertGenresAsync(db, [(manga.Id, JikanMapper.ExtractMangaGenres(dto))], "manga", ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Batched genre upsert for one or more media items: one query for existing
    /// genres and one for stale links, instead of two round-trips per item.
    /// </summary>
    private static async Task UpsertGenresAsync(
        CatalogDbContext db,
        IReadOnlyList<(int MediaId, IReadOnlyList<CachedGenre> Genres)> items,
        string mediaType,
        CancellationToken ct)
    {
        // Items with no genres keep their existing links (same as the old per-item
        // early return).
        var withGenres = items
            .Select(i => (i.MediaId, Genres: i.Genres.DistinctBy(g => g.Id).ToList()))
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
        var toDelete = await db.MediaGenres
            .Where(mg => mediaIds.Contains(mg.MediaId) && mg.MediaType == mediaType)
            .ToListAsync(ct).ConfigureAwait(false);
        db.MediaGenres.RemoveRange(toDelete);

        foreach (var (mediaId, genres) in withGenres)
            foreach (var genre in genres)
                db.MediaGenres.Add(new CachedMediaGenre
                {
                    MediaId = mediaId,
                    MediaType = mediaType,
                    GenreId = genre.Id
                });
    }

    private async Task<List<CachedAnime>> UpsertAnimeListAsync(
        IReadOnlyList<JikanAnimeDto> dtos, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Jikan seasons/now can return duplicate MAL IDs — deduplicate before tracking.
        var uniqueDtos = dtos.GroupBy(d => d.MalId).Select(g => g.First()).ToList();
        var mapped = uniqueDtos.Select(JikanMapper.ToAnime).ToList();

        var ids = mapped.Select(a => a.Id).ToList();
        var existingMap = await db.Anime
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);

        foreach (var anime in mapped)
        {
            if (existingMap.TryGetValue(anime.Id, out var existing))
                db.Entry(existing).CurrentValues.SetValues(anime);
            else
                db.Anime.Add(anime);
        }

        await UpsertGenresAsync(db,
            mapped.Zip(uniqueDtos, (a, d) => (a.Id, JikanMapper.ExtractAnimeGenres(d))).ToList(),
            "anime", ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return mapped;
    }

    private async Task<List<CachedManga>> UpsertMangaListAsync(
        IReadOnlyList<JikanMangaDto> dtos, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var uniqueDtos = dtos.GroupBy(d => d.MalId).Select(g => g.First()).ToList();
        var mapped = uniqueDtos.Select(JikanMapper.ToManga).ToList();

        var ids = mapped.Select(m => m.Id).ToList();
        var existingMap = await db.Manga
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);

        foreach (var manga in mapped)
        {
            if (existingMap.TryGetValue(manga.Id, out var existing))
                db.Entry(existing).CurrentValues.SetValues(manga);
            else
                db.Manga.Add(manga);
        }

        await UpsertGenresAsync(db,
            mapped.Zip(uniqueDtos, (m, d) => (m.Id, JikanMapper.ExtractMangaGenres(d))).ToList(),
            "manga", ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return mapped;
    }

    private async Task FetchAndCacheStreamingAsync(int malId, CancellationToken ct)
    {
        var result = await jikanClient.GetAnimeStreamingAsync(malId, ct).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value?.Data is null) return;

        await using var db = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var streaming = result.Value.Data.Select(s => JikanMapper.ToStreaming(s, malId)).ToList();
        db.AnimeStreaming.AddRange(streaming);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
