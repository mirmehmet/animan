using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.Jikan.Dtos;

public record JikanStreamingDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
