namespace AniTrack.Core.Domain.Models;

public class CachedMediaGenre
{
    public int MediaId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public int GenreId { get; set; }

    public CachedGenre? Genre { get; set; }
}
