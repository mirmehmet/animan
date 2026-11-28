namespace AniMan.Core.Domain.Models;

public class CachedAnime
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TitleJapanese { get; set; }
    public string? Synopsis { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public int? TotalEpisodes { get; set; }
    public string? AiringStart { get; set; }
    public string? AiringEnd { get; set; }
    public string? Season { get; set; }
    public int? Year { get; set; }
    public double? Score { get; set; }
    public int? Rank { get; set; }
    public int? Popularity { get; set; }
    public string? CoverMediumUrl { get; set; }
    public string? CoverLargeUrl { get; set; }
    public string? Studios { get; set; }

    // Extended metadata (MediaDataVersion 2). JSON-array columns hold display names.
    public string? TitleEnglish { get; set; }
    public string? Themes { get; set; }
    public string? Demographics { get; set; }
    public int? DurationMinutes { get; set; }
    public string? AgeRating { get; set; }
    public string? Source { get; set; }
    public int DataVersion { get; set; } = MediaDataVersion.Legacy;

    public DateTime FetchedAt { get; set; }

    public ICollection<CachedEpisode> Episodes { get; set; } = [];
    public ICollection<CachedAnimeStreaming> StreamingPlatforms { get; set; } = [];
}
