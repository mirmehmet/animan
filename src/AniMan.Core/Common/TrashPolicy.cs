namespace AniMan.Core.Common;

/// <summary>How long a title stays in the trash before it is deleted for good.</summary>
public static class TrashPolicy
{
    public const int RetentionDays = 30;

    /// <summary>Whole days left before an item deleted at <paramref name="deletedAtUtc"/> is purged (never negative).</summary>
    public static int DaysLeft(DateTime deletedAtUtc, DateTime nowUtc) =>
        Math.Max(0, RetentionDays - (int)(nowUtc - deletedAtUtc).TotalDays);
}
