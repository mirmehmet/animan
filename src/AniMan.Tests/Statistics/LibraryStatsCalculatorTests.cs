using AniMan.Core.Statistics;
using FluentAssertions;

namespace AniMan.Tests.Statistics;

public class LibraryStatsCalculatorTests
{
    private const int Completed = 3;
    private const int Watching = 1;

    private static LibraryOverview Overview(params StatsEntry[] entries) =>
        LibraryStatsCalculator.ComputeOverview(entries, Completed, Watching);

    [Fact]
    public void EmptyLibrary_ProducesZerosAndNoScores()
    {
        var o = Overview();

        o.TotalTitles.Should().Be(0);
        o.WatchMinutes.Should().Be(0);
        o.MeanScore.Should().BeNull();
        o.MalDelta.Should().BeNull();
        o.Scores.Should().HaveCount(10).And.OnlyContain(b => b.Count == 0);
        o.ReleaseYears.Should().BeEmpty();
        o.Taste.Should().BeEmpty();
    }

    [Fact]
    public void WatchMinutes_UsesEachTitlesOwnEpisodeLength()
    {
        var o = Overview(
            new StatsEntry { UnitsDone = 12, MinutesPerUnit = 24 },   // 288
            new StatsEntry { UnitsDone = 1, MinutesPerUnit = 105 });  // a movie: 105

        o.WatchMinutes.Should().Be(393);
    }

    [Fact]
    public void WatchMinutes_UnknownLength_FallsBackTo24()
    {
        Overview(new StatsEntry { UnitsDone = 10 }).WatchMinutes.Should().Be(240);
    }

    [Fact]
    public void WatchMinutes_CountsFullRewatchesOfKnownLengthSeries()
    {
        // 12 watched + 2 rewatches of a 12-episode series = 36 episodes.
        var o = Overview(new StatsEntry { UnitsDone = 12, TotalUnits = 12, RewatchCount = 2, MinutesPerUnit = 20 });

        o.WatchMinutes.Should().Be(36 * 20);
    }

    [Fact]
    public void MeanAndMalDelta_UseOnlyTitlesWithTheNeededScores()
    {
        var o = Overview(
            new StatsEntry { Rating = 9m, MalScore = 8.0 },   // +1.0
            new StatsEntry { Rating = 7m, MalScore = 8.0 },   // -1.0
            new StatsEntry { Rating = 8m, MalScore = null },  // counts for mean only
            new StatsEntry { Rating = null, MalScore = 9.0 }); // ignored

        o.MeanScore.Should().Be(8.0);
        o.MalDelta.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void ScoreBuckets_RoundHalfUp_AndCoverOneToTen()
    {
        var o = Overview(
            new StatsEntry { Rating = 8.5m },
            new StatsEntry { Rating = 8.4m },
            new StatsEntry { Rating = 10m });

        o.Scores.Single(b => b.Score == 9).Count.Should().Be(1);
        o.Scores.Single(b => b.Score == 8).Count.Should().Be(1);
        o.Scores.Single(b => b.Score == 10).Count.Should().Be(1);
    }

    [Fact]
    public void Taste_RequiresThreeTitlesWithBothScores_SortedByYourMean()
    {
        StatsEntry E(decimal r, double mal, params string[] g) => new() { Rating = r, MalScore = mal, Genres = g };

        var o = Overview(
            E(9, 8.0, "Drama"), E(8, 8.0, "Drama"), E(10, 9.0, "Drama", "Action"),
            E(6, 7.0, "Action"), E(7, 7.5, "Action"),
            E(10, 9.0, "Romance"), E(10, 9.0, "Romance")); // only 2 → excluded

        o.Taste.Select(t => t.Genre).Should().Equal("Drama", "Action");
        var drama = o.Taste[0];
        drama.YourMean.Should().Be(9.0);
        drama.MalMean.Should().BeApproximately(8.333, 0.001);
        drama.Count.Should().Be(3);
    }

    [Fact]
    public void TopGenres_CountEachTitleOnce()
    {
        var o = Overview(
            new StatsEntry { Genres = ["Action", "action"] },
            new StatsEntry { Genres = ["Action", "Drama"] });

        o.TopGenres.Should().Equal(new RankedValue("Action", 2), new RankedValue("Drama", 1));
    }

    [Fact]
    public void ReleaseYears_FillGapsBetweenOldestAndNewest()
    {
        var o = Overview(new StatsEntry { Year = 2020 }, new StatsEntry { Year = 2022 }, new StatsEntry { Year = 2022 });

        o.ReleaseYears.Should().Equal(
            new YearBucket(2020, 2020, 1), new YearBucket(2021, 2021, 0), new YearBucket(2022, 2022, 2));
    }

    [Fact]
    public void ReleaseYears_WideSpan_GroupsIntoFiveYearColumns()
    {
        var o = Overview(new StatsEntry { Year = 1988 }, new StatsEntry { Year = 2024 }, new StatsEntry { Year = 2021 });

        o.ReleaseYears[0].Should().Be(new YearBucket(1985, 1989, 1));
        o.ReleaseYears[^1].Should().Be(new YearBucket(2020, 2024, 2));
        o.ReleaseYears.Should().OnlyContain(b => b.To - b.From == 4);
    }

    [Fact]
    public void StatusCounts_AndCompletedInProgress()
    {
        var o = Overview(
            new StatsEntry { StatusId = Completed }, new StatsEntry { StatusId = Completed },
            new StatsEntry { StatusId = Watching }, new StatsEntry { StatusId = 6 });

        o.Completed.Should().Be(2);
        o.InProgress.Should().Be(1);
        o.Statuses[0].Should().Be(new StatusSlice(Completed, 2));
    }

    // ── Period ────────────────────────────────────────────────────────────────

    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Dec31 = new(2026, 12, 31);

    [Fact]
    public void Period_CountsOnlyDatedUnitsInsideTheRange()
    {
        var e = new StatsEntry
        {
            MinutesPerUnit = 30,
            Genres = ["Action"],
            UnitDates = [new(2025, 12, 31), new(2026, 3, 1), new(2026, 3, 1), new(2026, 7, 4)],
            CompletedAt = new DateOnly(2026, 7, 4)
        };

        var p = LibraryStatsCalculator.ComputePeriod([e], Jan1, Dec31);

        p.UnitsInPeriod.Should().Be(3);
        p.MinutesInPeriod.Should().Be(90);
        p.DailyUnits[new DateOnly(2026, 3, 1)].Should().Be(2);
        p.DailyUnits.Should().NotContainKey(new DateOnly(2025, 12, 31));
        p.CompletedInPeriod.Should().Be(1);
        p.MostActiveMonth.Should().Be((2026, 3));
        p.TopGenre.Should().Be("Action");
    }

    [Fact]
    public void Period_TopGenre_WeightedByUnitsWatchedInPeriod()
    {
        var p = LibraryStatsCalculator.ComputePeriod(
        [
            new StatsEntry { Genres = ["Drama"], UnitDates = [new(2026, 2, 1)] },
            new StatsEntry { Genres = ["Comedy"], UnitDates = [new(2026, 2, 1), new(2026, 2, 2), new(2026, 2, 3)] }
        ], Jan1, Dec31);

        p.TopGenre.Should().Be("Comedy");
    }

    [Fact]
    public void Period_NoActivity_IsEmpty()
    {
        var p = LibraryStatsCalculator.ComputePeriod([new StatsEntry { UnitsDone = 5 }], Jan1, Dec31);

        p.UnitsInPeriod.Should().Be(0);
        p.DailyUnits.Should().BeEmpty();
        p.TopGenre.Should().BeNull();
        p.MostActiveMonth.Should().BeNull();
    }

    [Fact]
    public void ActiveYears_FromDatesAndCompletions_NewestFirst()
    {
        var years = LibraryStatsCalculator.ActiveYears(
        [
            new StatsEntry { UnitDates = [new(2024, 5, 1)] },
            new StatsEntry { CompletedAt = new DateOnly(2026, 1, 2) },
            new StatsEntry { UnitDates = [new(2024, 6, 1)] }
        ]);

        years.Should().Equal(2026, 2024);
    }
}
