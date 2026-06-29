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
    public async Task<Result<IReadOnlyList<LibraryItem>>> GetLibraryAsync(
        string mediaType, CancellationToken ct = default)
    {
        if (!Enum.TryParse<MediaType>(mediaType, ignoreCase: true, out var parsedType))
            return Result<IReadOnlyList<LibraryItem>>.Failure($"Unknown media type: {mediaType}");

        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var items = await db.LibraryItems.AsNoTracking()
            .Include(i => i.Snapshot)
            .Include(i => i.Status)
            .Include(i => i.EpisodeProgress)
            .Include(i => i.ChapterProgress)
            .Where(i => i.MediaType == parsedType && i.DeletedAt == null)
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);
        return Result<IReadOnlyList<LibraryItem>>.Success(items);
    }

    public async Task<Result<LibraryItem>> GetItemAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.AsNoTracking()
            .Include(i => i.Snapshot)
            .Include(i => i.Status)
            .Include(i => i.EpisodeProgress)
            .Include(i => i.ChapterProgress)
            .Include(i => i.Notes)
            .Include(i => i.StreamingOverrides)
            .FirstOrDefaultAsync(i => i.Id == libraryItemId, ct);

        return item is null
            ? Result<LibraryItem>.Failure($"Item {libraryItemId} not found")
            : Result<LibraryItem>.Success(item);
    }

    public async Task<Result<TrackingResult>> ToggleEpisodeAsync(
        int libraryItemId, int episodeNumber, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var ep = await db.EpisodeProgress.FirstOrDefaultAsync(
            e => e.LibraryItemId == libraryItemId && e.EpisodeNumber == episodeNumber, ct);

        if (ep is null)
        {
            db.EpisodeProgress.Add(new EpisodeProgress
            {
                LibraryItemId = libraryItemId,
                EpisodeNumber = episodeNumber,
                IsWatched = true,
                WatchedAt = DateTime.UtcNow
            });
        }
        else
        {
            ep.IsWatched = !ep.IsWatched;
            ep.WatchedAt = ep.IsWatched ? DateTime.UtcNow : null;
        }

        await UpdateItemTimestampAsync(db, libraryItemId, ct);
        await db.SaveChangesAsync(ct);

        return await BuildTrackingResultAsync(db, libraryItemId, ct);
    }

    public async Task<Result<TrackingResult>> SetEpisodeWatchedAsync(
        int libraryItemId, int episodeNumber, bool watched, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var ep = await db.EpisodeProgress.FirstOrDefaultAsync(
            e => e.LibraryItemId == libraryItemId && e.EpisodeNumber == episodeNumber, ct);

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

        await UpdateItemTimestampAsync(db, libraryItemId, ct);
        await db.SaveChangesAsync(ct);

        return await BuildTrackingResultAsync(db, libraryItemId, ct);
    }

    public async Task<Result<TrackingResult>> MarkUpToHereAsync(
        int libraryItemId, int episodeNumber, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var existing = await db.EpisodeProgress
            .Where(e => e.LibraryItemId == libraryItemId && e.EpisodeNumber <= episodeNumber)
            .ToListAsync(ct);

        var existingSet = existing.Select(e => e.EpisodeNumber).ToHashSet();

        foreach (var ep in existing)
        {
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

        await UpdateItemTimestampAsync(db, libraryItemId, ct);
        await db.SaveChangesAsync(ct);

        return await BuildTrackingResultAsync(db, libraryItemId, ct);
    }

    public async Task<Result<TrackingResult>> ToggleChapterAsync(
        int libraryItemId, int chapterNumber, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var ch = await db.ChapterProgress.FirstOrDefaultAsync(
            c => c.LibraryItemId == libraryItemId && c.ChapterNumber == chapterNumber, ct);

        if (ch is null)
        {
            db.ChapterProgress.Add(new ChapterProgress
            {
                LibraryItemId = libraryItemId,
                ChapterNumber = chapterNumber,
                IsRead = true,
                ReadAt = DateTime.UtcNow
            });
        }
        else
        {
            ch.IsRead = !ch.IsRead;
            ch.ReadAt = ch.IsRead ? DateTime.UtcNow : null;
        }

        await UpdateItemTimestampAsync(db, libraryItemId, ct);
        await db.SaveChangesAsync(ct);

        return await BuildChapterTrackingResultAsync(db, libraryItemId, ct);
    }

    public async Task<Result<TrackingResult>> SetChapterReadAsync(
        int libraryItemId, int chapterNumber, bool read, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var ch = await db.ChapterProgress.FirstOrDefaultAsync(
            c => c.LibraryItemId == libraryItemId && c.ChapterNumber == chapterNumber, ct);

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

        await UpdateItemTimestampAsync(db, libraryItemId, ct);
        await db.SaveChangesAsync(ct);

        return await BuildChapterTrackingResultAsync(db, libraryItemId, ct);
    }

    public async Task<Result<TrackingResult>> MarkChaptersUpToAsync(
        int libraryItemId, int chapterNumber, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var existing = await db.ChapterProgress
            .Where(c => c.LibraryItemId == libraryItemId && c.ChapterNumber <= chapterNumber)
            .ToListAsync(ct);

        var existingSet = existing.Select(c => c.ChapterNumber).ToHashSet();

        foreach (var ch in existing)
        {
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

        await UpdateItemTimestampAsync(db, libraryItemId, ct);
        await db.SaveChangesAsync(ct);

        return await BuildChapterTrackingResultAsync(db, libraryItemId, ct);
    }

    public async Task<Result> UpdateScoreAsync(
        int libraryItemId, int? score, CancellationToken ct = default)
    {
        if (score is < 1 or > 10)
            return Result.Failure("Score must be between 1 and 10");

        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Failure($"Item {libraryItemId} not found");

        item.Score = score;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateStatusAsync(
        int libraryItemId, int statusId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Failure($"Item {libraryItemId} not found");

        item.StatusId = statusId;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ToggleFavoriteAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Failure($"Item {libraryItemId} not found");

        item.IsFavorite = !item.IsFavorite;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> IncrementRewatchAsync(
        int libraryItemId, DateOnly? date, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Failure($"Item {libraryItemId} not found");

        item.RewatchCount++;
        if (date.HasValue) item.LastRewatchDate = date;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Note>> SaveNoteAsync(
        int libraryItemId, int? episodeNumber, int? chapterNumber,
        string content, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var existing = await db.Notes.FirstOrDefaultAsync(n =>
            n.LibraryItemId == libraryItemId &&
            n.EpisodeNumber == episodeNumber &&
            n.ChapterNumber == chapterNumber, ct);

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

        await db.SaveChangesAsync(ct);
        logger.LogDebug("Note saved for item {Id} ep={Ep} ch={Ch}", libraryItemId, episodeNumber, chapterNumber);
        return Result<Note>.Success(existing);
    }

    public async Task<Result<ProgressInfo>> GetProgressAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);

        var item = await db.LibraryItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == libraryItemId, ct);
        if (item is null)
            return Result<ProgressInfo>.Failure($"Item {libraryItemId} not found");

        var snapshot = await db.Snapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryItemId == libraryItemId, ct);

        if (item.MediaType == MediaType.Manga)
        {
            var totalChapters = snapshot?.TotalChapters ?? 0;
            var read = await db.ChapterProgress.CountAsync(
                c => c.LibraryItemId == libraryItemId && c.IsRead, ct);
            return Result<ProgressInfo>.Success(new ProgressInfo(read, totalChapters));
        }

        var totalEpisodes = snapshot?.TotalEpisodes ?? 0;
        var watched = await db.EpisodeProgress.CountAsync(
            e => e.LibraryItemId == libraryItemId && e.IsWatched, ct);

        return Result<ProgressInfo>.Success(new ProgressInfo(watched, totalEpisodes));
    }

    public async Task<Result> SetRatingAsync(
        int libraryItemId, decimal? rating, CancellationToken ct = default)
    {
        if (rating is < 0m or > 10m)
            return Result.Failure("Rating must be between 0.0 and 10.0");

        if (rating.HasValue)
            rating = Math.Round(rating.Value, 1);

        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Failure($"Item {libraryItemId} not found");

        item.UserRating = rating;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SoftDeleteAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Failure($"Item {libraryItemId} not found");

        item.DeletedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RestoreFromTrashAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Failure($"Item {libraryItemId} not found");

        item.DeletedAt = null;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> PermanentDeleteAsync(
        int libraryItemId, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is null) return Result.Success();

        db.LibraryItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> EmptyTrashAsync(CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var trashed = await db.LibraryItems
            .Where(i => i.DeletedAt != null)
            .ToListAsync(ct);
        db.LibraryItems.RemoveRange(trashed);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<LibraryItem>>> GetTrashAsync(
        CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var items = await db.LibraryItems.AsNoTracking()
            .Include(i => i.Snapshot)
            .Where(i => i.DeletedAt != null)
            .OrderByDescending(i => i.DeletedAt)
            .ToListAsync(ct);
        return Result<IReadOnlyList<LibraryItem>>.Success(items);
    }

    public async Task PurgeExpiredTrashAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var expired = await db.LibraryItems
            .Where(i => i.DeletedAt != null && i.DeletedAt < cutoff)
            .ToListAsync(ct);
        if (expired.Count > 0)
        {
            db.LibraryItems.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Purged {Count} expired trash items", expired.Count);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task UpdateItemTimestampAsync(
        LibraryDbContext db, int libraryItemId, CancellationToken ct)
    {
        var item = await db.LibraryItems.FindAsync([libraryItemId], ct);
        if (item is not null) item.UpdatedAt = DateTime.UtcNow;
    }

    private static async Task<Result<TrackingResult>> BuildTrackingResultAsync(
        LibraryDbContext db, int libraryItemId, CancellationToken ct)
    {
        var snapshot = await db.Snapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryItemId == libraryItemId, ct);
        var total = snapshot?.TotalEpisodes ?? 0;
        var watched = await db.EpisodeProgress.CountAsync(
            e => e.LibraryItemId == libraryItemId && e.IsWatched, ct);

        var autoComplete = total > 0 && watched >= total;
        return Result<TrackingResult>.Success(new TrackingResult(autoComplete));
    }

    private static async Task<Result<TrackingResult>> BuildChapterTrackingResultAsync(
        LibraryDbContext db, int libraryItemId, CancellationToken ct)
    {
        var snapshot = await db.Snapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryItemId == libraryItemId, ct);
        var total = snapshot?.TotalChapters ?? 0;
        var read = await db.ChapterProgress.CountAsync(
            c => c.LibraryItemId == libraryItemId && c.IsRead, ct);

        var autoComplete = total > 0 && read >= total;
        return Result<TrackingResult>.Success(new TrackingResult(autoComplete));
    }
}
