using System;
using System.Linq;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Bulk source operations apply all their changes with a single change notification, so the
/// source's version moves once and downstream stages recompute once (#62 PushRange, #57 batches).
/// </summary>
public partial class BulkSourceOperationsTests
{
    [ComputingContext]
    private partial class ItemsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    private static long SourceVersion(ItemsContext context) =>
        context.Graph.Versions.GetOutputVersion(context.Items.Node.Id);

    [Fact]
    public void PushRange_RaisesOneNotification_AndOneVersionChange()
    {
        using var context = new ItemsContext();
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;
        var outputs = 0;
        context.RegisterOutput(context.Items.Collect(), (_, _) => outputs++);
        var version = SourceVersion(context);

        context.ItemsSource.PushRange(Enumerable.Range(0, 1000));

        Assert.Equal(1, notifications);
        Assert.Equal(1, outputs);
        Assert.Equal(version + 1, SourceVersion(context));
        Assert.Equal(Enumerable.Range(0, 1000), context.Items.Values);
    }

    [Fact]
    public void PushRange_SkipsPresentAndRepeatedItems_AndKeepsOrder()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([3, 1]);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        context.ItemsSource.PushRange([2, 1, 4, 2, 3]);

        Assert.Equal([3, 1, 2, 4], context.Items.Values);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void PushRange_OfPresentItemsOnly_RaisesNoNotification()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2]);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        context.ItemsSource.PushRange([2, 1]);
        context.ItemsSource.PushRange([]);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void PushRange_WithANullItem_AddsNothing()
    {
        using var context = new ItemsContextOfStrings();
        context.ItemsSource.Push("a");

        Assert.Throws<ArgumentNullException>(() => context.ItemsSource.PushRange(["b", null!, "c"]));
        Assert.Equal(["a"], context.Items.Values);
    }

    [Fact]
    public void PushRange_RecomputesDownstreamOnce()
    {
        using var context = new ItemsContext();
        context.ItemsSource.Push(0);
        var sums = 0;
        var total = context.Items.Collect().Select(items => { sums++; return items.Sum(); });
        Assert.Equal(0, total.Value);

        context.ItemsSource.PushRange(Enumerable.Range(1, 100));

        Assert.Equal(5050, total.Value);
        Assert.Equal(2, sums);
    }

    [Fact]
    public void BatchCommit_OfManyOperations_RaisesOneNotification_OneVersionChange_AndOneRecompute()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange(Enumerable.Range(0, 10));
        var sums = 0;
        var total = context.Items.Collect().Select(items => { sums++; return items.Sum(); });
        Assert.Equal(45, total.Value);

        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;
        var outputs = 0;
        context.RegisterOutput(total, (_, _) => outputs++);
        var version = SourceVersion(context);

        using (var batch = context.ItemsSource.BeginBatch())
        {
            for (var i = 10; i < 60; i++)
                batch.Push(i);
            batch.Remove(0);
            batch.Replace(1, 100);
            batch.Commit();
        }

        Assert.Equal(1, notifications);
        Assert.Equal(version + 1, SourceVersion(context));
        Assert.Equal(1, outputs);
        Assert.Equal(Enumerable.Range(2, 58).Sum() + 100, total.Value);
        Assert.Equal(2, sums);
    }

    [Fact]
    public void BatchCommit_AppliesOperationsInOrder()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2, 3]);

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Remove(1);
            batch.Push(1);      // moves 1 to the end
            batch.Replace(2, 20); // keeps 2's position
            batch.Push(4);
            batch.Replace(4, 40); // replaces an item pushed earlier in the batch
            batch.Push(3);      // present: no-op
            batch.Remove(99);   // missing: no-op
        }

        Assert.Equal([20, 3, 1, 40], context.Items.Values);
    }

    [Fact]
    public void BatchCommit_WithoutEffectiveChanges_RaisesNoNotification()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2]);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(1);
            batch.Remove(5);
            batch.Replace(2, 2);
            batch.Commit();
        }

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(7);
            batch.Rollback();
        }

        Assert.Equal(0, notifications);
        Assert.Equal([1, 2], context.Items.Values);
    }

    [Fact]
    public void BatchCommit_FailingOperation_KeepsEarlierOperations_NotifiesOnce_AndIsNotReappliedOnDispose()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2]);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        var batch = context.ItemsSource.BeginBatch();
        batch.Push(3);
        batch.Replace(42, 43); // 42 is not present
        batch.Push(4);

        Assert.Throws<InvalidOperationException>(batch.Commit);
        batch.Dispose(); // must not apply the batch again (or throw again)

        Assert.Equal(1, notifications);
        Assert.Equal([1, 2, 3], context.Items.Values);
    }

    [Fact]
    public void BatchDispose_CommitsPendingOperationsWithOneNotification()
    {
        using var context = new ItemsContext();
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(1);
            batch.Push(2);
            batch.Push(3);
        }

        Assert.Equal(1, notifications);
        Assert.Equal([1, 2, 3], context.Items.Values);
    }

    [ComputingContext]
    private partial class ItemsContextOfStrings : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Items { get; }
    }
}
