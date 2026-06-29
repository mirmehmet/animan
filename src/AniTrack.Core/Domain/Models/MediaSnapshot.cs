namespace AniTrack.Core.Domain.Models;

public class MediaSnapshot
{
    public int LibraryItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TitleJapanese { get; set; }
    public string? Synopsis { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Studio { get; set; }
    public int? TotalEpisodes { get; set; }
    public int? TotalChapters { get; set; }
    public int? TotalVolumes { get; set; }
    public string? AiringStart { get; set; }
    public string? AiringEnd { get; set; }
    public string? Season { get; set; }
    public int? Year { get; set; }
    public double? MalScore { get; set; }
    public string Genres { get; set; } = "[]";
    public string? CoverLocalPath { get; set; }
    public string? CoverOriginalUrl { get; set; }
    public string? StreamingPlatforms { get; set; }
    public DateTime SnapshotAt { get; set; }

    public LibraryItem? LibraryItem { get; set; }
}
