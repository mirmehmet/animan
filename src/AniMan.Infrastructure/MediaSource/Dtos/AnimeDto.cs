using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.MediaSource.Dtos;

public record AnimeDto
{
    /// <summary>True when built by the fallback source: MAL-only fields are missing, not empty.</summary>
    [JsonIgnore]
    public bool IsPartial { get; init; }

    [JsonPropertyName("mal_id")]
    public int MalId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("title_japanese")]
    public string? TitleJapanese { get; init; }

    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; init; }

    [JsonPropertyName("aired")]
    public DateRangeDto? Aired { get; init; }

    [JsonPropertyName("season")]
    public string? Season { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("score")]
    public double? Score { get; init; }

    [JsonPropertyName("rank")]
    public int? Rank { get; init; }

    [JsonPropertyName("popularity")]
    public int? Popularity { get; init; }

    [JsonPropertyName("images")]
    public ImagesDto? Images { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<GenreDto>? Genres { get; init; }

    [JsonPropertyName("studios")]
    public IReadOnlyList<NamedEntityDto>? Studios { get; init; }

    [JsonPropertyName("title_english")]
    public string? TitleEnglish { get; init; }

    /// <summary>Hentai, Ecchi, Erotica — MyAnimeList lists these apart from <see cref="Genres"/>.</summary>
    [JsonPropertyName("explicit_genres")]
    public IReadOnlyList<GenreDto>? ExplicitGenres { get; init; }

    [JsonPropertyName("themes")]
    public IReadOnlyList<NamedEntityDto>? Themes { get; init; }

    [JsonPropertyName("demographics")]
    public IReadOnlyList<NamedEntityDto>? Demographics { get; init; }

    /// <summary>Free text such as "24 min per ep" or "1 hr 45 min".</summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    /// <summary>Age rating, e.g. "PG-13 - Teens 13 or older".</summary>
    [JsonPropertyName("rating")]
    public string? Rating { get; init; }

    /// <summary>Adaptation source, e.g. "Manga", "Light novel", "Original".</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>
    /// Set by mappers for sources that report duration in whole minutes already (AniList),
    /// so the free-text <see cref="Duration"/> does not have to be faked. Never in JSON.
    /// </summary>
    [JsonIgnore]
    public int? DurationMinutes { get; init; }
}

public record DateRangeDto
{
    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public string? To { get; init; }
}
