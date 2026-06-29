namespace AniMan.Core.Domain.Models;

public class TrackingStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int DisplayOrder { get; set; }
    public string? Color { get; set; }

    public ICollection<LibraryItem> LibraryItems { get; set; } = [];
}
