using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.Jikan.Dtos;

public record JikanMangaDto
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
    public JikanDateRangeDto? Published { get; init; }

    [JsonPropertyName("score")]
    public double? Score { get; init; }

    [JsonPropertyName("rank")]
    public int? Rank { get; init; }

    [JsonPropertyName("popularity")]
    public int? Popularity { get; init; }

    [JsonPropertyName("images")]
    public JikanImagesDto? Images { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<JikanGenreDto>? Genres { get; init; }

    [JsonPropertyName("serializations")]
    public IReadOnlyList<JikanNamedEntityDto>? Serializations { get; init; }
}
