namespace AniMan.Core.Domain;

/// <summary>
/// Well-known ids of the seeded tracking statuses (see LibraryDbContext seed data).
/// Custom user statuses get ids beyond these.
/// </summary>
public static class TrackingStatusIds
{
    public const int Watching = 1;
    public const int Reading = 2;
    public const int Completed = 3;
    public const int OnHold = 4;
    public const int Dropped = 5;
    public const int PlanToWatch = 6;
    public const int PlanToRead = 7;
}
