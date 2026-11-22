using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.MediaSource.Dtos;

public record MangaDto
{
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

    [JsonPropertyName("chapters")]
    public int? Chapters { get; init; }

    [JsonPropertyName("volumes")]
    public int? Volumes { get; init; }

    [JsonPropertyName("published")]
    public DateRangeDto? Published { get; init; }

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

    [JsonPropertyName("serializations")]
    public IReadOnlyList<NamedEntityDto>? Serializations { get; init; }

    [JsonPropertyName("title_english")]
    public string? TitleEnglish { get; init; }

    /// <summary>Hentai, Ecchi, Erotica — MyAnimeList lists these apart from <see cref="Genres"/>.</summary>
    [JsonPropertyName("explicit_genres")]
    public IReadOnlyList<GenreDto>? ExplicitGenres { get; init; }

    [JsonPropertyName("themes")]
    public IReadOnlyList<NamedEntityDto>? Themes { get; init; }

    [JsonPropertyName("demographics")]
    public IReadOnlyList<NamedEntityDto>? Demographics { get; init; }

    /// <summary>Story/art credits; names come as "Last, First".</summary>
    [JsonPropertyName("authors")]
    public IReadOnlyList<NamedEntityDto>? Authors { get; init; }
}
