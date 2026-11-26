namespace AniMan.Core.Common;

/// <summary>
/// Why an operation failed, in terms the UI can explain to the user. The message text of a
/// <see cref="Result"/> is for logs; the UI picks a localized sentence from the kind instead.
/// </summary>
public enum ErrorKind
{
    /// <summary>Anything without a more specific reason.</summary>
    Unexpected = 0,

    /// <summary>No network, DNS failure, or the server could not be reached at all.</summary>
    NoConnection,

    /// <summary>The server did not answer in time.</summary>
    Timeout,

    /// <summary>The data service answered with a server error (5xx).</summary>
    ServiceUnavailable,

    /// <summary>The data service asked us to slow down (429).</summary>
    RateLimited,

    /// <summary>The requested title does not exist at the source (404).</summary>
    NotFound,

    /// <summary>The local library database could not be read or written.</summary>
    LocalData,

    /// <summary>The backup file exceeds the size limit.</summary>
    BackupTooLarge,

    /// <summary>The backup file is empty, corrupt, or unreadable.</summary>
    BackupInvalid,

    /// <summary>The backup was written by a newer AniMan with a schema this version cannot read.</summary>
    BackupTooNew,

    /// <summary>A replace-import was refused because the backup holds no items.</summary>
    BackupEmptyOverwrite,

    /// <summary>The backup file could not be written.</summary>
    ExportFailed
}
