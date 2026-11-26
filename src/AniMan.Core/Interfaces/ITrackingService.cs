using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;

namespace AniMan.Core.Interfaces;

public record ProgressInfo(int Watched, int Total);

public record TrackingResult(bool AutoCompleteNeeded);

/// <summary>
/// A library title with its snapshot and status, but without progress rows: only the number
/// of watched episodes (anime) or read chapters (manga).
/// </summary>
public record LibraryOverviewItem(LibraryItem Item, int ProgressDone);

public interface ITrackingService
{
    /// <summary>Every active title with all of its progress rows. For pages that need the rows (Stats).</summary>
    Task<Result<IReadOnlyList<LibraryItem>>> GetLibraryAsync(MediaType mediaType, CancellationToken ct = default);

    /// <summary>Active titles with snapshot, status and a progress count — the light list for library pages and the dashboard.</summary>
    Task<Result<IReadOnlyList<LibraryOverviewItem>>> GetLibraryOverviewAsync(MediaType mediaType, CancellationToken ct = default);

    /// <summary>(MAL id, media type) of every active title, for "already in library" marks.</summary>
    Task<Result<IReadOnlySet<(int MalId, MediaType MediaType)>>> GetLibraryKeysAsync(CancellationToken ct = default);

    /// <summary>The library id of the active title with this MAL id, or <c>null</c> if it is not in the library.</summary>
    Task<Result<int?>> FindItemIdAsync(int malId, MediaType mediaType, CancellationToken ct = default);

    /// <summary>Every title in the library, the trash included.</summary>
    Task<Result<int>> CountAllItemsAsync(CancellationToken ct = default);

    Task<Result<LibraryItem>> GetItemAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result<TrackingResult>> SetEpisodeWatchedAsync(int libraryItemId, int episodeNumber, bool watched, CancellationToken ct = default);
    Task<Result<TrackingResult>> MarkUpToHereAsync(int libraryItemId, int episodeNumber, CancellationToken ct = default);
    Task<Result<TrackingResult>> SetChapterReadAsync(int libraryItemId, int chapterNumber, bool read, CancellationToken ct = default);
    Task<Result<TrackingResult>> MarkChaptersUpToAsync(int libraryItemId, int chapterNumber, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int libraryItemId, int statusId, CancellationToken ct = default);
    Task<Result> ToggleFavoriteAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result<Note>> SaveNoteAsync(int libraryItemId, int? episodeNumber, int? chapterNumber, string content, CancellationToken ct = default);
    Task<Result<ProgressInfo>> GetProgressAsync(int libraryItemId, CancellationToken ct = default);

    // Rating
    Task<Result> SetRatingAsync(int libraryItemId, decimal? rating, CancellationToken ct = default);

    // Trash
    Task<Result> SoftDeleteAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result> RestoreFromTrashAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result> EmptyTrashAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<LibraryItem>>> GetTrashAsync(CancellationToken ct = default);
    Task PurgeExpiredTrashAsync(CancellationToken ct = default);
}
