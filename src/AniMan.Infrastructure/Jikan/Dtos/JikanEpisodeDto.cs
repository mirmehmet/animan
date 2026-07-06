using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.Jikan.Dtos;

public record JikanEpisodeDto
{
    [JsonPropertyName("mal_id")]
    public int MalId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("aired")]
    public string? Aired { get; init; }
}
