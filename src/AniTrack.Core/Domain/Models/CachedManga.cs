namespace AniTrack.Core.Domain.Models;

public class CachedManga
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TitleJapanese { get; set; }
    public string? Synopsis { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public int? TotalChapters { get; set; }
    public int? TotalVolumes { get; set; }
    public string? PublishedStart { get; set; }
    public string? PublishedEnd { get; set; }
    public double? Score { get; set; }
    public int? Rank { get; set; }
    public int? Popularity { get; set; }
    public string? CoverMediumUrl { get; set; }
    public string? CoverLargeUrl { get; set; }
    public DateTime FetchedAt { get; set; }

    public ICollection<CachedMediaGenre> MediaGenres { get; set; } = [];
}
