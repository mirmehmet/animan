using AniTrack.Core.Common;
using AniTrack.Core.Domain.Enums;
using AniTrack.Core.Domain.Models;

namespace AniTrack.Core.Interfaces;

public interface ISnapshotService
{
    Task<Result<LibraryItem>> SnapshotAsync(int malId, MediaType mediaType, int statusId, CancellationToken ct = default);
    Task<Result<MediaSnapshot>> ReSnapshotAsync(int libraryItemId, CancellationToken ct = default);
}
