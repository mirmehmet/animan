using System.Collections.ObjectModel;
using AniMan.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Stats;

public partial class StatsViewModel : ObservableObject
{
    private readonly IStatisticsService _stats;

    [ObservableProperty] private int _totalEpisodes;
    [ObservableProperty] private string _watchTimeText = "—";
    [ObservableProperty] private int _totalChapters;
    [ObservableProperty] private string _meanScoreText = "—";
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<StatusBarViewModel> _statusBreakdown = [];

    [ObservableProperty]
    private ObservableCollection<ScoreBarViewModel> _scoreHistogram = [];

    [ObservableProperty]
    private ObservableCollection<GenreBarViewModel> _topGenres = [];

    [ObservableProperty]
    private ObservableCollection<MonthBarViewModel> _monthlyActivity = [];

    public StatsViewModel(IStatisticsService stats)
    {
        _stats = stats;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var tasks = new[]
            {
                LoadMetricsAsync(),
                LoadStatusBreakdownAsync(),
                LoadScoreHistogramAsync(),
                LoadTopGenresAsync(),
                LoadMonthlyActivityAsync()
            };
            await Task.WhenAll(tasks);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadMetricsAsync()
    {
        var ep = await _stats.GetTotalEpisodesWatchedAsync();
        if (ep.IsSuccess) TotalEpisodes = ep.Value;

        var time = await _stats.GetTotalWatchTimeAsync();
        if (time.IsSuccess)
        {
            var t = time.Value;
            WatchTimeText = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes}m"
                : $"{t.Minutes}m";
        }

        var ch = await _stats.GetTotalChaptersReadAsync();
        if (ch.IsSuccess) TotalChapters = ch.Value;

        var score = await _stats.GetMeanScoreAsync();
        if (score.IsSuccess)
            MeanScoreText = score.Value.HasValue ? $"{score.Value.Value:F1}" : "—";
    }

    private async Task LoadStatusBreakdownAsync()
    {
        var result = await _stats.GetStatusBreakdownAsync();
        if (!result.IsSuccess) return;

        var items = result.Value!;
        int total = items.Sum(s => s.Count);
        if (total == 0) return;

        StatusBreakdown = new ObservableCollection<StatusBarViewModel>(
            items.Select(s => new StatusBarViewModel(
                s.StatusName, s.Count,
                (double)s.Count / total,
                s.Color)));
    }

    private async Task LoadScoreHistogramAsync()
    {
        var result = await _stats.GetScoreHistogramAsync();
        if (!result.IsSuccess) return;

        if (!result.Value!.Any()) return;
        int max = result.Value!.Values.Max();
        if (max == 0) return;

        ScoreHistogram = new ObservableCollection<ScoreBarViewModel>(
            result.Value.Select(kv => new ScoreBarViewModel(
                kv.Key, kv.Value,
                max > 0 ? (double)kv.Value / max : 0)));
    }

    private async Task LoadTopGenresAsync()
    {
        var result = await _stats.GetTopGenresAsync(8);
        if (!result.IsSuccess) return;

        var items = result.Value!;
        int max = items.Count > 0 ? items.Max(g => g.Count) : 0;
        if (max == 0) return;

        TopGenres = new ObservableCollection<GenreBarViewModel>(
            items.Select(g => new GenreBarViewModel(
                g.Genre, g.Count,
                (double)g.Count / max)));
    }

    private async Task LoadMonthlyActivityAsync()
    {
        var result = await _stats.GetMonthlyActivityAsync(12);
        if (!result.IsSuccess) return;

        var items = result.Value!;
        int max = items.Count > 0 ? items.Max(m => m.Count) : 0;
        if (max == 0) return;

        MonthlyActivity = new ObservableCollection<MonthBarViewModel>(
            items.Select(m => new MonthBarViewModel(
                $"{m.Year}/{m.Month:D2}", m.Count,
                (double)m.Count / max)));
    }
}

public record ScoreBarViewModel(int Score, int Count, double Fraction);
public record GenreBarViewModel(string Name, int Count, double Fraction);
public record MonthBarViewModel(string Label, int Count, double Fraction);
