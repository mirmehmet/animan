namespace AniMan.Core.Domain.Models;

public class EpisodeProgress
{
    public int Id { get; set; }
    public int LibraryItemId { get; set; }
    public int EpisodeNumber { get; set; }
    public bool IsWatched { get; set; }
    public DateTime? WatchedAt { get; set; }

    public LibraryItem? LibraryItem { get; set; }
}
