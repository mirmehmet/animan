using System.Collections.Concurrent;
using System.Text.Json;
using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

public sealed class ExportService(
    IDbContextFactory<LibraryDbContext> libraryFactory,
    CoverStore coverStore,
    ILogger<ExportService> logger) : IExportService
{
    internal const int CurrentSchemaVersion = 1;
    private const long MaxBackupSizeBytes = 50L * 1024 * 1024; // 50 MB

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Result> ExportAsync(string filePath, CancellationToken ct = default)
    {
        var tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            var statuses = await db.TrackingStatuses.AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => new StatusDto(s.Name, s.IsDefault, s.DisplayOrder, s.Color))
                .ToListAsync(ct).ConfigureAwait(false);

            var items = await db.LibraryItems.AsNoTracking()
                .Include(i => i.Status)
                .Include(i => i.Snapshot)
                .Include(i => i.EpisodeProgress)
                .Include(i => i.ChapterProgress)
                .Include(i => i.Notes)
                .Include(i => i.StreamingOverrides)
                .ToListAsync(ct).ConfigureAwait(false);

            var backup = new LibraryBackup
            {
                SchemaVersion = CurrentSchemaVersion,
                ExportedAt = DateTime.UtcNow,
                Statuses = statuses,
                Items = items.Select(ToDto).ToList()
            };

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await using (var stream = File.Create(tmpPath))
                await JsonSerializer.SerializeAsync(stream, backup, JsonOptions, ct).ConfigureAwait(false);

            File.Move(tmpPath, filePath, overwrite: true);

            await UpsertSettingAsync(db, "LastBackupAt", backup.ExportedAt.ToString("O"), ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            logger.LogInformation("Exported {Count} library items to {Path}", backup.Items.Count, filePath);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            TryDeleteTempFile(tmpPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteTempFile(tmpPath);
            logger.LogError(ex, "Export to {Path} failed", filePath);
            return Result.Failure("Export failed. Check the log file for details.");
        }
    }

    public async Task<Result> ImportAsync(string filePath, ImportMode mode, CancellationToken ct = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                return Result.Failure($"Backup file not found: {filePath}");

            if (fileInfo.Length > MaxBackupSizeBytes)
                return Result.Failure("Backup file is too large (maximum 50 MB).");

            LibraryBackup? backup;
            await using (var stream = File.OpenRead(filePath))
                backup = await JsonSerializer.DeserializeAsync<LibraryBackup>(stream, JsonOptions, ct).ConfigureAwait(false);

            if (backup is null || backup.SchemaVersion < 1 || backup.ExportedAt == default)
                return Result.Failure("Backup file is empty or invalid.");
            if (backup.SchemaVersion > CurrentSchemaVersion)
                return Result.Failure(
                    $"Backup schema version {backup.SchemaVersion} is newer than supported ({CurrentSchemaVersion}).");

            var backupItems = backup.Items ?? [];

            var referencedCovers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingCoverDownloads = new List<(LibraryItem Entity, int MalId, MediaType MediaType, string Url)>();
            var imported = 0;
            var skipped = 0;

            await using (var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                if (mode == ImportMode.Overwrite && backupItems.Count == 0 &&
                    await db.LibraryItems.AnyAsync(ct).ConfigureAwait(false))
                {
                    return Result.Failure("Backup contains no items; refusing to overwrite a non-empty library.");
                }

                // Overwrite deletes everything before inserting; a single fast DB-only transaction
                // keeps the user's library intact if the insert phase fails, without holding
                // SQLite write locks during network cover downloads.
                await using var transaction = db.Database.IsRelational()
                    ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
                    : null;

                if (mode == ImportMode.Overwrite)
                {
                    // Cascade delete removes snapshots/progress/notes/overrides with their item.
                    db.LibraryItems.RemoveRange(await db.LibraryItems.ToListAsync(ct).ConfigureAwait(false));
                    var customStatuses = await db.TrackingStatuses
                        .Where(s => !s.IsDefault)
                        .ToListAsync(ct).ConfigureAwait(false);
                    if (customStatuses.Count > 0)
                        db.TrackingStatuses.RemoveRange(customStatuses);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }

                var statusMap = await EnsureStatusesAsync(db, backup.Statuses ?? [], ct).ConfigureAwait(false);

                var existingActiveKeys = new HashSet<(int MalId, MediaType MediaType)>();
                var existingTrashedByKey = new Dictionary<(int MalId, MediaType MediaType), LibraryItem>();

                if (mode == ImportMode.Merge)
                {
                    var localItems = await db.LibraryItems.ToListAsync(ct).ConfigureAwait(false);
                    foreach (var local in localItems)
                    {
                        if (local.DeletedAt is null)
                            existingActiveKeys.Add((local.MalId, local.MediaType));
                        else
                            existingTrashedByKey[(local.MalId, local.MediaType)] = local;
                    }
                }

                var seenKeys = new HashSet<(int MalId, MediaType MediaType)>(existingActiveKeys);

                foreach (var dto in backupItems)
                {
                    if (dto.MalId <= 0)
                    {
                        skipped++;
                        logger.LogWarning("Skipping backup item with invalid MalId {MalId}", dto.MalId);
                        continue;
                    }

                    if (!Enum.TryParse<MediaType>(dto.MediaType, ignoreCase: true, out var mediaType))
                    {
                        skipped++;
                        logger.LogWarning(
                            "Skipping backup item {MalId}: unknown media type '{MediaType}'",
                            dto.MalId, dto.MediaType);
                        continue;
                    }

                    var key = (dto.MalId, mediaType);
                    if (mode == ImportMode.Merge)
                    {
                        if (!seenKeys.Add(key))
                        {
                            skipped++;
                            continue;
                        }

                        if (existingTrashedByKey.TryGetValue(key, out var trashedLocal))
                        {
                            if (dto.DeletedAt is not null)
                                continue;

                            db.LibraryItems.Remove(trashedLocal);
                            await db.SaveChangesAsync(ct).ConfigureAwait(false);
                            existingTrashedByKey.Remove(key);
                        }
                    }

                    var entity = BuildEntity(dto, mediaType, statusMap);

                    // Never trust a CoverLocalPath from the backup file. Point directly to
                    // this machine's canonical cover file if already on disk; otherwise queue
                    // the remote download to run AFTER the SQLite transaction commits.
                    if (entity.Snapshot is { } snap)
                    {
                        var existingLocal = coverStore.GetExistingLocalPath(dto.MalId, mediaType);
                        if (existingLocal is not null)
                        {
                            snap.CoverLocalPath = existingLocal;
                            referencedCovers.Add(Path.GetFullPath(existingLocal));
                        }
                        else if (!string.IsNullOrWhiteSpace(snap.CoverOriginalUrl))
                        {
                            pendingCoverDownloads.Add((entity, dto.MalId, mediaType, snap.CoverOriginalUrl));
                        }
                    }

                    db.LibraryItems.Add(entity);
                    imported++;
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                if (transaction is not null)
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
            }

            if (pendingCoverDownloads.Count > 0)
            {
                var downloaded = new ConcurrentBag<(int LibraryItemId, string Path)>();
                await Parallel.ForEachAsync(
                    pendingCoverDownloads,
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                    async (work, token) =>
                    {
                        var path = await coverStore.EnsureAsync(work.MalId, work.MediaType, work.Url, token)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(path))
                            downloaded.Add((work.Entity.Id, path));
                    }).ConfigureAwait(false);

                if (!downloaded.IsEmpty)
                {
                    var byItemId = downloaded.ToDictionary(d => d.LibraryItemId, d => d.Path);
                    foreach (var path in byItemId.Values)
                        referencedCovers.Add(Path.GetFullPath(path));

                    await using var coverDb = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                    var itemIds = byItemId.Keys.ToList();
                    var snapshots = await coverDb.Snapshots
                        .Where(s => itemIds.Contains(s.LibraryItemId))
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var snap in snapshots)
                    {
                        if (byItemId.TryGetValue(snap.LibraryItemId, out var resolvedPath))
                            snap.CoverLocalPath = resolvedPath;
                    }
                    await coverDb.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }

            if (mode == ImportMode.Overwrite)
                coverStore.SweepUnreferenced(referencedCovers);

            logger.LogInformation(
                "Imported {Count} items ({Mode}) from {Path}; {Skipped} skipped",
                imported, mode, filePath, skipped);
            return Result.Success();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import from {Path} failed", filePath);
            return Result.Failure("Import failed. The backup file may be invalid or unreadable.");
        }
    }

    // ── Status reconciliation ──────────────────────────────────────────────────

    /// <summary>
    /// Maps each backup status name to a tracked <see cref="TrackingStatus"/>, inserting
    /// any custom statuses that don't already exist. Returns a name → entity lookup.
    /// </summary>
    private static async Task<Dictionary<string, TrackingStatus>> EnsureStatusesAsync(
        LibraryDbContext db, List<StatusDto> backupStatuses, CancellationToken ct)
    {
        var existing = await db.TrackingStatuses.ToListAsync(ct).ConfigureAwait(false);
        var map = existing.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var dto in backupStatuses)
        {
            var trimmedName = dto.Name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName) || map.ContainsKey(trimmedName))
                continue;

            var status = new TrackingStatus
            {
                Name = trimmedName,
                // Seeded default statuses already exist in DB; custom statuses from backups are never system defaults.
                IsDefault = false,
                DisplayOrder = dto.DisplayOrder,
                Color = dto.Color
            };
            db.TrackingStatuses.Add(status);
            map[trimmedName] = status;
        }

        return map;
    }

    private static TrackingStatus ResolveStatus(
        Dictionary<string, TrackingStatus> map, string? statusName, MediaType mediaType)
    {
        if (!string.IsNullOrWhiteSpace(statusName) && map.TryGetValue(statusName.Trim(), out var status))
            return status;

        // Fall back to the media-appropriate "Plan to …" default, else any status.
        var fallback = mediaType == MediaType.Anime ? "Plan to watch" : "Plan to read";
        return map.TryGetValue(fallback, out var planned) ? planned : map.Values.First();
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static LibraryItem BuildEntity(
        LibraryItemDto dto, MediaType mediaType, Dictionary<string, TrackingStatus> statusMap)
    {
        var sanitizedScore = dto.Score is >= 1 and <= 10 ? dto.Score : null;
        var sanitizedRating = dto.UserRating.HasValue
            ? Math.Round(Math.Clamp(dto.UserRating.Value, 0m, 10m), 1, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        var item = new LibraryItem
        {
            MalId = dto.MalId,
            MediaType = mediaType,
            Status = ResolveStatus(statusMap, dto.StatusName, mediaType),
            Score = sanitizedScore,
            UserRating = sanitizedRating,
            IsFavorite = dto.IsFavorite,
            RewatchCount = Math.Max(0, dto.RewatchCount),
            LastRewatchDate = dto.LastRewatchDate,
            StartedAt = dto.StartedAt,
            CompletedAt = dto.CompletedAt,
            AddedAt = dto.AddedAt == default ? DateTime.UtcNow : dto.AddedAt,
            UpdatedAt = dto.UpdatedAt == default ? DateTime.UtcNow : dto.UpdatedAt,
            DeletedAt = dto.DeletedAt,
            EpisodeProgress = (dto.Episodes ?? [])
                .Where(e => e.EpisodeNumber > 0)
                .DistinctBy(e => e.EpisodeNumber)
                .Select(e => new EpisodeProgress
                {
                    EpisodeNumber = e.EpisodeNumber, IsWatched = e.IsWatched, WatchedAt = e.WatchedAt
                }).ToList(),
            ChapterProgress = (dto.Chapters ?? [])
                .Where(c => c.ChapterNumber > 0)
                .DistinctBy(c => c.ChapterNumber)
                .Select(c => new ChapterProgress
                {
                    ChapterNumber = c.ChapterNumber, IsRead = c.IsRead, ReadAt = c.ReadAt
                }).ToList(),
            Notes = (dto.Notes ?? [])
                .Where(n => !string.IsNullOrWhiteSpace(n.Content))
                .GroupBy(n => (n.EpisodeNumber, n.ChapterNumber))
                .Select(g => g.Last())
                .Select(n => new Note
                {
                    EpisodeNumber = n.EpisodeNumber,
                    ChapterNumber = n.ChapterNumber,
                    Content = n.Content,
                    CreatedAt = n.CreatedAt == default ? DateTime.UtcNow : n.CreatedAt,
                    UpdatedAt = n.UpdatedAt == default ? DateTime.UtcNow : n.UpdatedAt
                }).ToList(),
            StreamingOverrides = (dto.StreamingOverrides ?? [])
                .Where(o => !string.IsNullOrWhiteSpace(o.PlatformName)
                            && (string.IsNullOrWhiteSpace(o.Url) || HttpUrlGuard.IsAllowedWebUrl(o.Url, out _)))
                .Select(o => new UserStreamingOverride
                {
                    PlatformName = o.PlatformName.Trim(),
                    Url = HttpUrlGuard.SanitizeWebUrl(o.Url),
                    AddedAt = o.AddedAt == default ? DateTime.UtcNow : o.AddedAt
                }).ToList()
        };

        if (dto.Snapshot is { } s)
        {
            item.Snapshot = new MediaSnapshot
            {
                Title = string.IsNullOrWhiteSpace(s.Title) ? $"#{dto.MalId}" : s.Title,
                TitleJapanese = s.TitleJapanese,
                Synopsis = s.Synopsis,
                Type = s.Type,
                Status = s.Status,
                Studio = s.Studio,
                TotalEpisodes = s.TotalEpisodes is > 0 ? s.TotalEpisodes : null,
                TotalChapters = s.TotalChapters is > 0 ? s.TotalChapters : null,
                TotalVolumes = s.TotalVolumes is > 0 ? s.TotalVolumes : null,
                AiringStart = s.AiringStart,
                AiringEnd = s.AiringEnd,
                Season = s.Season,
                Year = s.Year,
                MalScore = s.MalScore,
                Genres = string.IsNullOrWhiteSpace(s.Genres) ? "[]" : s.Genres,
                CoverLocalPath = null,
                CoverOriginalUrl = HttpUrlGuard.SanitizeWebUrl(s.CoverOriginalUrl),
                StreamingPlatforms = s.StreamingPlatforms,
                SnapshotAt = s.SnapshotAt == default ? DateTime.UtcNow : s.SnapshotAt
            };
        }

        return item;
    }

    private static LibraryItemDto ToDto(LibraryItem i) => new()
    {
        MalId = i.MalId,
        MediaType = i.MediaType.ToString(),
        StatusName = i.Status?.Name ?? string.Empty,
        Score = i.Score,
        UserRating = i.UserRating,
        IsFavorite = i.IsFavorite,
        RewatchCount = i.RewatchCount,
        LastRewatchDate = i.LastRewatchDate,
        StartedAt = i.StartedAt,
        CompletedAt = i.CompletedAt,
        AddedAt = i.AddedAt,
        UpdatedAt = i.UpdatedAt,
        DeletedAt = i.DeletedAt,
        Snapshot = i.Snapshot is null ? null : new SnapshotDto(
            i.Snapshot.Title, i.Snapshot.TitleJapanese, i.Snapshot.Synopsis, i.Snapshot.Type,
            i.Snapshot.Status, i.Snapshot.Studio,
            i.Snapshot.TotalEpisodes, i.Snapshot.TotalChapters, i.Snapshot.TotalVolumes,
            i.Snapshot.AiringStart, i.Snapshot.AiringEnd, i.Snapshot.Season, i.Snapshot.Year,
            i.Snapshot.MalScore, i.Snapshot.Genres, CoverLocalPath: null,
            i.Snapshot.CoverOriginalUrl, i.Snapshot.StreamingPlatforms, i.Snapshot.SnapshotAt),
        Episodes = i.EpisodeProgress
            .Select(e => new EpisodeDto(e.EpisodeNumber, e.IsWatched, e.WatchedAt)).ToList(),
        Chapters = i.ChapterProgress
            .Select(c => new ChapterDto(c.ChapterNumber, c.IsRead, c.ReadAt)).ToList(),
        Notes = i.Notes
            .Select(n => new NoteDto(n.EpisodeNumber, n.ChapterNumber, n.Content, n.CreatedAt, n.UpdatedAt)).ToList(),
        StreamingOverrides = i.StreamingOverrides
            .Select(o => new OverrideDto(o.PlatformName, o.Url, o.AddedAt)).ToList()
    };

    private static async Task UpsertSettingAsync(
        LibraryDbContext db, string key, string value, CancellationToken ct)
    {
        var existing = await db.Settings.FindAsync([key], ct).ConfigureAwait(false);
        if (existing is null)
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        else
            existing.Value = value;
    }

    private static void TryDeleteTempFile(string tmpPath)
    {
        try
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of incomplete temporary export file.
        }
    }
}
