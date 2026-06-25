using AniTrack.Core.Common;
using AniTrack.Core.Domain.Models;

namespace AniTrack.Core.Interfaces;

public record ProgressInfo(int Watched, int Total);

public record TrackingResult(bool AutoCompleteNeeded);

public interface ITrackingService
{
    Task<Result<IReadOnlyList<LibraryItem>>> GetLibraryAsync(string mediaType, CancellationToken ct = default);
    Task<Result<LibraryItem>> GetItemAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result<TrackingResult>> ToggleEpisodeAsync(int libraryItemId, int episodeNumber, CancellationToken ct = default);
    Task<Result<TrackingResult>> MarkUpToHereAsync(int libraryItemId, int episodeNumber, CancellationToken ct = default);
    Task<Result<TrackingResult>> ToggleChapterAsync(int libraryItemId, int chapterNumber, CancellationToken ct = default);
    Task<Result<TrackingResult>> MarkChaptersUpToAsync(int libraryItemId, int chapterNumber, CancellationToken ct = default);
    Task<Result> UpdateScoreAsync(int libraryItemId, int? score, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int libraryItemId, int statusId, CancellationToken ct = default);
    Task<Result> ToggleFavoriteAsync(int libraryItemId, CancellationToken ct = default);
    Task<Result> IncrementRewatchAsync(int libraryItemId, DateOnly? date, CancellationToken ct = default);
    Task<Result<Note>> SaveNoteAsync(int libraryItemId, int? episodeNumber, int? chapterNumber, string content, CancellationToken ct = default);
    Task<Result<ProgressInfo>> GetProgressAsync(int libraryItemId, CancellationToken ct = default);
}
