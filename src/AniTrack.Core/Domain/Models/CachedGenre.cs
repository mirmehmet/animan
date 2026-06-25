namespace AniTrack.Core.Domain.Models;

public class CachedGenre
{
    public int Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ICollection<CachedMediaGenre> MediaGenres { get; set; } = [];
}
