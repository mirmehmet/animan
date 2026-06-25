namespace AniTrack.Core.Domain.Models;

public class CachedEpisode
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Title { get; set; }
    public string? TitleJapanese { get; set; }
    public string? AiredAt { get; set; }
    public int? Duration { get; set; }
    public DateTime FetchedAt { get; set; }

    public CachedAnime? Anime { get; set; }
}
