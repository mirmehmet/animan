using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

public sealed class TrackingService(
    IDbContextFactory<LibraryDbContext> libraryFactory,
    ILogger<TrackingService> logger) : ITrackingService
{
    private const string LoadError = "Failed to load your library.";
    private const string ProgressError = "Failed to update your progress.";
    private const string SaveError = "Failed to save your changes.";

    public async Task<Result<IReadOnlyList<LibraryItem>>> GetLibraryAsync(
        string mediaType, CancellationToken ct = default)
    {
        if (!Enum.TryParse<MediaType>(mediaType, ignoreCase: true, out var parsedType))
            return Result<IReadOnlyList<LibraryItem>>.Failure($"Unknown media type: {mediaType}");

        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var items = await db.LibraryItems.AsNoTracking()
                .Include(i => i.Snapshot)
                .Include(i => i.Status)
                .Include(i => i.EpisodeProgress)
                .Include(i => i.ChapterProgress)
                .Where(i => i.MediaType == parsedType && i.DeletedAt == null)
                .OrderByDescending(i => i.UpdatedAt)
                .ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<LibraryItem>>.Success(items);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetLibrary failed for {Type}", mediaType);
            return Result<IReadOnlyList<LibraryItem>>.Failure(LoadError);
        }
    }

    public async Task<Result<LibraryItem>> GetItemAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var item = await db.LibraryItems.AsNoTracking()
                .Include(i => i.Snapshot)
                .Include(i => i.Status)
                .Include(i => i.EpisodeProgress)
                .Include(i => i.ChapterProgress)
                .Include(i => i.Notes)
                .Include(i => i.StreamingOverrides)
                .FirstOrDefaultAsync(i => i.Id == libraryItemId, ct).ConfigureAwait(false);

            return item is null
                ? Result<LibraryItem>.Failure($"Item {libraryItemId} not found")
                : Result<LibraryItem>.Success(item);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetItem failed for {Id}", libraryItemId);
            return Result<LibraryItem>.Failure(LoadError);
        }
    }

    // ── Episode/chapter progress ──────────────────────────────────────────────

    public Task<Result<TrackingResult>> SetEpisodeWatchedAsync(
        int libraryItemId, int episodeNumber, bool watched, CancellationToken ct = default) =>
        MutateProgressAsync(libraryItemId, isManga: false, nameof(SetEpisodeWatchedAsync), async db =>
        {
            var ep = await db.EpisodeProgress.FirstOrDefaultAsync(
                e => e.LibraryItemId == libraryItemId && e.EpisodeNumber == episodeNumber, ct).ConfigureAwait(false);

            if (ep is null)
            {
                db.EpisodeProgress.Add(new EpisodeProgress
                {
                    LibraryItemId = libraryItemId,
                    EpisodeNumber = episodeNumber,
                    IsWatched = watched,
                    WatchedAt = watched ? DateTime.UtcNow : null
                });
            }
            else
            {
                ep.IsWatched = watched;
                ep.WatchedAt = watched ? DateTime.UtcNow : null;
            }
        }, ct);

    public Task<Result<TrackingResult>> MarkUpToHereAsync(
        int libraryItemId, int episodeNumber, CancellationToken ct = default) =>
        MutateProgressAsync(libraryItemId, isManga: false, nameof(MarkUpToHereAsync), async db =>
        {
            // "Exactly up to here": everything at or below the target is watched
            // (existing watch dates preserved), everything above it — including
            // detached marks further up — is removed.
            var all = await db.EpisodeProgress
                .Where(e => e.LibraryItemId == libraryItemId)
                .ToListAsync(ct).ConfigureAwait(false);

            db.EpisodeProgress.RemoveRange(all.Where(e => e.EpisodeNumber > episodeNumber));

            var existingSet = new HashSet<int>();
            foreach (var ep in all.Where(e => e.EpisodeNumber <= episodeNumber))
            {
                existingSet.Add(ep.EpisodeNumber);
                ep.IsWatched = true;
                ep.WatchedAt ??= DateTime.UtcNow;
            }

            for (int i = 1; i <= episodeNumber; i++)
            {
                if (!existingSet.Contains(i))
                {
                    db.EpisodeProgress.Add(new EpisodeProgress
                    {
                        LibraryItemId = libraryItemId,
                        EpisodeNumber = i,
                        IsWatched = true,
                        WatchedAt = DateTime.UtcNow
                    });
                }
            }
        }, ct);

    public Task<Result<TrackingResult>> SetChapterReadAsync(
        int libraryItemId, int chapterNumber, bool read, CancellationToken ct = default) =>
        MutateProgressAsync(libraryItemId, isManga: true, nameof(SetChapterReadAsync), async db =>
        {
            var ch = await db.ChapterProgress.FirstOrDefaultAsync(
                c => c.LibraryItemId == libraryItemId && c.ChapterNumber == chapterNumber, ct).ConfigureAwait(false);

            if (ch is null)
            {
                db.ChapterProgress.Add(new ChapterProgress
                {
                    LibraryItemId = libraryItemId,
                    ChapterNumber = chapterNumber,
                    IsRead = read,
                    ReadAt = read ? DateTime.UtcNow : null
                });
            }
            else
            {
                ch.IsRead = read;
                ch.ReadAt = read ? DateTime.UtcNow : null;
            }
        }, ct);

    public Task<Result<TrackingResult>> MarkChaptersUpToAsync(
        int libraryItemId, int chapterNumber, CancellationToken ct = default) =>
        MutateProgressAsync(libraryItemId, isManga: true, nameof(MarkChaptersUpToAsync), async db =>
        {
            // "Exactly up to here" — see MarkUpToHereAsync; chapter mirror.
            var all = await db.ChapterProgress
                .Where(c => c.LibraryItemId == libraryItemId)
                .ToListAsync(ct).ConfigureAwait(false);

            db.ChapterProgress.RemoveRange(all.Where(c => c.ChapterNumber > chapterNumber));

            var existingSet = new HashSet<int>();
            foreach (var ch in all.Where(c => c.ChapterNumber <= chapterNumber))
            {
                existingSet.Add(ch.ChapterNumber);
                ch.IsRead = true;
                ch.ReadAt ??= DateTime.UtcNow;
            }

            for (int i = 1; i <= chapterNumber; i++)
            {
                if (!existingSet.Contains(i))
                {
                    db.ChapterProgress.Add(new ChapterProgress
                    {
                        LibraryItemId = libraryItemId,
                        ChapterNumber = i,
                        IsRead = true,
                        ReadAt = DateTime.UtcNow
                    });
                }
            }
        }, ct);

    // ── Item field updates ────────────────────────────────────────────────────

    public Task<Result> UpdateScoreAsync(
        int libraryItemId, int? score, CancellationToken ct = default)
    {
        if (score is < 1 or > 10)
            return Task.FromResult(Result.Failure("Score must be between 1 and 10"));

        return MutateItemAsync(libraryItemId, nameof(UpdateScoreAsync),
            item => item.Score = score, ct);
    }

    public Task<Result> UpdateStatusAsync(
        int libraryItemId, int statusId, CancellationToken ct = default) =>
        MutateItemAsync(libraryItemId, nameof(UpdateStatusAsync),
            item => item.StatusId = statusId, ct);

    public Task<Result> ToggleFavoriteAsync(
        int libraryItemId, CancellationToken ct = default) =>
        MutateItemAsync(libraryItemId, nameof(ToggleFavoriteAsync),
            item => item.IsFavorite = !item.IsFavorite, ct);

    public Task<Result> IncrementRewatchAsync(
        int libraryItemId, DateOnly? date, CancellationToken ct = default) =>
        MutateItemAsync(libraryItemId, nameof(IncrementRewatchAsync), item =>
        {
            item.RewatchCount++;
            if (date.HasValue) item.LastRewatchDate = date;
        }, ct);

    public Task<Result> SetRatingAsync(
        int libraryItemId, decimal? rating, CancellationToken ct = default)
    {
        if (rating is < 0m or > 10m)
            return Task.FromResult(Result.Failure("Rating must be between 0.0 and 10.0"));

        if (rating.HasValue)
            rating = Math.Round(rating.Value, 1);

        return MutateItemAsync(libraryItemId, nameof(SetRatingAsync),
            item => item.UserRating = rating, ct);
    }

    public Task<Result> SoftDeleteAsync(
        int libraryItemId, CancellationToken ct = default) =>
        MutateItemAsync(libraryItemId, nameof(SoftDeleteAsync),
            item => item.DeletedAt = DateTime.UtcNow, ct);

    public Task<Result> RestoreFromTrashAsync(
        int libraryItemId, CancellationToken ct = default) =>
        MutateItemAsync(libraryItemId, nameof(RestoreFromTrashAsync),
            item => item.DeletedAt = null, ct);

    // ── Notes / progress info ─────────────────────────────────────────────────

    public async Task<Result<Note>> SaveNoteAsync(
        int libraryItemId, int? episodeNumber, int? chapterNumber,
        string content, CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            var existing = await db.Notes.FirstOrDefaultAsync(n =>
                n.LibraryItemId == libraryItemId &&
                n.EpisodeNumber == episodeNumber &&
                n.ChapterNumber == chapterNumber, ct).ConfigureAwait(false);

            if (existing is null)
            {
                existing = new Note
                {
                    LibraryItemId = libraryItemId,
                    EpisodeNumber = episodeNumber,
                    ChapterNumber = chapterNumber,
                    Content = content,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Notes.Add(existing);
            }
            else
            {
                existing.Content = content;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogDebug("Note saved for item {Id} ep={Ep} ch={Ch}", libraryItemId, episodeNumber, chapterNumber);
            return Result<Note>.Success(existing);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "SaveNote failed for item {Id}", libraryItemId);
            return Result<Note>.Failure("Failed to save the note.");
        }
    }

    public async Task<Result<ProgressInfo>> GetProgressAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            var item = await db.LibraryItems.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == libraryItemId, ct).ConfigureAwait(false);
            if (item is null)
                return Result<ProgressInfo>.Failure($"Item {libraryItemId} not found");

            var (done, total) = await CountProgressAsync(
                db, libraryItemId, item.MediaType == MediaType.Manga, ct).ConfigureAwait(false);
            return Result<ProgressInfo>.Success(new ProgressInfo(done, total));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetProgress failed for item {Id}", libraryItemId);
            return Result<ProgressInfo>.Failure(LoadError);
        }
    }

    // ── Trash ─────────────────────────────────────────────────────────────────

    public async Task<Result> EmptyTrashAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var trashed = await db.LibraryItems
                .Where(i => i.DeletedAt != null)
                .ToListAsync(ct).ConfigureAwait(false);
            db.LibraryItems.RemoveRange(trashed);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "EmptyTrash failed");
            return Result.Failure(SaveError);
        }
    }

    public async Task<Result<IReadOnlyList<LibraryItem>>> GetTrashAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var items = await db.LibraryItems.AsNoTracking()
                .Include(i => i.Snapshot)
                .Where(i => i.DeletedAt != null)
                .OrderByDescending(i => i.DeletedAt)
                .ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<LibraryItem>>.Success(items);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTrash failed");
            return Result<IReadOnlyList<LibraryItem>>.Failure(LoadError);
        }
    }

    public async Task PurgeExpiredTrashAsync(CancellationToken ct = default)
    {
        // Called on startup — a purge failure must never block the app from launching.
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var expired = await db.LibraryItems
                .Where(i => i.DeletedAt != null && i.DeletedAt < cutoff)
                .ToListAsync(ct).ConfigureAwait(false);
            if (expired.Count > 0)
            {
                db.LibraryItems.RemoveRange(expired);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                logger.LogInformation("Purged {Count} expired trash items", expired.Count);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "PurgeExpiredTrash failed");
        }
    }

    // ── Shared cores ──────────────────────────────────────────────────────────

    /// <summary>Find-mutate-save skeleton shared by all single-item field updates.</summary>
    private async Task<Result> MutateItemAsync(
        int libraryItemId, string operation, Action<LibraryItem> mutate, CancellationToken ct)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var item = await db.LibraryItems.FindAsync([libraryItemId], ct).ConfigureAwait(false);
            if (item is null) return Result.Failure($"Item {libraryItemId} not found");

            mutate(item);
            item.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Operation} failed for item {Id}", operation, libraryItemId);
            return Result.Failure(SaveError);
        }
    }

    /// <summary>
    /// Progress-mutation skeleton shared by the episode/chapter Set and MarkUpTo
    /// methods: mutate, bump the item timestamp, save, report auto-complete state.
    /// </summary>
    private async Task<Result<TrackingResult>> MutateProgressAsync(
        int libraryItemId, bool isManga, string operation,
        Func<LibraryDbContext, Task> mutate, CancellationToken ct)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await mutate(db).ConfigureAwait(false);

            // State already matches the target → true no-op: don't bump the item
            // timestamp (library sort order) and don't re-trigger auto-complete.
            if (!db.ChangeTracker.HasChanges())
                return Result<TrackingResult>.Success(new TrackingResult(AutoCompleteNeeded: false));

            await UpdateItemTimestampAsync(db, libraryItemId, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return await BuildResultAsync(db, libraryItemId, isManga, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Operation} failed for item {Id}", operation, libraryItemId);
            return Result<TrackingResult>.Failure(ProgressError);
        }
    }

    private static async Task UpdateItemTimestampAsync(
        LibraryDbContext db, int libraryItemId, CancellationToken ct)
    {
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct).ConfigureAwait(false);
        if (item is not null) item.UpdatedAt = DateTime.UtcNow;
    }

    private static async Task<(int Done, int Total)> CountProgressAsync(
        LibraryDbContext db, int libraryItemId, bool isManga, CancellationToken ct)
    {
        var snapshot = await db.Snapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryItemId == libraryItemId, ct).ConfigureAwait(false);

        if (isManga)
        {
            var read = await db.ChapterProgress.CountAsync(
                c => c.LibraryItemId == libraryItemId && c.IsRead, ct).ConfigureAwait(false);
            return (read, snapshot?.TotalChapters ?? 0);
        }

        var watched = await db.EpisodeProgress.CountAsync(
            e => e.LibraryItemId == libraryItemId && e.IsWatched, ct).ConfigureAwait(false);
        return (watched, snapshot?.TotalEpisodes ?? 0);
    }

    private static async Task<Result<TrackingResult>> BuildResultAsync(
        LibraryDbContext db, int libraryItemId, bool isManga, CancellationToken ct)
    {
        var (done, total) = await CountProgressAsync(db, libraryItemId, isManga, ct).ConfigureAwait(false);
        var autoComplete = total > 0 && done >= total;
        return Result<TrackingResult>.Success(new TrackingResult(autoComplete));
    }
}
