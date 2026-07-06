namespace AniMan.Infrastructure.Services;

/// <summary>
/// Portable, version-tagged JSON representation of the user's library
/// (<c>library.db</c>). Database identity columns are intentionally omitted —
/// relationships are expressed by nesting, and IDs are regenerated on import.
/// </summary>
internal sealed class LibraryBackup
{
    public int SchemaVersion { get; set; }
    public DateTime ExportedAt { get; set; }
    public List<StatusDto> Statuses { get; set; } = [];
    public List<LibraryItemDto> Items { get; set; } = [];
}

internal sealed record StatusDto(string Name, bool IsDefault, int DisplayOrder, string? Color);

internal sealed class LibraryItemDto
{
    public int MalId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public int? Score { get; set; }
    public decimal? UserRating { get; set; }
    public bool IsFavorite { get; set; }
    public int RewatchCount { get; set; }
    public DateOnly? LastRewatchDate { get; set; }
    public DateOnly? StartedAt { get; set; }
    public DateOnly? CompletedAt { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public SnapshotDto? Snapshot { get; set; }
    public List<EpisodeDto> Episodes { get; set; } = [];
    public List<ChapterDto> Chapters { get; set; } = [];
    public List<NoteDto> Notes { get; set; } = [];
    public List<OverrideDto> StreamingOverrides { get; set; } = [];
}

internal sealed record SnapshotDto(
    string Title, string? TitleJapanese, string? Synopsis, string? Type,
    string? Status, string? Studio,
    int? TotalEpisodes, int? TotalChapters, int? TotalVolumes,
    string? AiringStart, string? AiringEnd, string? Season, int? Year,
    double? MalScore, string Genres, string? CoverLocalPath,
    string? CoverOriginalUrl, string? StreamingPlatforms, DateTime SnapshotAt);

internal sealed record EpisodeDto(int EpisodeNumber, bool IsWatched, DateTime? WatchedAt);

internal sealed record ChapterDto(int ChapterNumber, bool IsRead, DateTime? ReadAt);

internal sealed record NoteDto(
    int? EpisodeNumber, int? ChapterNumber, string Content, DateTime CreatedAt, DateTime UpdatedAt);

internal sealed record OverrideDto(string PlatformName, string? Url, DateTime AddedAt);
