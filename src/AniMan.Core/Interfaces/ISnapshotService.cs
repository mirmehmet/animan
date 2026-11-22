using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;

namespace AniMan.Core.Interfaces;

public interface ISnapshotService
{
    Task<Result<LibraryItem>> SnapshotAsync(int malId, MediaType mediaType, int statusId, CancellationToken ct = default);
    Task<Result<MediaSnapshot>> ReSnapshotAsync(int libraryItemId, CancellationToken ct = default);

    /// <summary>
    /// Brings a Legacy snapshot up to the current <see cref="Domain.MediaDataVersion"/> by
    /// filling only the extended metadata fields (and genres, which now include explicit
    /// ones). Title, cover, streaming data and every piece of user data are left untouched.
    /// </summary>
    Task<Result> FillExtendedMetadataAsync(int libraryItemId, CancellationToken ct = default);
}
