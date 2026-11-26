using System.Text.Json;
using AniMan.Core.Domain;
using AniMan.Infrastructure.AniList;
using AniMan.Infrastructure.AniList.Dtos;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.MediaSource;
using FluentAssertions;

namespace AniMan.Tests.MediaSource;

public class MediaMapperTests
{
    [Theory]
    [InlineData("24 min per ep", 24)]
    [InlineData("23 min", 23)]
    [InlineData("1 hr 45 min", 105)]
    [InlineData("2 hr", 120)]
    [InlineData("1 hr per ep", 60)]
    [InlineData("45 sec per ep", 1)]
    [InlineData("1 min 30 sec per ep", 2)]
    [InlineData("2 hrs 5 min", 125)]
    public void ParseDurationMinutes_KnownFormats(string input, int expected) =>
        MediaMapper.ParseDurationMinutes(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("0 min")]
    public void ParseDurationMinutes_UnknownOrEmpty_IsNull(string? input) =>
        MediaMapper.ParseDurationMinutes(input).Should().BeNull();

    /// <summary>Trimmed from a live Tenrai /anime/5114/full response (2026-11-22).</summary>
    private const string TenraiAnimeJson = """
        {
          "mal_id": 5114,
          "title": "Fullmetal Alchemist: Brotherhood",
          "title_english": "Fullmetal Alchemist: Brotherhood",
          "title_japanese": "鋼の錬金術師 FULLMETAL ALCHEMIST",
          "type": "TV",
          "source": "Manga",
          "episodes": 64,
          "duration": "24 min per ep",
          "rating": "R - 17+ (violence & profanity)",
          "genres": [ { "mal_id": 1, "name": "Action" }, { "mal_id": 8, "name": "Drama" } ],
          "explicit_genres": [ { "mal_id": 9, "name": "Ecchi" } ],
          "themes": [ { "mal_id": 38, "name": "Military" } ],
          "demographics": [ { "mal_id": 27, "name": "Shounen" } ]
        }
        """;

    [Fact]
    public void ToAnime_ReadsExtendedFieldsFromTenraiJson()
    {
        var dto = JsonSerializer.Deserialize<AnimeDto>(TenraiAnimeJson)!;

        var anime = MediaMapper.ToAnime(dto);

        anime.TitleEnglish.Should().Be("Fullmetal Alchemist: Brotherhood");
        anime.Source.Should().Be("Manga");
        anime.DurationMinutes.Should().Be(24);
        anime.AgeRating.Should().Be("R - 17+ (violence & profanity)");
        anime.Themes.Should().Be("""["Military"]""");
        anime.Demographics.Should().Be("""["Shounen"]""");
        anime.DataVersion.Should().Be(MediaDataVersion.Current);
    }

    [Fact]
    public void ExtractAnimeGenres_IncludesExplicitGenres()
    {
        var dto = JsonSerializer.Deserialize<AnimeDto>(TenraiAnimeJson)!;

        MediaMapper.ExtractAnimeGenres(dto).Select(g => g.Name)
            .Should().Equal("Action", "Drama", "Ecchi");
    }

    [Fact]
    public void ToAnime_SourceWithoutExtendedFields_LeavesThemNull()
    {
        var dto = new AnimeDto { MalId = 1, Title = "Old payload" };

        var anime = MediaMapper.ToAnime(dto);

        anime.TitleEnglish.Should().BeNull();
        anime.Themes.Should().BeNull("missing is not the same as an empty list");
        anime.DurationMinutes.Should().BeNull();
    }

    [Fact]
    public void ToManga_ReadsAuthorsAndEnglishTitle()
    {
        var dto = JsonSerializer.Deserialize<MangaDto>("""
            {
              "mal_id": 2, "title": "Berserk", "title_english": "Berserk",
              "authors": [ { "mal_id": 1868, "name": "Miura, Kentarou" } ],
              "themes": [], "demographics": [ { "mal_id": 42, "name": "Seinen" } ]
            }
            """)!;

        var manga = MediaMapper.ToManga(dto);

        manga.TitleEnglish.Should().Be("Berserk");
        manga.Authors.Should().Be("""["Miura, Kentarou"]""");
        manga.Themes.Should().Be("[]", "an empty list from the source is a real answer");
        manga.Demographics.Should().Be("""["Seinen"]""");
    }

    [Fact]
    public void AniList_ToAnimeDto_MapsEnglishTitleDurationAndSource()
    {
        var media = new AniListMedia
        {
            IdMal = 16498,
            Title = new AniListTitle { Romaji = "Shingeki no Kyojin", English = "Attack on Titan" },
            Duration = 24,
            Source = "LIGHT_NOVEL"
        };

        var anime = MediaMapper.ToAnime(AniListMapper.ToAnimeDto(media));

        anime.Title.Should().Be("Shingeki no Kyojin");
        anime.TitleEnglish.Should().Be("Attack on Titan");
        anime.DurationMinutes.Should().Be(24);
        anime.Source.Should().Be("Light novel", "AniList's enum is translated to MyAnimeList's wording");
        anime.Themes.Should().BeNull("AniList has no equivalent");
    }
}
