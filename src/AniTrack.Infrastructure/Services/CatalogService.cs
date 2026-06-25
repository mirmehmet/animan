using AniTrack.Core.Common;
using AniTrack.Core.Domain.Models;
using AniTrack.Core.Interfaces;
using AniTrack.Infrastructure.Data;
using AniTrack.Infrastructure.Jikan;
using AniTrack.Infrastructure.Jikan.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniTrack.Infrastructure.Services;

public sealed class CatalogService(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IJikanClient jikanClient,
    ISettingsService settingsService,
    ILogger<CatalogService> logger) : ICatalogService
{
    public async Task<Result<IReadOnlyList<CachedAnime>>> SearchAnimeAsync(
        string query, CancellationToken ct = default)
    {
        var apiResult = await jikanClient.SearchAnimeAsync(query, ct: ct);
        if (!apiResult.IsSuccess)
            return Result<IReadOnlyList<CachedAnime>>.Failure(apiResult.Error!);

        var items = apiResult.Value!.Data ?? [];
        var anime = items.Select(JikanMapper.ToAnime).ToList();

        await UpsertAnimeListAsync(anime, ct);
        return Result<IReadOnlyList<CachedAnime>>.Success(anime);
    }

    public async Task<Result<IReadOnlyList<CachedManga>>> SearchMangaAsync(
        string query, CancellationToken ct = default)
    {
        var apiResult = await jikanClient.SearchMangaAsync(query, ct: ct);
        if (!apiResult.IsSuccess)
            return Result<IReadOnlyList<CachedManga>>.Failure(apiResult.Error!);

        var items = apiResult.Value!.Data ?? [];
        var manga = items.Select(JikanMapper.ToManga).ToList();

        await UpsertMangaListAsync(manga, ct);
        return Result<IReadOnlyList<CachedManga>>.Success(manga);
    }

    public async Task<Result<CachedAnime>> GetAnimeAsync(int malId, CancellationToken ct = default)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        var cached = await db.Anime.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == malId, ct);

        if (cached is not null)
        {
            if (!await IsStaleAsync(cached.FetchedAt))
                return Result<CachedAnime>.Success(cached);

            // Stale — return immediately, refresh in background
            _ = Task.Run(() => RefreshAnimeAsync(malId), CancellationToken.None);
            return Result<CachedAnime>.Success(cached);
        }

        return await FetchAndCacheAnimeAsync(malId, ct);
    }

    public async Task<Result<CachedManga>> GetMangaAsync(int malId, CancellationToken ct = default)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        var cached = await db.Manga.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == malId, ct);

        if (cached is not null)
        {
            if (!await IsStaleAsync(cached.FetchedAt))
                return Result<CachedManga>.Success(cached);

            _ = Task.Run(() => RefreshMangaAsync(malId), CancellationToken.None);
            return Result<CachedManga>.Success(cached);
        }

        return await FetchAndCacheMangaAsync(malId, ct);
    }

    public async Task<Result<IReadOnlyList<CachedEpisode>>> GetAnimeEpisodesAsync(
        int malId, CancellationToken ct = default)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        var anime = await db.Anime.AsNoTracking().FirstOrDefaultAsync(a => a.Id == malId, ct);
        if (anime is null)
            return Result<IReadOnlyList<CachedEpisode>>.Failure($"Anime {malId} not in catalog");

        var cached = await db.Episodes.AsNoTracking()
            .Where(e => e.AnimeId == malId)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync(ct);

        if (cached.Count > 0 && !await IsStaleAsync(anime.FetchedAt))
            return Result<IReadOnlyList<CachedEpisode>>.Success(cached);

        return await FetchAndCacheEpisodesAsync(malId, anime.TotalEpisodes, ct);
    }

    public async Task<Result<IReadOnlyList<CachedAnime>>> GetCurrentSeasonAsync(
        CancellationToken ct = default)
    {
        var result = await jikanClient.GetCurrentSeasonAsync(ct);
        if (!result.IsSuccess)
            return Result<IReadOnlyList<CachedAnime>>.Failure(result.Error!);

        var anime = (result.Value!.Data ?? []).Select(JikanMapper.ToAnime).ToList();
        await UpsertAnimeListAsync(anime, ct);
        return Result<IReadOnlyList<CachedAnime>>.Success(anime);
    }

    public async Task<Result<IReadOnlyList<CachedAnime>>> GetUpcomingSeasonAsync(
        CancellationToken ct = default)
    {
        var result = await jikanClient.GetUpcomingSeasonAsync(ct);
        if (!result.IsSuccess)
            return Result<IReadOnlyList<CachedAnime>>.Failure(result.Error!);

        var anime = (result.Value!.Data ?? []).Select(JikanMapper.ToAnime).ToList();
        await UpsertAnimeListAsync(anime, ct);
        return Result<IReadOnlyList<CachedAnime>>.Success(anime);
    }

    public async Task<Result<IReadOnlyList<CachedAnime>>> GetTopAnimeAsync(
        CancellationToken ct = default)
    {
        var result = await jikanClient.GetTopAnimeAsync(ct: ct);
        if (!result.IsSuccess)
            return Result<IReadOnlyList<CachedAnime>>.Failure(result.Error!);

        var anime = (result.Value!.Data ?? []).Select(JikanMapper.ToAnime).ToList();
        await UpsertAnimeListAsync(anime, ct);
        return Result<IReadOnlyList<CachedAnime>>.Success(anime);
    }

    public async Task<Result<IReadOnlyList<CachedManga>>> GetTopMangaAsync(
        CancellationToken ct = default)
    {
        var result = await jikanClient.GetTopMangaAsync(ct: ct);
        if (!result.IsSuccess)
            return Result<IReadOnlyList<CachedManga>>.Failure(result.Error!);

        var manga = (result.Value!.Data ?? []).Select(JikanMapper.ToManga).ToList();
        await UpsertMangaListAsync(manga, ct);
        return Result<IReadOnlyList<CachedManga>>.Success(manga);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> IsStaleAsync(DateTime fetchedAt)
    {
        var days = await settingsService.GetCacheRefreshDaysAsync();
        return fetchedAt.AddDays(days) < DateTime.UtcNow;
    }

    private async Task<Result<CachedAnime>> FetchAndCacheAnimeAsync(
        int malId, CancellationToken ct)
    {
        var apiResult = await jikanClient.GetAnimeFullAsync(malId, ct);
        if (!apiResult.IsSuccess || apiResult.Value?.Data is null)
            return Result<CachedAnime>.Failure(apiResult.Error ?? "No data from Jikan");

        var anime = JikanMapper.ToAnime(apiResult.Value.Data);
        await UpsertAnimeAsync(anime, apiResult.Value.Data, ct);
        return Result<CachedAnime>.Success(anime);
    }

    private async Task<Result<CachedManga>> FetchAndCacheMangaAsync(
        int malId, CancellationToken ct)
    {
        var apiResult = await jikanClient.GetMangaFullAsync(malId, ct);
        if (!apiResult.IsSuccess || apiResult.Value?.Data is null)
            return Result<CachedManga>.Failure(apiResult.Error ?? "No data from Jikan");

        var manga = JikanMapper.ToManga(apiResult.Value.Data);
        await UpsertMangaAsync(manga, apiResult.Value.Data, ct);
        return Result<CachedManga>.Success(manga);
    }

    private async Task<Result<IReadOnlyList<CachedEpisode>>> FetchAndCacheEpisodesAsync(
        int malId, int? totalEpisodes, CancellationToken ct)
    {
        var allEpisodes = new List<CachedEpisode>();
        int page = 1;

        while (true)
        {
            var result = await jikanClient.GetAnimeEpisodesAsync(malId, page, ct);
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

        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        var existing = db.Episodes.Where(e => e.AnimeId == malId);
        db.Episodes.RemoveRange(existing);
        db.Episodes.AddRange(allEpisodes);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cached {Count} episodes for anime {Id}", allEpisodes.Count, malId);
        return Result<IReadOnlyList<CachedEpisode>>.Success(allEpisodes);
    }

    private async Task UpsertAnimeAsync(CachedAnime anime, JikanAnimeDto dto, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);

        var existing = await db.Anime.FindAsync([anime.Id], ct);
        if (existing is null)
            db.Anime.Add(anime);
        else
            db.Entry(existing).CurrentValues.SetValues(anime);

        await UpsertGenresAsync(db, JikanMapper.ExtractAnimeGenres(dto), anime.Id, "anime", ct);

        // Refresh streaming
        var existingStreaming = db.AnimeStreaming.Where(s => s.AnimeId == anime.Id);
        db.AnimeStreaming.RemoveRange(existingStreaming);

        await db.SaveChangesAsync(ct);

        // Fetch streaming info
        _ = Task.Run(() => FetchAndCacheStreamingAsync(anime.Id), CancellationToken.None);
    }

    private async Task UpsertMangaAsync(CachedManga manga, JikanMangaDto dto, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);

        var existing = await db.Manga.FindAsync([manga.Id], ct);
        if (existing is null)
            db.Manga.Add(manga);
        else
            db.Entry(existing).CurrentValues.SetValues(manga);

        await UpsertGenresAsync(db, JikanMapper.ExtractMangaGenres(dto), manga.Id, "manga", ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertGenresAsync(
        CatalogDbContext db,
        IReadOnlyList<CachedGenre> genres,
        int mediaId,
        string mediaType,
        CancellationToken ct)
    {
        foreach (var genre in genres)
        {
            var existingGenre = await db.Genres.FindAsync([genre.Id], ct);
            if (existingGenre is null)
                db.Genres.Add(genre);
        }

        // Remove old links for this media, re-add
        var oldLinks = db.MediaGenres.Where(mg => mg.MediaId == mediaId && mg.MediaType == mediaType);
        db.MediaGenres.RemoveRange(oldLinks);

        foreach (var genre in genres)
        {
            db.MediaGenres.Add(new CachedMediaGenre
            {
                MediaId = mediaId,
                MediaType = mediaType,
                GenreId = genre.Id
            });
        }
    }

    private async Task UpsertAnimeListAsync(IEnumerable<CachedAnime> animeList, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        foreach (var anime in animeList)
        {
            var existing = await db.Anime.FindAsync([anime.Id], ct);
            if (existing is null)
                db.Anime.Add(anime);
            else
                db.Entry(existing).CurrentValues.SetValues(anime);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertMangaListAsync(IEnumerable<CachedManga> mangaList, CancellationToken ct)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        foreach (var manga in mangaList)
        {
            var existing = await db.Manga.FindAsync([manga.Id], ct);
            if (existing is null)
                db.Manga.Add(manga);
            else
                db.Entry(existing).CurrentValues.SetValues(manga);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task FetchAndCacheStreamingAsync(int malId)
    {
        try
        {
            var result = await jikanClient.GetAnimeStreamingAsync(malId);
            if (!result.IsSuccess || result.Value?.Data is null) return;

            await using var db = await catalogFactory.CreateDbContextAsync();
            var streaming = result.Value.Data.Select(s => JikanMapper.ToStreaming(s, malId)).ToList();
            db.AnimeStreaming.AddRange(streaming);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background streaming fetch failed for anime {Id}", malId);
        }
    }

    private async Task RefreshAnimeAsync(int malId)
    {
        try
        {
            await FetchAndCacheAnimeAsync(malId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background refresh failed for anime {Id}", malId);
        }
    }

    private async Task RefreshMangaAsync(int malId)
    {
        try
        {
            await FetchAndCacheMangaAsync(malId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background refresh failed for manga {Id}", malId);
        }
    }
}
