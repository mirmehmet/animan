namespace AniMan.Core.Domain.Models;

public class ChapterProgress
{
    public int Id { get; set; }
    public int LibraryItemId { get; set; }
    public int ChapterNumber { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    public LibraryItem? LibraryItem { get; set; }
}
