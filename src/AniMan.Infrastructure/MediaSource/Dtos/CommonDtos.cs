using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.MediaSource.Dtos;

public record ImagesDto
{
    [JsonPropertyName("jpg")]
    public ImageVariantsDto? Jpg { get; init; }
}

public record ImageVariantsDto
{
    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("large_image_url")]
    public string? LargeImageUrl { get; init; }
}

public record GenreDto
{
    [JsonPropertyName("mal_id")]
    public int MalId { get; init; }

    // Nullable: a JSON null would otherwise slip into a non-nullable string (AM-038).
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public record NamedEntityDto
{
    // Nullable: a JSON null would otherwise slip into a non-nullable string (AM-038).
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public record PaginationDto
{
    [JsonPropertyName("has_next_page")]
    public bool HasNextPage { get; init; }
}

public record SingleResult<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }
}

public record PagedResult<T>
{
    [JsonPropertyName("data")]
    public IReadOnlyList<T>? Data { get; init; }

    [JsonPropertyName("pagination")]
    public PaginationDto? Pagination { get; init; }
}
