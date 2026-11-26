namespace AniMan.Core.Interfaces;

/// <summary>
/// A number that grows whenever library data (titles, progress, notes, trash…) is written —
/// app settings excluded. Pages remember the value they loaded at and skip a reload while it
/// is unchanged.
/// </summary>
public interface ILibraryVersion
{
    long Current { get; }
}
