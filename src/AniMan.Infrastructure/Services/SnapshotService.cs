using System.Text.Json;
using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

public sealed class SnapshotService(
    IDbContextFactory<LibraryDbContext> libraryFactory,
    IDbContextFactory<CatalogDbContext> catalogFactory,
    ICatalogService catalogService,
    CoverStore coverStore,
    ILogger<SnapshotService> logger) : ISnapshotService
{
    public async Task<Result<LibraryItem>> SnapshotAsync(
        int malId, MediaType mediaType, int statusId, CancellationToken ct = default)
    {
        // Guard: duplicate check
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var existing = await db.LibraryItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.MalId == malId && i.MediaType == mediaType, ct);
        if (existing is not null)
            return Result<LibraryItem>.Failure($"{mediaType} {malId} is already in your library");

        // Fetch metadata
        MediaSnapshot snapshot;
        string? coverUrl;

        if (mediaType == MediaType.Anime)
        {
            var result = await catalogService.GetAnimeAsync(malId, ct);
            if (!result.IsSuccess)
                return Result<LibraryItem>.Failure(result.Error!);
            snapshot = MapAnimeSnapshot(result.Value!);
            coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
        }
        else
        {
            var result = await catalogService.GetMangaAsync(malId, ct);
            if (!result.IsSuccess)
                return Result<LibraryItem>.Failure(result.Error!);
            snapshot = MapMangaSnapshot(result.Value!);
            coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
        }

        // Load genres from catalog
        snapshot.Genres = await LoadGenresJsonAsync(malId, mediaType == MediaType.Anime ? "anime" : "manga");

        // Download cover
        snapshot.CoverLocalPath = await coverStore.DownloadAsync(malId, mediaType, coverUrl, ct);
        snapshot.CoverOriginalUrl = coverUrl;

        snapshot.SnapshotAt = DateTime.UtcNow;

        var item = new LibraryItem
        {
            MalId = malId,
            MediaType = mediaType,
            StatusId = statusId,
            AddedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Snapshot = snapshot
        };

        // Single SaveChangesAsync → EF wraps both inserts in an implicit transaction.
        // No orphaned LibraryItem if the snapshot fails to save.
        db.LibraryItems.Add(item);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Snapshot created for {Type} {MalId} → LibraryItem {Id}", mediaType, malId, item.Id);
        return Result<LibraryItem>.Success(item);
    }

    public async Task<Result<MediaSnapshot>> ReSnapshotAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.AsNoTracking()
            .Include(i => i.Snapshot)
            .FirstOrDefaultAsync(i => i.Id == libraryItemId, ct);

        if (item is null)
            return Result<MediaSnapshot>.Failure($"LibraryItem {libraryItemId} not found");

        MediaSnapshot fresh;
        string? coverUrl;

        if (item.MediaType == MediaType.Anime)
        {
            var result = await catalogService.GetAnimeAsync(item.MalId, ct);
            if (!result.IsSuccess) return Result<MediaSnapshot>.Failure(result.Error!);
            fresh = MapAnimeSnapshot(result.Value!);
            coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
        }
        else
        {
            var result = await catalogService.GetMangaAsync(item.MalId, ct);
            if (!result.IsSuccess) return Result<MediaSnapshot>.Failure(result.Error!);
            fresh = MapMangaSnapshot(result.Value!);
            coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
        }

        fresh.Genres = await LoadGenresJsonAsync(item.MalId, item.MediaType == MediaType.Anime ? "anime" : "manga");
        fresh.CoverLocalPath = await coverStore.DownloadAsync(item.MalId, item.MediaType, coverUrl, ct);
        fresh.CoverOriginalUrl = coverUrl;
        fresh.LibraryItemId = libraryItemId;
        fresh.SnapshotAt = DateTime.UtcNow;

        var existing = await db.Snapshots.FindAsync([libraryItemId], ct);
        if (existing is null)
            db.Snapshots.Add(fresh);
        else
            db.Entry(existing).CurrentValues.SetValues(fresh);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Re-snapshot complete for LibraryItem {Id}", libraryItemId);
        return Result<MediaSnapshot>.Success(fresh);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static MediaSnapshot MapAnimeSnapshot(Core.Domain.Models.CachedAnime a) => new()
    {
        Title = a.Title,
        TitleJapanese = a.TitleJapanese,
        Synopsis = a.Synopsis,
        Type = a.Type,
        Status = a.Status,
        Studio = a.Studios,
        TotalEpisodes = a.TotalEpisodes,
        AiringStart = a.AiringStart,
        AiringEnd = a.AiringEnd,
        Season = a.Season,
        Year = a.Year,
        MalScore = a.Score,
        Genres = "[]"
    };

    private static MediaSnapshot MapMangaSnapshot(Core.Domain.Models.CachedManga m) => new()
    {
        Title = m.Title,
        TitleJapanese = m.TitleJapanese,
        Synopsis = m.Synopsis,
        Type = m.Type,
        Status = m.Status,
        Studio = m.Serializations,
        TotalChapters = m.TotalChapters,
        TotalVolumes = m.TotalVolumes,
        AiringStart = m.PublishedStart,
        AiringEnd = m.PublishedEnd,
        MalScore = m.Score,
        Genres = "[]"
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> LoadGenresJsonAsync(int malId, string mediaType)
    {
        try
        {
            await using var catalogDb = await catalogFactory.CreateDbContextAsync();
            var genres = await catalogDb.MediaGenres
                .Where(mg => mg.MediaId == malId && mg.MediaType == mediaType)
                .Join(catalogDb.Genres, mg => mg.GenreId, g => g.Id, (_, g) => g.Name)
                .ToListAsync();
            return JsonSerializer.Serialize(genres);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Genre load failed for {MalId}", malId);
            return "[]";
        }
    }
}
