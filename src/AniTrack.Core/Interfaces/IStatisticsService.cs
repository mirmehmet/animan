using AniTrack.Core.Common;

namespace AniTrack.Core.Interfaces;

public record StatusBreakdown(string StatusName, int Count, string? Color);
public record MonthlyActivity(int Year, int Month, int Count);

public interface IStatisticsService
{
    Task<Result<int>> GetTotalEpisodesWatchedAsync(CancellationToken ct = default);
    Task<Result<TimeSpan>> GetTotalWatchTimeAsync(CancellationToken ct = default);
    Task<Result<int>> GetTotalChaptersReadAsync(CancellationToken ct = default);
    Task<Result<double?>> GetMeanScoreAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<StatusBreakdown>>> GetStatusBreakdownAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyDictionary<int, int>>> GetScoreHistogramAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<(string Genre, int Count)>>> GetTopGenresAsync(int n = 5, CancellationToken ct = default);
    Task<Result<IReadOnlyList<MonthlyActivity>>> GetMonthlyActivityAsync(int months = 12, CancellationToken ct = default);
}
