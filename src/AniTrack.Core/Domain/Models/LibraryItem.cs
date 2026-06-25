using AniTrack.Core.Domain.Enums;

namespace AniTrack.Core.Domain.Models;

public class LibraryItem
{
    public int Id { get; set; }
    public int MalId { get; set; }
    public MediaType MediaType { get; set; }
    public int StatusId { get; set; }
    public int? Score { get; set; }
    public bool IsFavorite { get; set; }
    public int RewatchCount { get; set; }
    public DateOnly? LastRewatchDate { get; set; }
    public DateOnly? StartedAt { get; set; }
    public DateOnly? CompletedAt { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public TrackingStatus? Status { get; set; }
    public MediaSnapshot? Snapshot { get; set; }
    public ICollection<EpisodeProgress> EpisodeProgress { get; set; } = [];
    public ICollection<ChapterProgress> ChapterProgress { get; set; } = [];
    public ICollection<Note> Notes { get; set; } = [];
    public ICollection<UserStreamingOverride> StreamingOverrides { get; set; } = [];
}
