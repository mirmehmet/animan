using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.MediaSource.Dtos;

public record AnimeDto
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
}

public record DateRangeDto
{
    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public string? To { get; init; }
}
