using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DotNetConf.App.ViewModels;

namespace DotNetConf.Core.Tests;

public sealed class CollectionUpdatesTests
{
    [Fact]
    public void NoChangePreservesCollectionItemsAndRaisesNoEvents()
    {
        var first = new object();
        var second = new object();
        var collection = new ObservableCollection<object> { first, second };
        var changes = Observe(collection);
        CollectionUpdates.Apply(collection, new[] { first, second });
        Assert.Empty(changes);
        Assert.Same(first, collection[0]);
        Assert.Same(second, collection[1]);
    }

    [Fact]
    public void ReorderUsesMoveWithoutReplacingRetainedInstances()
    {
        var first = new object();
        var second = new object();
        var third = new object();
        var collection = new ObservableCollection<object> { first, second, third };
        var changes = Observe(collection);
        CollectionUpdates.Apply(collection, new[] { third, first, second });
        Assert.Equal(new[] { third, first, second }, collection);
        Assert.Equal(NotifyCollectionChangedAction.Move, Assert.Single(changes));
    }

    [Fact]
    public void MixedRemovalInsertionAndReorderNeverResetCollection()
    {
        var first = new object();
        var removed = new object();
        var third = new object();
        var added = new object();
        var collection = new ObservableCollection<object> { first, removed, third };
        var changes = Observe(collection);
        CollectionUpdates.Apply(collection, new[] { third, added, first });
        Assert.Equal(new[] { third, added, first }, collection);
        Assert.Contains(NotifyCollectionChangedAction.Remove, changes);
        Assert.Contains(NotifyCollectionChangedAction.Add, changes);
        Assert.Contains(NotifyCollectionChangedAction.Move, changes);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, changes);
    }

    [Fact]
    public void EmptyInputRemovesAllWithoutResetAndEmptyToEmptyRaisesNothing()
    {
        var collection = new ObservableCollection<int> { 1, 2 };
        var changes = Observe(collection);
        CollectionUpdates.Apply(collection, []);
        Assert.Empty(collection);
        Assert.Equal([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove], changes);
        changes.Clear();
        CollectionUpdates.Apply(collection, []);
        Assert.Empty(changes);
    }

    [Fact]
    public void EqualRecordsRetainExistingIdentity()
    {
        var original = new Item("id");
        var collection = new ObservableCollection<Item> { original };
        var changes = Observe(collection);
        CollectionUpdates.Apply(collection, [new Item("id")]);
        Assert.Same(original, Assert.Single(collection));
        Assert.Empty(changes);
    }

    [Fact]
    public void InputCanBeTheTargetCollection()
    {
        var collection = new ObservableCollection<int> { 1, 2, 3 };
        var changes = Observe(collection);
        CollectionUpdates.Apply(collection, collection);
        Assert.Equal(new[] { 1, 2, 3 }, collection);
        Assert.Empty(changes);
    }

    private static List<NotifyCollectionChangedAction> Observe<T>(ObservableCollection<T> collection)
    {
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => changes.Add(args.Action);
        return changes;
    }

    private sealed record Item(string Id);
}
