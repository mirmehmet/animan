using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.Jikan.Dtos;

public record JikanImagesDto
{
    [JsonPropertyName("jpg")]
    public JikanImageVariantsDto? Jpg { get; init; }
}

public record JikanImageVariantsDto
{
    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("medium_image_url")]
    public string? MediumImageUrl { get; init; }

    [JsonPropertyName("large_image_url")]
    public string? LargeImageUrl { get; init; }
}

public record JikanGenreDto
{
    [JsonPropertyName("mal_id")]
    public int MalId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public record JikanNamedEntityDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public record JikanPaginationDto
{
    [JsonPropertyName("last_visible_page")]
    public int LastVisiblePage { get; init; }

    [JsonPropertyName("has_next_page")]
    public bool HasNextPage { get; init; }
}

public record JikanSingleResult<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }
}

public record JikanPagedResult<T>
{
    [JsonPropertyName("data")]
    public IReadOnlyList<T>? Data { get; init; }

    [JsonPropertyName("pagination")]
    public JikanPaginationDto? Pagination { get; init; }
}
