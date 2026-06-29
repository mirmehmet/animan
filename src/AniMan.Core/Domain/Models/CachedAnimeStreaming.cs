namespace AniMan.Core.Domain.Models;

public class CachedAnimeStreaming
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string? Url { get; set; }

    public CachedAnime? Anime { get; set; }
}
