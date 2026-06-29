using System.Text.Json.Serialization;

namespace AniTrack.Infrastructure.Jikan.Dtos;

public record JikanAnimeDto
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

    [JsonPropertyName("episodes")]
    public int? Episodes { get; init; }

    [JsonPropertyName("aired")]
    public JikanDateRangeDto? Aired { get; init; }

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
    public JikanImagesDto? Images { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<JikanGenreDto>? Genres { get; init; }

    [JsonPropertyName("studios")]
    public IReadOnlyList<JikanNamedEntityDto>? Studios { get; init; }
}

public record JikanDateRangeDto
{
    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public string? To { get; init; }
}
