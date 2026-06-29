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

    public async Task<Result<int>> GetTotalEpisodesWatchedAsync(CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var count = await db.EpisodeProgress.CountAsync(e => e.IsWatched, ct);
        return Result<int>.Success(count);
    }

    public async Task<Result<TimeSpan>> GetTotalWatchTimeAsync(CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var count = await db.EpisodeProgress.CountAsync(e => e.IsWatched, ct);
        return Result<TimeSpan>.Success(TimeSpan.FromMinutes(count * MinutesPerEpisode));
    }

    public async Task<Result<int>> GetTotalChaptersReadAsync(CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var count = await db.ChapterProgress.CountAsync(c => c.IsRead, ct);
        return Result<int>.Success(count);
    }

    public async Task<Result<double?>> GetMeanScoreAsync(CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var scores = await db.LibraryItems.AsNoTracking()
            .Where(i => i.Score.HasValue)
            .Select(i => (double)i.Score!.Value)
            .ToListAsync(ct);

        double? mean = scores.Count > 0 ? scores.Average() : null;
        return Result<double?>.Success(mean);
    }

    public async Task<Result<IReadOnlyList<StatusBreakdown>>> GetStatusBreakdownAsync(
        CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var breakdown = await db.LibraryItems.AsNoTracking()
            .GroupBy(i => i.StatusId)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var statuses = await db.TrackingStatuses.AsNoTracking().ToListAsync(ct);
        var result = breakdown
            .Join(statuses, b => b.StatusId, s => s.Id,
                (b, s) => new StatusBreakdown(s.Name, b.Count, s.Color))
            .OrderByDescending(s => s.Count)
            .ToList();

        return Result<IReadOnlyList<StatusBreakdown>>.Success(result);
    }

    public async Task<Result<IReadOnlyDictionary<int, int>>> GetScoreHistogramAsync(
        CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var histogram = await db.LibraryItems.AsNoTracking()
            .Where(i => i.Score.HasValue)
            .GroupBy(i => i.Score!.Value)
            .Select(g => new { Score = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Fill in 1–10 with 0 for missing scores
        var result = Enumerable.Range(1, 10)
            .ToDictionary(
                score => score,
                score => histogram.FirstOrDefault(h => h.Score == score)?.Count ?? 0);

        return Result<IReadOnlyDictionary<int, int>>.Success(result);
    }

    public async Task<Result<IReadOnlyList<(string Genre, int Count)>>> GetTopGenresAsync(
        int n = 5, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var genreJsonList = await db.Snapshots.AsNoTracking()
            .Select(s => s.Genres)
            .ToListAsync(ct);

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
                logger.LogDebug(ex, "Failed to parse genre JSON: {Json}", json);
            }
        }

        var top = counts.OrderByDescending(kv => kv.Value)
            .Take(n)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        return Result<IReadOnlyList<(string Genre, int Count)>>.Success(top);
    }

    public async Task<Result<IReadOnlyList<MonthlyActivity>>> GetMonthlyActivityAsync(
        int months = 12, CancellationToken ct = default)
    {
        await using var db = await libraryFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddMonths(-months);

        var activity = await db.EpisodeProgress.AsNoTracking()
            .Where(e => e.IsWatched && e.WatchedAt >= cutoff)
            .Select(e => new { e.WatchedAt })
            .ToListAsync(ct);

        var grouped = activity
            .Where(e => e.WatchedAt.HasValue)
            .GroupBy(e => new { e.WatchedAt!.Value.Year, e.WatchedAt.Value.Month })
            .Select(g => new MonthlyActivity(g.Key.Year, g.Key.Month, g.Count()))
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        return Result<IReadOnlyList<MonthlyActivity>>.Success(grouped);
    }
}
