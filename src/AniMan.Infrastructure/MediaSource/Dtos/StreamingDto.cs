using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.MediaSource.Dtos;

public record StreamingDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
