using System.Collections.ObjectModel;

namespace AniMan.Core.Common;

/// <summary>
/// Brings an <see cref="ObservableCollection{T}"/> to a new contents and order with as few
/// removes, moves and inserts as possible, so a bound list keeps the containers of the items
/// that stay. Items are compared by reference.
/// </summary>
public static class CollectionSync
{
    /// <summary>
    /// Updates <paramref name="target"/> in place to equal <paramref name="desired"/>.
    /// Returns <c>false</c> without touching it when more than <paramref name="maxChanges"/>
    /// moves and inserts would be needed (a re-sort, say); one fresh collection is then
    /// cheaper than hundreds of change notifications, and the caller should replace it.
    /// </summary>
    public static bool TryApply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, int maxChanges)
        where T : class
    {
        var keep = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);

        // Plan first: after the removals, how many items would sit out of place?
        var survivors = target.Where(keep.Contains).ToList();
        var changes = desired.Count - CountAlreadyInOrder(survivors, desired);
        if (changes > maxChanges)
            return false;

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
                continue;

            var from = IndexOf(target, desired[i], i + 1);
            if (from >= 0)
                target.Move(from, i);
            else
                target.Insert(i, desired[i]);
        }

        return true;
    }

    /// <summary>
    /// Counts the survivors that already stand in the same relative order as in
    /// <paramref name="desired"/> (a greedy walk through both). Every other desired item costs
    /// one move or insert. Narrowing a search keeps the order, so it costs nothing beyond the
    /// removals; widening it costs one insert per returning item; a new sort costs nearly all.
    /// </summary>
    private static int CountAlreadyInOrder<T>(List<T> survivors, IReadOnlyList<T> desired) where T : class
    {
        int s = 0, matched = 0;
        foreach (var item in desired)
        {
            if (s < survivors.Count && ReferenceEquals(survivors[s], item))
            {
                matched++;
                s++;
            }
        }
        return matched;
    }

    private static int IndexOf<T>(ObservableCollection<T> list, T item, int start) where T : class
    {
        for (var i = start; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return i;
        }
        return -1;
    }
}
