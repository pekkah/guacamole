using System;
using System.Collections.Generic;
using System.Linq;
using Guacamole.Tests.Common;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #69: the values of a collection provider are a read-only view of the cached array, which
/// the pipeline shares with downstream stages, <c>Collect()</c> results and source snapshots, so a
/// caller cannot corrupt the cache by casting it to an array and writing to it.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class ReadOnlyValuesTests
{
    [ComputingContext]
    private partial class ItemsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    private static ItemsContext Create(params int[] items)
    {
        var context = new ItemsContext();
        context.ItemsSource.PushRange(items);
        return context;
    }

    [Fact]
    public void Values_OfSourceAndOperators_CannotBeCastToArray()
    {
        using var context = Create(1, 2, 3);
        var doubled = context.Items.Select(x => x * 2);
        var odd = context.Items.Where(x => x % 2 == 1);

        // Cached and freshly computed reads, through Values and GetValues
        foreach (var values in new[]
        {
            context.Items.Values, context.Items.Values, context.Items.GetValues().ValueOrDefault!,
            doubled.Values, doubled.Values, odd.Values, odd.GetValues().ValueOrDefault!,
        })
        {
            Assert.IsNotType<int[]>(values, exactMatch: false);
            Assert.Null(values as int[]);
            Assert.Throws<InvalidCastException>(() => (int[])values);
        }
    }

    [Fact]
    public void Values_MutationThroughAnyInterface_IsRejected()
    {
        using var context = Create(1, 2, 3);
        var values = context.Items.Values;

        var list = Assert.IsAssignableFrom<IList<int>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = 42);
        Assert.Throws<NotSupportedException>(() => list.Add(4));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<int>)values).Remove(1));
        Assert.Throws<NotSupportedException>(() => ((System.Collections.IList)values)[0] = 42);

        Assert.Equal([1, 2, 3], context.Items.Values);
    }

    [Fact]
    public void Values_SharedWithCollectAndSnapshot_StayIntact()
    {
        using var context = Create(1, 2, 3);
        var collected = context.Items.Collect();
        var collectedBefore = collected.Value;
        var snapshot = context.ItemsSource.GetSnapshot();

        // Copying out is the only way to get a writable array, and it does not affect the cache
        var copy = context.Items.Values.ToArray();
        copy[0] = 42;

        Assert.Equal([1, 2, 3], context.Items.Values);
        Assert.Equal([1, 2, 3], collected.Value);
        Assert.Equal([1, 2, 3], collectedBefore);
        Assert.Equal([1, 2, 3], snapshot);
        Assert.Equal(6, context.Items.Select(x => x).Collect().Select(a => a.Sum()).Value);
    }

    [Fact]
    public void Values_StayUnchanged_AfterLaterSourceChanges()
    {
        using var context = Create(1, 2, 3);
        var before = context.Items.Values;

        context.ItemsSource.Push(4);
        context.ItemsSource.Remove(1);

        Assert.Equal([1, 2, 3], before);
        Assert.Equal([2, 3, 4], context.Items.Values);
    }

    [Fact]
    public void OutputHandler_ReceivesReadOnlyValues()
    {
        using var context = new ItemsContext();
        var received = new List<IReadOnlyList<int>>();
        context.RegisterOutput(context.Items, (values, _) => received.Add(values));

        context.ItemsSource.Push(1);
        context.ItemsSource.Push(2);

        Assert.Equal(2, received.Count);
        foreach (var values in received)
        {
            Assert.Null(values as int[]);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)values)[0] = 42);
        }

        Assert.Equal([1], received[0]);
        Assert.Equal([1, 2], received[1]);
    }

    [Fact]
    public void Values_CachedRead_ReturnsSameViewWithoutAllocating()
    {
        using var context = Create(1, 2, 3);
        var doubled = context.Items.Select(x => x * 2);
        var first = doubled.Values;

        Assert.Same(first, doubled.Values);
        Assert.Same(context.Items.Values, context.Items.Values);

        IReadOnlyList<int>? read = null;
        var allocated = ScalingAssert.AllocatedBytes(() =>
        {
            for (var i = 0; i < 100; i++)
                read = doubled.Values;
        });

        Assert.Equal(0, allocated);
        Assert.Same(first, read);
    }

    [Fact]
    public void Values_OfEmptyCollection_AreReadOnly()
    {
        using var context = new ItemsContext();

        var values = context.Items.Values;

        Assert.Empty(values);
        Assert.Null(values as int[]);
    }

    [Fact]
    public void Values_AreIndexableReadOnlyLists()
    {
        using var context = Create(10, 20, 30);
        var doubled = context.Items.Select(x => x * 2);

        IReadOnlyList<int> values = doubled.Values;
        IReadOnlyList<int> viaResult = doubled.GetValues().ValueOrDefault!;

        Assert.Equal(3, values.Count);
        Assert.Equal(20, values[0]);
        Assert.Equal(60, values[2]);
        Assert.Same(values, viaResult);
    }

    [Fact]
    public void OutputHandler_ReceivesIndexableList()
    {
        using var context = new ItemsContext();
        var counts = new List<int>();
        var lasts = new List<int>();
        context.RegisterOutput(context.Items, (IReadOnlyList<int> values, ComputingContextBase _) =>
        {
            counts.Add(values.Count);
            lasts.Add(values[values.Count - 1]);
        });

        context.ItemsSource.Push(1);
        context.ItemsSource.Push(2);

        Assert.Equal([1, 2], counts);
        Assert.Equal([1, 2], lasts);
    }

    [Fact]
    public void GetValues_CachedRead_DoesNotAllocate()
    {
        using var context = Create(1, 2, 3);
        var doubled = context.Items.Select(x => x * 2);
        var first = doubled.GetValues().ValueOrDefault;

        IReadOnlyList<int>? read = null;
        var allocated = ScalingAssert.AllocatedBytes(() =>
        {
            for (var i = 0; i < 100; i++)
                read = doubled.GetValues().ValueOrDefault;
        });

        Assert.Equal(0, allocated);
        Assert.Same(first, read);
    }
}
