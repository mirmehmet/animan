using AniMan.Core.Library;
using FluentAssertions;

namespace AniMan.Tests.Library;

public class LibraryFilterEngineTests
{
    private sealed record Entry : ILibraryEntry
    {
        public string Title { get; init; } = "";
        public string? Format { get; init; }
        public int? Year { get; init; }
        public IReadOnlyList<string> Genres { get; init; } = [];
        public IReadOnlyList<string> Themes { get; init; } = [];
        public IReadOnlyList<string> Demographics { get; init; } = [];
        public string? ReleaseStatus { get; init; }
        public string? Source { get; init; }
        public string? AgeRating { get; init; }
        public decimal? UserRating { get; init; }
        public double? MalScore { get; init; }
        public DateTime AddedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public double ProgressPercent { get; init; }
    }

    private static readonly Entry Aot = new()
    {
        Title = "Shingeki no Kyojin", Format = "TV", Year = 2013,
        Genres = ["Action", "Drama"], Themes = ["Military", "Gore"], Demographics = ["Shounen"],
        ReleaseStatus = "Finished Airing", Source = "Manga", AgeRating = "R-17+", UserRating = 9.5m, MalScore = 8.55
    };

    private static readonly Entry YourName = new()
    {
        Title = "Kimi no Na wa.", Format = "Movie", Year = 2016,
        Genres = ["Drama", "Romance"], Demographics = [],
        ReleaseStatus = "Finished Airing", Source = "Original", AgeRating = "PG-13", UserRating = 8m, MalScore = 8.83
    };

    private static readonly Entry Frieren = new()
    {
        Title = "Sousou no Frieren", Format = "TV", Year = 2023,
        Genres = ["Adventure", "Drama", "Fantasy"], Demographics = ["Shounen"],
        ReleaseStatus = "Finished Airing", Source = "Manga", AgeRating = "PG-13", UserRating = null, MalScore = 9.3
    };

    /// <summary>A Legacy item: never backfilled, so no themes/demographics/source.</summary>
    private static readonly Entry Legacy = new()
    {
        Title = "Old entry", Format = "OVA", Year = null, Genres = ["Action"]
    };

    private static readonly Entry[] All = [Aot, YourName, Frieren, Legacy];

    private static IEnumerable<string> Run(LibraryFilter f) =>
        All.Where(e => LibraryFilterEngine.Matches(e, f)).Select(e => e.Title);

    [Fact]
    public void EmptyFilter_MatchesEverything()
    {
        var f = new LibraryFilter();

        f.IsEmpty.Should().BeTrue();
        Run(f).Should().HaveCount(4);
    }

    [Fact]
    public void Formats_AreAlternatives()
    {
        var f = new LibraryFilter();
        f.Formats.UnionWith(["Movie", "OVA"]);

        Run(f).Should().BeEquivalentTo("Kimi no Na wa.", "Old entry");
    }

    [Fact]
    public void Genres_MustAllBePresent()
    {
        var f = new LibraryFilter();
        f.Genres.UnionWith(["Drama", "Fantasy"]);

        Run(f).Should().Equal("Sousou no Frieren");
    }

    [Fact]
    public void Genres_MatchAny_NeedOnlyOne()
    {
        var f = new LibraryFilter { GenresMatchAny = true };
        f.Genres.UnionWith(["Romance", "Fantasy"]);

        Run(f).Should().BeEquivalentTo("Kimi no Na wa.", "Sousou no Frieren");
    }

    [Fact]
    public void Themes_MatchAny_StillExcludesItemsWithoutThemes()
    {
        var f = new LibraryFilter { ThemesMatchAny = true };
        f.Themes.UnionWith(["Military", "School"]);

        Run(f).Should().Equal("Shingeki no Kyojin");
    }

    [Fact]
    public void MatchModes_ArePerGroup()
    {
        // Genres: any of Romance/Action. Themes: all of Military+Gore.
        var f = new LibraryFilter { GenresMatchAny = true, ThemesMatchAny = false };
        f.Genres.UnionWith(["Romance", "Action"]);
        f.Themes.UnionWith(["Military", "Gore"]);

        Run(f).Should().Equal("Shingeki no Kyojin");
    }

    [Fact]
    public void GenreMatching_IgnoresCase()
    {
        var f = new LibraryFilter();
        f.Genres.Add("drama");

        Run(f).Should().HaveCount(3);
    }

    [Fact]
    public void Groups_CombineWithAnd()
    {
        var f = new LibraryFilter();
        f.Formats.Add("TV");
        f.Genres.Add("Drama");
        f.AgeRatings.Add("PG-13");

        Run(f).Should().Equal("Sousou no Frieren");
    }

    [Fact]
    public void YearRange_IsInclusive_AndExcludesItemsWithoutAYear()
    {
        var f = new LibraryFilter { YearFrom = 2013, YearTo = 2016 };

        Run(f).Should().BeEquivalentTo("Shingeki no Kyojin", "Kimi no Na wa.");
    }

    [Fact]
    public void OpenEndedYear_FromOnly()
    {
        var f = new LibraryFilter { YearFrom = 2016 };

        Run(f).Should().BeEquivalentTo("Kimi no Na wa.", "Sousou no Frieren");
    }

    [Fact]
    public void ThemeFilter_ExcludesItemsWhoseThemesAreNotKnownYet()
    {
        var f = new LibraryFilter();
        f.Themes.Add("Military");

        Run(f).Should().Equal("Shingeki no Kyojin");
    }

    [Fact]
    public void Demographics_AreAlternatives()
    {
        var f = new LibraryFilter();
        f.Demographics.UnionWith(["Shounen", "Seinen"]);

        Run(f).Should().BeEquivalentTo("Shingeki no Kyojin", "Sousou no Frieren");
    }

    [Theory]
    [InlineData(RatingFilter.AtLeast9, new[] { "Shingeki no Kyojin" })]
    [InlineData(RatingFilter.AtLeast8, new[] { "Shingeki no Kyojin", "Kimi no Na wa." })]
    [InlineData(RatingFilter.Unrated, new[] { "Sousou no Frieren", "Old entry" })]
    public void RatingFilter_UsesMyRating(RatingFilter rating, string[] expected) =>
        Run(new LibraryFilter { Rating = rating }).Should().BeEquivalentTo(expected);

    [Fact]
    public void ActiveCount_CountsEachValuePlusYearAndRatingOnce()
    {
        var f = new LibraryFilter { YearFrom = 2010, YearTo = 2020, Rating = RatingFilter.AtLeast7 };
        f.Genres.UnionWith(["Action", "Drama"]);
        f.Formats.Add("TV");

        f.ActiveCount.Should().Be(5);
    }

    [Fact]
    public void BuildOptions_ListsOnlyPresentValuesWithCounts_MostCommonFirst()
    {
        var options = LibraryFilterEngine.BuildOptions(All);

        options.Formats.Should().Equal(new FilterOption("TV", 2), new FilterOption("Movie", 1), new FilterOption("OVA", 1));
        options.Genres[0].Should().Be(new FilterOption("Drama", 3));
        options.Themes.Select(t => t.Value).Should().BeEquivalentTo("Gore", "Military");
        options.Sources.Should().Contain(new FilterOption("Manga", 2));
        options.Years.Should().Equal(2023, 2016, 2013);
    }

    [Fact]
    public void Sort_MyRating_HighestFirst_UnratedLast()
    {
        LibraryFilterEngine.Sort(All, LibrarySort.MyRating).Select(e => e.Title)
            .Should().Equal("Shingeki no Kyojin", "Kimi no Na wa.", "Sousou no Frieren", "Old entry");
    }

    [Fact]
    public void Sort_Year_NewestFirst_UnknownLast()
    {
        LibraryFilterEngine.Sort(All, LibrarySort.Year).Select(e => e.Title)
            .Should().Equal("Sousou no Frieren", "Kimi no Na wa.", "Shingeki no Kyojin", "Old entry");
    }

    [Fact]
    public void Sort_Title_Alphabetical()
    {
        LibraryFilterEngine.Sort(All, LibrarySort.Title).Select(e => e.Title)
            .Should().Equal("Kimi no Na wa.", "Old entry", "Shingeki no Kyojin", "Sousou no Frieren");
    }

    [Theory]
    [InlineData("PG-13 - Teens 13 or older", "PG-13")]
    [InlineData("R - 17+ (violence & profanity)", "R-17+")]
    [InlineData("R+ - Mild Nudity", "R+")]
    [InlineData("Rx - Hentai", "Rx")]
    [InlineData("G - All Ages", "G")]
    [InlineData("PG - Children", "PG")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void AgeRatingCodes_Shorten(string? raw, string? expected) =>
        AgeRatingCodes.Shorten(raw).Should().Be(expected);
}
