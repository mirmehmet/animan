using System.Text.Json;
using AniMan.Core.Common;
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
            var count = await db.EpisodeProgress.CountAsync(e => e.IsWatched, ct).ConfigureAwait(false);
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
            var count = await db.EpisodeProgress.CountAsync(e => e.IsWatched, ct).ConfigureAwait(false);
            return Result<TimeSpan>.Success(TimeSpan.FromMinutes(count * MinutesPerEpisode));
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
            var count = await db.ChapterProgress.CountAsync(c => c.IsRead, ct).ConfigureAwait(false);
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
                .Where(i => i.Score.HasValue)
                .Select(i => (double)i.Score!.Value)
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
                .GroupBy(i => i.StatusId)
                .Select(g => new { StatusId = g.Key, Count = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false);

            var statuses = await db.TrackingStatuses.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            var result = breakdown
                .Join(statuses, b => b.StatusId, s => s.Id,
                    (b, s) => new StatusBreakdown(s.Name, b.Count, s.Color))
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
            var histogram = await db.LibraryItems.AsNoTracking()
                .Where(i => i.Score.HasValue)
                .GroupBy(i => i.Score!.Value)
                .Select(g => new { Score = g.Key, Count = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false);

            // Fill in 1–10 with 0 for missing scores
            var result = Enumerable.Range(1, 10)
                .ToDictionary(
                    score => score,
                    score => histogram.FirstOrDefault(h => h.Score == score)?.Count ?? 0);

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
                .Select(s => s.Genres)
                .ToListAsync(ct).ConfigureAwait(false);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var json in genreJsonList)
            {
                try
                {
                    var genres = JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? [];
                    foreach (var g in genres)
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
            var cutoff = DateTime.UtcNow.AddMonths(-months);

            var activity = await db.EpisodeProgress.AsNoTracking()
                .Where(e => e.IsWatched && e.WatchedAt >= cutoff)
                .Select(e => new { e.WatchedAt })
                .ToListAsync(ct).ConfigureAwait(false);

            var grouped = activity
                .Where(e => e.WatchedAt.HasValue)
                .GroupBy(e => new { e.WatchedAt!.Value.Year, e.WatchedAt.Value.Month })
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
