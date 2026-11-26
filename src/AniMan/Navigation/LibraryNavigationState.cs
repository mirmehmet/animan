using AniMan.Core.Domain.Enums;

namespace AniMan.Navigation;

/// <summary>
/// A filter another page wants the library to open with (e.g. clicking "Action" on the
/// Stats page). 0 / null fields mean "leave unrestricted".
/// </summary>
public sealed record LibraryFilterRequest(
    MediaType MediaType, int StatusId = 0, string? Genre = null, int YearFrom = 0, int YearTo = 0);

public static class LibraryNavigationState
{
    /// <summary>Set before navigating to a library page; consumed once by that page.</summary>
    public static LibraryFilterRequest? PendingFilter { get; set; }

    /// <summary>Returns and clears the pending filter if it targets <paramref name="mediaType"/>.</summary>
    public static LibraryFilterRequest? TakeFilter(MediaType mediaType)
    {
        if (PendingFilter is not { } request || request.MediaType != mediaType)
            return null;
        PendingFilter = null;
        return request;
    }
}
