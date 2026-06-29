namespace AniMan.Core.Domain.Models;

public class Note
{
    public int Id { get; set; }
    public int LibraryItemId { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? ChapterNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public LibraryItem? LibraryItem { get; set; }
}
