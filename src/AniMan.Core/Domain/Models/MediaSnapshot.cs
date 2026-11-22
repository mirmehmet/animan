namespace AniMan.Core.Domain.Models;

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

    // Extended metadata (MediaDataVersion 2). All nullable: snapshots from older versions
    // and older exports simply lack them until the background backfill fills them in.
    // JSON-array columns hold display names, like Genres.
    public string? TitleEnglish { get; set; }
    public string? Themes { get; set; }
    public string? Demographics { get; set; }
    public int? DurationMinutes { get; set; }
    public string? AgeRating { get; set; }
    public string? Source { get; set; }
    public string? Authors { get; set; }

    /// <summary>See <see cref="MediaDataVersion"/>. Below Current means "extended fields not filled yet".</summary>
    public int DataVersion { get; set; } = MediaDataVersion.Legacy;

    public LibraryItem? LibraryItem { get; set; }
}
