using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;

namespace AniMan.Core.Interfaces;

public interface ISnapshotService
{
    Task<Result<LibraryItem>> SnapshotAsync(int malId, MediaType mediaType, int statusId, CancellationToken ct = default);
    Task<Result<MediaSnapshot>> ReSnapshotAsync(int libraryItemId, CancellationToken ct = default);
}
