using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AniMan.Core.Common;
using FluentAssertions;

namespace AniMan.Tests.Common;

public class CollectionSyncTests
{
    private sealed record Card(string Name);

    private static readonly Card A = new("A"), B = new("B"), C = new("C"), D = new("D"), E = new("E");

    private static (ObservableCollection<Card> List, List<NotifyCollectionChangedAction> Events) Track(params Card[] items)
    {
        var list = new ObservableCollection<Card>(items);
        var events = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => events.Add(e.Action);
        return (list, events);
    }

    [Fact]
    public void NarrowingKeepsTheSurvivors_OnlyRemoving()
    {
        var (list, events) = Track(A, B, C, D, E);

        CollectionSync.TryApply(list, [B, D], maxChanges: 10).Should().BeTrue();

        list.Should().Equal(B, D);
        events.Should().OnlyContain(a => a == NotifyCollectionChangedAction.Remove,
            "typing in the search box must not rebuild the cards that stay");
    }

    [Fact]
    public void WideningInsertsTheReturningItemsInPlace()
    {
        var (list, events) = Track(B, D);

        CollectionSync.TryApply(list, [A, B, C, D, E], maxChanges: 10).Should().BeTrue();

        list.Should().Equal(A, B, C, D, E);
        events.Should().HaveCount(3).And.OnlyContain(a => a == NotifyCollectionChangedAction.Add);
    }

    [Fact]
    public void ReorderingWithinTheLimit_MovesItems()
    {
        var (list, _) = Track(A, B, C);

        CollectionSync.TryApply(list, [C, A, B], maxChanges: 10).Should().BeTrue();

        list.Should().Equal(C, A, B);
    }

    [Fact]
    public void TooManyChanges_LeavesTheListUntouched_ForTheCallerToReplace()
    {
        var (list, events) = Track(A, B, C, D, E);

        CollectionSync.TryApply(list, [E, D, C, B, A], maxChanges: 2).Should().BeFalse();

        list.Should().Equal(A, B, C, D, E);
        events.Should().BeEmpty();
    }

    [Fact]
    public void ComparesByReference_NotByValue()
    {
        var copyOfA = new Card("A"); // equal record, different instance
        var (list, _) = Track(A);

        CollectionSync.TryApply(list, [copyOfA], maxChanges: 10).Should().BeTrue();

        list.Single().Should().BeSameAs(copyOfA);
    }
}
