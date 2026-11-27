using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
                    return Result<LibraryItem>.Failure(result.Error!, result.Kind);
                snapshot = MapAnimeSnapshot(result.Value!);
                coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
            }
            else
            {
                var result = await catalogService.GetMangaAsync(malId, force: false, ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                    return Result<LibraryItem>.Failure(result.Error!, result.Kind);
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
                    MergeIntoSnapshot(db.Entry(existing.Snapshot), snapshot);
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
                var result = await catalogService.RefreshAnimeDetailsAsync(item.MalId, ct).ConfigureAwait(false);
                if (!result.IsSuccess) return Result<MediaSnapshot>.Failure(result.Error!, result.Kind);
                fresh = MapAnimeSnapshot(result.Value!);
                coverUrl = result.Value!.CoverLargeUrl ?? result.Value.CoverMediumUrl;
            }
            else
            {
                var result = await catalogService.GetMangaAsync(item.MalId, force: true, ct).ConfigureAwait(false);
                if (!result.IsSuccess) return Result<MediaSnapshot>.Failure(result.Error!, result.Kind);
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
            {
                db.Snapshots.Add(fresh);
            }
            else
            {
                MergeIntoSnapshot(db.Entry(existing), fresh);
                fresh = existing;
            }

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

    public async Task<Result> FillExtendedMetadataAsync(int libraryItemId, CancellationToken ct = default)
    {
        try
        {
            // Read what to fetch, then release the context before going to the network.
            int malId;
            MediaType mediaType;
            await using (var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                var target = await db.Snapshots.AsNoTracking()
                    .Where(s => s.LibraryItemId == libraryItemId && s.LibraryItem != null)
                    .Select(s => new { s.DataVersion, s.LibraryItem!.MalId, s.LibraryItem.MediaType })
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);

                if (target is null)
                    return Result.Failure($"Snapshot for LibraryItem {libraryItemId} not found");
                if (target.DataVersion >= MediaDataVersion.Current)
                    return Result.Success();

                malId = target.MalId;
                mediaType = target.MediaType;
            }

            // Fetched fresh: the catalog row for this title may itself predate the extended fields.
            Action<MediaSnapshot> apply;
            int dataVersion;
            if (mediaType == MediaType.Anime)
            {
                var result = await catalogService.RefreshAnimeDetailsAsync(malId, ct).ConfigureAwait(false);
                if (!result.IsSuccess) return Result.Failure(result.Error!, result.Kind);
                var a = result.Value!;
                dataVersion = a.DataVersion;
                // Only ever adds: a value the source did not send keeps what the snapshot has.
                apply = s =>
                {
                    s.TitleEnglish = a.TitleEnglish ?? s.TitleEnglish;
                    s.Themes = a.Themes ?? s.Themes;
                    s.Demographics = a.Demographics ?? s.Demographics;
                    s.DurationMinutes = a.DurationMinutes ?? s.DurationMinutes;
                    s.AgeRating = a.AgeRating ?? s.AgeRating;
                    s.Source = a.Source ?? s.Source;
                    s.DataVersion = Math.Max(s.DataVersion, a.DataVersion);
                };
            }
            else
            {
                var result = await catalogService.GetMangaAsync(malId, force: true, ct).ConfigureAwait(false);
                if (!result.IsSuccess) return Result.Failure(result.Error!, result.Kind);
                var m = result.Value!;
                dataVersion = m.DataVersion;
                apply = s =>
                {
                    s.TitleEnglish = m.TitleEnglish ?? s.TitleEnglish;
                    s.Themes = m.Themes ?? s.Themes;
                    s.Demographics = m.Demographics ?? s.Demographics;
                    s.Authors = m.Authors ?? s.Authors;
                    s.DataVersion = Math.Max(s.DataVersion, m.DataVersion);
                };
            }

            var genres = await LoadGenresJsonAsync(malId, mediaType == MediaType.Anime ? "anime" : "manga", ct)
                .ConfigureAwait(false);

            await using (var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                var snapshot = await db.Snapshots.FindAsync([libraryItemId], ct).ConfigureAwait(false);
                if (snapshot is null)
                    return Result.Failure($"Snapshot for LibraryItem {libraryItemId} disappeared");

                apply(snapshot);
                // An empty list here means the catalog write failed, not that the title lost
                // its genres — keep what the snapshot already has.
                if (genres != "[]")
                    snapshot.Genres = genres;

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            // Only fallback data was available: whatever it could add is saved, but the item is
            // still incomplete. Reported as a failure so the backfill's "stop after N failures in a
            // row" also stops it from re-fetching the whole library while the primary source is down.
            if (dataVersion < MediaDataVersion.Current)
                return Result.Failure("Only partial (fallback) data is available right now.", ErrorKind.ServiceUnavailable);

            return Result.Success();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Extended metadata fill failed for LibraryItem {Id}", libraryItemId);
            return Result.Failure("Failed to fill extended metadata.");
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
        Genres = "[]",
        TitleEnglish = a.TitleEnglish,
        Themes = a.Themes,
        Demographics = a.Demographics,
        DurationMinutes = a.DurationMinutes,
        AgeRating = a.AgeRating,
        Source = a.Source,
        // Inherit the catalog row's generation: a snapshot built from a row cached before
        // extended metadata existed stays Legacy, so the backfill still completes it.
        DataVersion = a.DataVersion
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
        Genres = "[]",
        TitleEnglish = m.TitleEnglish,
        Themes = m.Themes,
        Demographics = m.Demographics,
        Authors = m.Authors,
        DataVersion = m.DataVersion
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The snapshot is the user's own copy of a title's data: fresh data may extend or update
    /// it, never erase it, and fallback-source data only fills its gaps (see <see cref="EntityMerge"/>).
    /// </summary>
    private static void MergeIntoSnapshot(EntityEntry<MediaSnapshot> stored, MediaSnapshot fresh)
    {
        // An empty genre list means the catalog had none to give, not that the title lost its genres.
        if (fresh.Genres == "[]")
            fresh.Genres = stored.Entity.Genres;

        EntityMerge.Apply(stored, fresh, EntityMerge.FillOnly(stored.Entity.DataVersion, fresh.DataVersion));
    }

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
            return NameList.ToJson(genres);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Genre load failed for {MalId}", malId);
            return "[]";
        }
    }
}
