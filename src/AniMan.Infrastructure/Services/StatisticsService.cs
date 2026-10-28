using System.Text.Json;
using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

public sealed class StatisticsService(
    IDbContextFactory<LibraryDbContext> libraryFactory,
    ILogger<StatisticsService> logger) : IStatisticsService
{
    private const int MinutesPerEpisode = 24;
    private const string LoadError = "Failed to load statistics.";

    public async Task<Result<int>> GetTotalEpisodesWatchedAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var count = await db.EpisodeProgress
                .CountAsync(e => e.IsWatched && e.LibraryItem != null && e.LibraryItem.DeletedAt == null, ct)
                .ConfigureAwait(false);
            return Result<int>.Success(count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTotalEpisodesWatched failed");
            return Result<int>.Failure(LoadError);
        }
    }

    public async Task<Result<TimeSpan>> GetTotalWatchTimeAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var watchedCount = await db.EpisodeProgress
                .CountAsync(e => e.IsWatched && e.LibraryItem != null && e.LibraryItem.DeletedAt == null, ct)
                .ConfigureAwait(false);

            var rewatchedItems = await db.LibraryItems.AsNoTracking()
                .Where(i => i.DeletedAt == null && i.MediaType == MediaType.Anime && i.RewatchCount > 0)
                .Select(i => new
                {
                    i.RewatchCount,
                    TotalEpisodes = i.Snapshot != null ? i.Snapshot.TotalEpisodes : null,
                    WatchedEpisodes = i.EpisodeProgress.Count(e => e.IsWatched)
                })
                .ToListAsync(ct).ConfigureAwait(false);

            var rewatchEpisodes = rewatchedItems.Sum(i =>
                (long)i.RewatchCount * (i.TotalEpisodes is > 0 ? i.TotalEpisodes.Value : i.WatchedEpisodes));

            var totalEpisodes = watchedCount + rewatchEpisodes;
            return Result<TimeSpan>.Success(TimeSpan.FromMinutes(totalEpisodes * MinutesPerEpisode));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTotalWatchTime failed");
            return Result<TimeSpan>.Failure(LoadError);
        }
    }

    public async Task<Result<int>> GetTotalChaptersReadAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var count = await db.ChapterProgress
                .CountAsync(c => c.IsRead && c.LibraryItem != null && c.LibraryItem.DeletedAt == null, ct)
                .ConfigureAwait(false);
            return Result<int>.Success(count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTotalChaptersRead failed");
            return Result<int>.Failure(LoadError);
        }
    }

    public async Task<Result<double?>> GetMeanScoreAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var scores = await db.LibraryItems.AsNoTracking()
                .Where(i => i.DeletedAt == null && (i.UserRating.HasValue || i.Score.HasValue))
                .Select(i => (double)(i.UserRating ?? i.Score!.Value))
                .Where(s => s > 0)
                .ToListAsync(ct).ConfigureAwait(false);

            double? mean = scores.Count > 0 ? scores.Average() : null;
            return Result<double?>.Success(mean);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetMeanScore failed");
            return Result<double?>.Failure(LoadError);
        }
    }

    public async Task<Result<IReadOnlyList<StatusBreakdown>>> GetStatusBreakdownAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var breakdown = await db.LibraryItems.AsNoTracking()
                .Where(i => i.DeletedAt == null)
                .GroupBy(i => i.StatusId)
                .Select(g => new { StatusId = g.Key, Count = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false);

            var statuses = await db.TrackingStatuses.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            var result = breakdown
                .Join(statuses, b => b.StatusId, s => s.Id,
                    (b, s) => new StatusBreakdown(s.Id, s.Name, b.Count, s.Color))
                .OrderByDescending(s => s.Count)
                .ToList();

            return Result<IReadOnlyList<StatusBreakdown>>.Success(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetStatusBreakdown failed");
            return Result<IReadOnlyList<StatusBreakdown>>.Failure(LoadError);
        }
    }

    public async Task<Result<IReadOnlyDictionary<int, int>>> GetScoreHistogramAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var rawScores = await db.LibraryItems.AsNoTracking()
                .Where(i => i.DeletedAt == null && (i.UserRating.HasValue || i.Score.HasValue))
                .Select(i => i.UserRating ?? (decimal?)i.Score)
                .ToListAsync(ct).ConfigureAwait(false);

            var histogram = rawScores
                .Where(s => s.HasValue && s.Value > 0m)
                .Select(s => Math.Clamp((int)Math.Round(s!.Value, MidpointRounding.AwayFromZero), 1, 10))
                .GroupBy(score => score)
                .ToDictionary(g => g.Key, g => g.Count());

            // Fill in 1–10 with 0 for missing scores
            var result = Enumerable.Range(1, 10)
                .ToDictionary(
                    score => score,
                    score => histogram.GetValueOrDefault(score, 0));

            return Result<IReadOnlyDictionary<int, int>>.Success(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetScoreHistogram failed");
            return Result<IReadOnlyDictionary<int, int>>.Failure(LoadError);
        }
    }

    public async Task<Result<IReadOnlyList<(string Genre, int Count)>>> GetTopGenresAsync(
        int n = 5, CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var genreJsonList = await db.Snapshots.AsNoTracking()
                .Where(s => s.LibraryItem != null && s.LibraryItem.DeletedAt == null)
                .Select(s => s.Genres)
                .ToListAsync(ct).ConfigureAwait(false);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var json in genreJsonList)
            {
                try
                {
                    var genres = JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? [];
                    foreach (var g in genres.Where(g => !string.IsNullOrWhiteSpace(g)))
                        counts[g] = counts.GetValueOrDefault(g) + 1;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to parse genre JSON");
                }
            }

            var top = counts.OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            return Result<IReadOnlyList<(string Genre, int Count)>>.Success(top);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTopGenres failed");
            return Result<IReadOnlyList<(string Genre, int Count)>>.Failure(LoadError);
        }
    }

    public async Task<Result<IReadOnlyList<MonthlyActivity>>> GetMonthlyActivityAsync(
        int months = 12, CancellationToken ct = default)
    {
        try
        {
            await using var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var cutoff = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(-Math.Max(0, months - 1));

            var episodeDates = await db.EpisodeProgress.AsNoTracking()
                .Where(e => e.IsWatched && e.WatchedAt >= cutoff && e.LibraryItem != null && e.LibraryItem.DeletedAt == null)
                .Select(e => e.WatchedAt)
                .ToListAsync(ct).ConfigureAwait(false);

            var chapterDates = await db.ChapterProgress.AsNoTracking()
                .Where(c => c.IsRead && c.ReadAt >= cutoff && c.LibraryItem != null && c.LibraryItem.DeletedAt == null)
                .Select(c => c.ReadAt)
                .ToListAsync(ct).ConfigureAwait(false);

            var grouped = episodeDates.Concat(chapterDates)
                .Where(d => d.HasValue)
                .GroupBy(d => new { d!.Value.Year, d.Value.Month })
                .Select(g => new MonthlyActivity(g.Key.Year, g.Key.Month, g.Count()))
                .OrderBy(m => m.Year).ThenBy(m => m.Month)
                .ToList();

            return Result<IReadOnlyList<MonthlyActivity>>.Success(grouped);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetMonthlyActivity failed");
            return Result<IReadOnlyList<MonthlyActivity>>.Failure(LoadError);
        }
    }
}
