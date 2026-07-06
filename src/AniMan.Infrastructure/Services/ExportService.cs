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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Result> ExportAsync(string filePath, CancellationToken ct = default)
    {
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

            await using (var stream = File.Create(filePath))
                await JsonSerializer.SerializeAsync(stream, backup, JsonOptions, ct).ConfigureAwait(false);

            await UpsertSettingAsync(db, "LastBackupAt", backup.ExportedAt.ToString("O"), ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            logger.LogInformation("Exported {Count} library items to {Path}", backup.Items.Count, filePath);
            return Result.Success();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export to {Path} failed", filePath);
            return Result.Failure("Export failed. Check the log file for details.");
        }
    }

    public async Task<Result> ImportAsync(string filePath, ImportMode mode, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return Result.Failure($"Backup file not found: {filePath}");

            LibraryBackup? backup;
            await using (var stream = File.OpenRead(filePath))
                backup = await JsonSerializer.DeserializeAsync<LibraryBackup>(stream, JsonOptions, ct).ConfigureAwait(false);

            if (backup is null)
                return Result.Failure("Backup file is empty or invalid.");
            if (backup.SchemaVersion > CurrentSchemaVersion)
                return Result.Failure(
                    $"Backup schema version {backup.SchemaVersion} is newer than supported ({CurrentSchemaVersion}).");

            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            // Overwrite deletes everything before inserting; a single transaction keeps
            // the user's library intact if the insert phase fails. The InMemory test
            // provider doesn't support transactions, so it takes the null path.
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
                : null;

            if (mode == ImportMode.Overwrite)
            {
                // Cascade delete removes snapshots/progress/notes/overrides with their item.
                db.LibraryItems.RemoveRange(await db.LibraryItems.ToListAsync(ct).ConfigureAwait(false));
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            var statusMap = await EnsureStatusesAsync(db, backup.Statuses, ct).ConfigureAwait(false);

            var existingKeys = mode == ImportMode.Merge
                ? (await db.LibraryItems.AsNoTracking()
                        .Select(i => new { i.MalId, i.MediaType })
                        .ToListAsync(ct).ConfigureAwait(false))
                    .Select(k => (k.MalId, k.MediaType))
                    .ToHashSet()
                : [];

            var imported = 0;
            var skipped = 0;
            foreach (var dto in backup.Items)
            {
                if (!Enum.TryParse<MediaType>(dto.MediaType, ignoreCase: true, out var mediaType))
                {
                    skipped++;
                    logger.LogWarning(
                        "Skipping backup item {MalId}: unknown media type '{MediaType}'",
                        dto.MalId, dto.MediaType);
                    continue;
                }

                if (mode == ImportMode.Merge && existingKeys.Contains((dto.MalId, mediaType)))
                    continue;

                var entity = BuildEntity(dto, mediaType, statusMap);

                // The backup stores an absolute CoverLocalPath from the exporting
                // machine — never trust it. Re-point to this machine's covers folder,
                // re-downloading from CoverOriginalUrl when the file is gone.
                if (entity.Snapshot is { } snap)
                    snap.CoverLocalPath = await coverStore.EnsureAsync(
                        dto.MalId, mediaType, snap.CoverOriginalUrl, ct).ConfigureAwait(false);

                db.LibraryItems.Add(entity);
                imported++;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);

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
            if (map.ContainsKey(dto.Name)) continue;

            var status = new TrackingStatus
            {
                Name = dto.Name,
                IsDefault = dto.IsDefault,
                DisplayOrder = dto.DisplayOrder,
                Color = dto.Color
            };
            db.TrackingStatuses.Add(status);
            map[dto.Name] = status;
        }

        return map;
    }

    private static TrackingStatus ResolveStatus(
        Dictionary<string, TrackingStatus> map, string statusName, MediaType mediaType)
    {
        if (map.TryGetValue(statusName, out var status))
            return status;

        // Fall back to the media-appropriate "Plan to …" default, else any status.
        var fallback = mediaType == MediaType.Anime ? "Plan to watch" : "Plan to read";
        return map.TryGetValue(fallback, out var planned) ? planned : map.Values.First();
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static LibraryItem BuildEntity(
        LibraryItemDto dto, MediaType mediaType, Dictionary<string, TrackingStatus> statusMap)
    {
        var item = new LibraryItem
        {
            MalId = dto.MalId,
            MediaType = mediaType,
            Status = ResolveStatus(statusMap, dto.StatusName, mediaType),
            Score = dto.Score,
            IsFavorite = dto.IsFavorite,
            RewatchCount = dto.RewatchCount,
            LastRewatchDate = dto.LastRewatchDate,
            StartedAt = dto.StartedAt,
            CompletedAt = dto.CompletedAt,
            AddedAt = dto.AddedAt,
            UpdatedAt = dto.UpdatedAt,
            EpisodeProgress = dto.Episodes
                .Select(e => new EpisodeProgress
                {
                    EpisodeNumber = e.EpisodeNumber, IsWatched = e.IsWatched, WatchedAt = e.WatchedAt
                }).ToList(),
            ChapterProgress = dto.Chapters
                .Select(c => new ChapterProgress
                {
                    ChapterNumber = c.ChapterNumber, IsRead = c.IsRead, ReadAt = c.ReadAt
                }).ToList(),
            Notes = dto.Notes
                .Select(n => new Note
                {
                    EpisodeNumber = n.EpisodeNumber, ChapterNumber = n.ChapterNumber,
                    Content = n.Content, CreatedAt = n.CreatedAt, UpdatedAt = n.UpdatedAt
                }).ToList(),
            StreamingOverrides = dto.StreamingOverrides
                .Select(o => new UserStreamingOverride
                {
                    PlatformName = o.PlatformName, Url = o.Url, AddedAt = o.AddedAt
                }).ToList()
        };

        if (dto.Snapshot is { } s)
        {
            item.Snapshot = new MediaSnapshot
            {
                Title = s.Title, TitleJapanese = s.TitleJapanese, Synopsis = s.Synopsis, Type = s.Type,
                Status = s.Status, Studio = s.Studio,
                TotalEpisodes = s.TotalEpisodes, TotalChapters = s.TotalChapters, TotalVolumes = s.TotalVolumes,
                AiringStart = s.AiringStart, AiringEnd = s.AiringEnd, Season = s.Season, Year = s.Year,
                MalScore = s.MalScore, Genres = s.Genres, CoverLocalPath = s.CoverLocalPath,
                CoverOriginalUrl = s.CoverOriginalUrl, StreamingPlatforms = s.StreamingPlatforms,
                SnapshotAt = s.SnapshotAt
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
        IsFavorite = i.IsFavorite,
        RewatchCount = i.RewatchCount,
        LastRewatchDate = i.LastRewatchDate,
        StartedAt = i.StartedAt,
        CompletedAt = i.CompletedAt,
        AddedAt = i.AddedAt,
        UpdatedAt = i.UpdatedAt,
        Snapshot = i.Snapshot is null ? null : new SnapshotDto(
            i.Snapshot.Title, i.Snapshot.TitleJapanese, i.Snapshot.Synopsis, i.Snapshot.Type,
            i.Snapshot.Status, i.Snapshot.Studio,
            i.Snapshot.TotalEpisodes, i.Snapshot.TotalChapters, i.Snapshot.TotalVolumes,
            i.Snapshot.AiringStart, i.Snapshot.AiringEnd, i.Snapshot.Season, i.Snapshot.Year,
            i.Snapshot.MalScore, i.Snapshot.Genres, i.Snapshot.CoverLocalPath,
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
}
