using AniMan.Core.Common;
using AniMan.Core.Domain.Models;

namespace AniMan.Core.Interfaces;

public record ProgressInfo(int Watched, int Total);

public record TrackingResult(bool AutoCompleteNeeded);

public interface ITrackingService
{
    Task<Result<IReadOnlyList<LibraryItem>>> GetLibraryAsync(string mediaType, CancellationToken ct = default);
    Task<Result<LibraryItem>> GetItemAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result<TrackingResult>> ToggleEpisodeAsync(int libraryItemId, int episodeNumber, CancellationToken ct = default);
    Task<Result<TrackingResult>> SetEpisodeWatchedAsync(int libraryItemId, int episodeNumber, bool watched, CancellationToken ct = default);
    Task<Result<TrackingResult>> MarkUpToHereAsync(int libraryItemId, int episodeNumber, CancellationToken ct = default);
    Task<Result<TrackingResult>> ToggleChapterAsync(int libraryItemId, int chapterNumber, CancellationToken ct = default);
    Task<Result<TrackingResult>> SetChapterReadAsync(int libraryItemId, int chapterNumber, bool read, CancellationToken ct = default);
    Task<Result<TrackingResult>> MarkChaptersUpToAsync(int libraryItemId, int chapterNumber, CancellationToken ct = default);
    Task<Result> UpdateScoreAsync(int libraryItemId, int? score, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int libraryItemId, int statusId, CancellationToken ct = default);
    Task<Result> ToggleFavoriteAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result> IncrementRewatchAsync(int libraryItemId, DateOnly? date, CancellationToken ct = default);
    Task<Result<Note>> SaveNoteAsync(int libraryItemId, int? episodeNumber, int? chapterNumber, string content, CancellationToken ct = default);
    Task<Result<ProgressInfo>> GetProgressAsync(int libraryItemId, CancellationToken ct = default);

    // Rating
    Task<Result> SetRatingAsync(int libraryItemId, decimal? rating, CancellationToken ct = default);

    // Trash
    Task<Result> SoftDeleteAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result> RestoreFromTrashAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result> PermanentDeleteAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result> EmptyTrashAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<LibraryItem>>> GetTrashAsync(CancellationToken ct = default);
    Task PurgeExpiredTrashAsync(CancellationToken ct = default);
}
