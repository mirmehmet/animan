namespace AniMan.Core.Common;

/// <summary>
/// One page of a paged list, with the source's own answer to "is there another page?".
/// Guessing from the page size is wrong whenever entries are filtered out of a full page.
/// </summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, bool HasNextPage);
