namespace AniTrack.Core.Domain.Models;

public class UserStreamingOverride
{
    public int Id { get; set; }
    public int LibraryItemId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateTime AddedAt { get; set; }

    public LibraryItem? LibraryItem { get; set; }
}
