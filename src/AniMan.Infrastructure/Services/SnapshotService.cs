using System.Text.Json;
using AniMan.Core.Common;
using AniMan.Core.Domain;
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
        if (malId <= 0)
            return Result<LibraryItem>.Failure("Invalid media ID.");

        try
        {
            // Guard: duplicate check (allow re-adding if the existing entry was soft-deleted to Trash)
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var existing = await db.LibraryItems
                .Include(i => i.Snapshot)
                .FirstOrDefaultAsync(i => i.MalId == malId && i.MediaType == mediaType, ct)
                .ConfigureAwait(false);
            if (existing is { DeletedAt: null })
                return Result<LibraryItem>.Failure($"{mediaType} {malId} is already in your library");

            // Fetch metadata BEFORE touching library.db so a network failure never destroys a trashed item.
            MediaSnapshot snapshot;
            string? coverUrl;

            if (mediaType == MediaType.Anime)
            {
                var result = await catalogService.GetAnimeAsync(malId, force: false, ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                    return Result<LibraryItem>.Failure(result.Error!);
                snapshot = MapAnimeSnapshot(result.Value!);
                coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
            }
            else
            {
                var result = await catalogService.GetMangaAsync(malId, force: false, ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                    return Result<LibraryItem>.Failure(result.Error!);
                snapshot = MapMangaSnapshot(result.Value!);
                coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
            }

            // Load genres from catalog
            snapshot.Genres = await LoadGenresJsonAsync(malId, mediaType == MediaType.Anime ? "anime" : "manga", ct).ConfigureAwait(false);

            // Download cover; fall back to any existing valid on-disk cover if offline
            var downloadedCover = await coverStore.DownloadAsync(malId, mediaType, coverUrl, ct).ConfigureAwait(false);
            snapshot.CoverLocalPath = downloadedCover ?? ResolveExistingCoverOnDisk(existing?.Snapshot?.CoverLocalPath, malId, mediaType);
            snapshot.CoverOriginalUrl = coverUrl;
            snapshot.SnapshotAt = DateTime.UtcNow;

            var today = DateOnly.FromDateTime(DateTime.Now);
            var isStartedStatus = statusId is TrackingStatusIds.Watching or TrackingStatusIds.Reading;
            var isCompletedStatus = statusId == TrackingStatusIds.Completed;

            // If the item was in Trash, restore it with refreshed metadata while preserving
            // all watched episodes, read chapters, ratings, and notes.
            if (existing is not null)
            {
                existing.DeletedAt = null;
                existing.StatusId = statusId;
                existing.UpdatedAt = DateTime.UtcNow;
                if (isStartedStatus) existing.StartedAt ??= today;
                if (isCompletedStatus) existing.CompletedAt ??= today;

                snapshot.LibraryItemId = existing.Id;
                if (existing.Snapshot is null)
                {
                    existing.Snapshot = snapshot;
                    db.Snapshots.Add(snapshot);
                }
                else
                {
                    db.Entry(existing.Snapshot).CurrentValues.SetValues(snapshot);
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                logger.LogInformation("Restored trashed {Type} {MalId} → LibraryItem {Id}", mediaType, malId, existing.Id);
                return Result<LibraryItem>.Success(existing);
            }

            var item = new LibraryItem
            {
                MalId = malId,
                MediaType = mediaType,
                StatusId = statusId,
                StartedAt = isStartedStatus ? today : null,
                CompletedAt = isCompletedStatus ? today : null,
                AddedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Snapshot = snapshot
            };

            // Single SaveChangesAsync → EF wraps both inserts in an implicit transaction.
            // No orphaned LibraryItem if the snapshot fails to save.
            db.LibraryItems.Add(item);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Snapshot created for {Type} {MalId} → LibraryItem {Id}", mediaType, malId, item.Id);
            return Result<LibraryItem>.Success(item);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Snapshot failed for {Type} {MalId}", mediaType, malId);
            return Result<LibraryItem>.Failure("Failed to add the item to your library.");
        }
    }

    public async Task<Result<MediaSnapshot>> ReSnapshotAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var item = await db.LibraryItems.AsNoTracking()
                .Include(i => i.Snapshot)
                .FirstOrDefaultAsync(i => i.Id == libraryItemId, ct).ConfigureAwait(false);

            if (item is null)
                return Result<MediaSnapshot>.Failure($"LibraryItem {libraryItemId} not found");

            MediaSnapshot fresh;
            string? coverUrl;

            if (item.MediaType == MediaType.Anime)
            {
                var result = await catalogService.GetAnimeAsync(item.MalId, force: true, ct).ConfigureAwait(false);
                if (!result.IsSuccess) return Result<MediaSnapshot>.Failure(result.Error!);
                fresh = MapAnimeSnapshot(result.Value!);
                coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
            }
            else
            {
                var result = await catalogService.GetMangaAsync(item.MalId, force: true, ct).ConfigureAwait(false);
                if (!result.IsSuccess) return Result<MediaSnapshot>.Failure(result.Error!);
                fresh = MapMangaSnapshot(result.Value!);
                coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
            }

            fresh.Genres = await LoadGenresJsonAsync(item.MalId, item.MediaType == MediaType.Anime ? "anime" : "manga", ct).ConfigureAwait(false);
            var downloadedCover = await coverStore.DownloadAsync(item.MalId, item.MediaType, coverUrl, ct).ConfigureAwait(false);
            fresh.CoverLocalPath = downloadedCover ?? ResolveExistingCoverOnDisk(item.Snapshot?.CoverLocalPath, item.MalId, item.MediaType);
            fresh.CoverOriginalUrl = coverUrl ?? item.Snapshot?.CoverOriginalUrl;
            fresh.StreamingPlatforms = item.Snapshot?.StreamingPlatforms;
            fresh.LibraryItemId = libraryItemId;
            fresh.SnapshotAt = DateTime.UtcNow;

            var existing = await db.Snapshots.FindAsync([libraryItemId], ct).ConfigureAwait(false);
            if (existing is null)
                db.Snapshots.Add(fresh);
            else
                db.Entry(existing).CurrentValues.SetValues(fresh);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Re-snapshot complete for LibraryItem {Id}", libraryItemId);
            return Result<MediaSnapshot>.Success(fresh);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReSnapshot failed for LibraryItem {Id}", libraryItemId);
            return Result<MediaSnapshot>.Failure("Failed to refresh item data.");
        }
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

    private string? ResolveExistingCoverOnDisk(string? previousPath, int malId, MediaType mediaType)
    {
        if (!string.IsNullOrWhiteSpace(previousPath) &&
            new FileInfo(previousPath) is { Exists: true, Length: > 0 })
        {
            return previousPath;
        }

        return coverStore.GetExistingLocalPath(malId, mediaType);
    }

    private async Task<string> LoadGenresJsonAsync(int malId, string mediaType, CancellationToken ct)
    {
        try
        {
            await using var catalogDb = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var genres = await catalogDb.MediaGenres
                .Where(mg => mg.MediaId == malId && mg.MediaType == mediaType)
                .Join(catalogDb.Genres, mg => mg.GenreId, g => g.Id, (_, g) => g.Name)
                .ToListAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(genres);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Genre load failed for {MalId}", malId);
            return "[]";
        }
    }
}
