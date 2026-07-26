using AniMan.Infrastructure.AniList;
using AniMan.Infrastructure.AniList.Dtos;
using FluentAssertions;

namespace AniMan.Tests.AniList;

/// <summary>
/// AniList and MyAnimeList disagree on more than field names — score scale, ranking
/// semantics and date shape all differ. These tests pin the translations that would
/// otherwise corrupt cached data silently.
/// </summary>
public class AniListMapperTests
{
    [Fact]
    public void ToAnimeDtos_EntryWithoutMalId_IsDropped()
    {
        // MAL id is the primary key; unfiltered, every such entry collides at id 0.
        // Real results contain them: 12 of the 50 most recently added anime.
        IReadOnlyList<AniListMedia> media =
        [
            Anime(idMal: 30831, title: "Konosuba"),
            Anime(idMal: null, title: "(Title to be Announced)"),
            Anime(idMal: 21, title: "One Piece")
        ];

        var mapped = AniListMapper.ToAnimeDtos(media);

        mapped.Select(m => m.MalId).Should().Equal(30831, 21);
    }

    [Fact]
    public void ToMangaDtos_EntryWithoutMalId_IsDropped()
    {
        IReadOnlyList<AniListMedia> media = [Manga(idMal: null), Manga(idMal: 2)];

        AniListMapper.ToMangaDtos(media).Select(m => m.MalId).Should().Equal(2);
    }

    [Fact]
    public void ToAnimeDto_Score_IsConvertedFromHundredToTenScale()
    {
        // AniList reports 0-100, MyAnimeList 0-10.
        AniListMapper.ToAnimeDto(Anime(averageScore: 79)).Score.Should().Be(7.9);
        AniListMapper.ToAnimeDto(Anime(averageScore: 100)).Score.Should().Be(10.0);
        AniListMapper.ToAnimeDto(Anime(averageScore: null)).Score.Should().BeNull();
    }

    [Fact]
    public void ToAnimeDto_RankAndPopularity_StayNull()
    {
        // AniList's `popularity` is a member count, not MyAnimeList's popularity rank —
        // carrying it through would corrupt sorting and the Stats page.
        var dto = AniListMapper.ToAnimeDto(Anime(averageScore: 79));

        dto.Rank.Should().BeNull();
        dto.Popularity.Should().BeNull();
    }

    [Theory]
    [InlineData("TV", "TV")]
    [InlineData("MOVIE", "Movie")]
    [InlineData("OVA", "OVA")]
    [InlineData("TV_SHORT", "TV Short")]
    public void ToAnimeDto_Format_IsTranslated(string aniListFormat, string expected) =>
        AniListMapper.ToAnimeDto(Anime(format: aniListFormat)).Type.Should().Be(expected);

    [Theory]
    [InlineData("FINISHED", "Finished Airing")]
    [InlineData("RELEASING", "Currently Airing")]
    [InlineData("NOT_YET_RELEASED", "Not yet aired")]
    [InlineData("HIATUS", "On Hiatus")]
    public void ToAnimeDto_Status_IsTranslated(string aniListStatus, string expected) =>
        AniListMapper.ToAnimeDto(Anime(status: aniListStatus)).Status.Should().Be(expected);

    [Theory]
    [InlineData("MANGA", "Manga")]
    [InlineData("NOVEL", "Light Novel")]
    [InlineData("ONE_SHOT", "One-shot")]
    public void ToMangaDto_Format_IsTranslated(string aniListFormat, string expected) =>
        AniListMapper.ToMangaDto(Manga(format: aniListFormat)).Type.Should().Be(expected);

    [Theory]
    [InlineData("FINISHED", "Finished")]
    [InlineData("RELEASING", "Publishing")]
    public void ToMangaDto_Status_IsTranslated(string aniListStatus, string expected) =>
        AniListMapper.ToMangaDto(Manga(status: aniListStatus)).Status.Should().Be(expected);

    [Fact]
    public void ToAnimeDto_FullDate_MatchesSourceIsoShape()
    {
        var dto = AniListMapper.ToAnimeDto(
            Anime(start: new AniListFuzzyDate { Year = 2016, Month = 1, Day = 14 }));

        dto.Aired!.From.Should().Be("2016-01-14T00:00:00+00:00");
    }

    [Fact]
    public void ToAnimeDto_YearOnlyDate_FallsBackToJanuaryFirst()
    {
        // AniList dates are fuzzy; a year-only date must still parse downstream.
        var dto = AniListMapper.ToAnimeDto(
            Anime(start: new AniListFuzzyDate { Year = 1989 }));

        dto.Aired!.From.Should().Be("1989-01-01T00:00:00+00:00");
    }

    [Fact]
    public void ToAnimeDto_MissingDate_IsNull() =>
        AniListMapper.ToAnimeDto(Anime(start: null)).Aired!.From.Should().BeNull();

    [Fact]
    public void ToAnimeDto_Season_IsLowercasedLikeMyAnimeList() =>
        AniListMapper.ToAnimeDto(Anime(season: "WINTER")).Season.Should().Be("winter");

    [Fact]
    public void ToAnimeDto_Description_HasMarkupRemoved()
    {
        // `asHtml: false` still leaves <br> and entities in AniList descriptions.
        var dto = AniListMapper.ToAnimeDto(
            Anime(description: "First line.<br><br><i>Second</i> line &amp; more."));

        dto.Synopsis.Should().Be("First line.\n\nSecond line & more.");
    }

    [Fact]
    public void ToAnimeDto_Genres_AreEmittedWithoutIdsForNameResolution()
    {
        // AniList genres are bare strings; id 0 tells CatalogService to resolve by name.
        var dto = AniListMapper.ToAnimeDto(Anime(genres: ["Action", "Comedy"]));

        dto.Genres!.Select(g => g.Name).Should().Equal("Action", "Comedy");
        dto.Genres!.Should().AllSatisfy(g => g.MalId.Should().Be(0));
    }

    [Fact]
    public void ToAnimeDto_Title_PrefersRomajiLikeSource()
    {
        var dto = AniListMapper.ToAnimeDto(Anime() with
        {
            Title = new AniListTitle { Romaji = "Kono Subarashii", English = "Konosuba", Native = "この素晴らしい" }
        });

        dto.Title.Should().Be("Kono Subarashii");
        dto.TitleJapanese.Should().Be("この素晴らしい");
    }

    [Fact]
    public void ToStreamingDtos_KeepsOnlyStreamingLinks()
    {
        IReadOnlyList<AniListExternalLink> links =
        [
            new() { Site = "Official Site", Url = "https://x", Type = "INFO" },
            new() { Site = "Crunchyroll", Url = "https://cr", Type = "STREAMING" },
            new() { Site = "Twitter", Url = "https://t", Type = "SOCIAL" },
            new() { Site = "Netflix", Url = "https://nf", Type = "STREAMING" }
        ];

        AniListMapper.ToStreamingDtos(links)
            .Select(s => s.Name).Should().Equal("Crunchyroll", "Netflix");
    }

    [Fact]
    public void ToStreamingDtos_NullLinks_ReturnsEmpty() =>
        AniListMapper.ToStreamingDtos(null).Should().BeEmpty();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AniListMedia Anime(
        int? idMal = 1,
        string title = "Test",
        int? averageScore = null,
        string? format = "TV",
        string? status = "FINISHED",
        string? season = null,
        string? description = null,
        AniListFuzzyDate? start = null,
        IReadOnlyList<string>? genres = null) => new()
        {
            IdMal = idMal,
            Title = new AniListTitle { Romaji = title },
            AverageScore = averageScore,
            Format = format,
            Status = status,
            Season = season,
            Description = description,
            StartDate = start,
            Genres = genres
        };

    private static AniListMedia Manga(
        int? idMal = 1,
        string? format = "MANGA",
        string? status = "FINISHED") => new()
        {
            IdMal = idMal,
            Title = new AniListTitle { Romaji = "Test Manga" },
            Format = format,
            Status = status
        };
}
