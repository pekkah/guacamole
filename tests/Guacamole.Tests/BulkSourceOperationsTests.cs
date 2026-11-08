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

        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
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

        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
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

        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(1);
            batch.Remove(5);
            batch.Replace(2, 2);
            batch.Commit();
        }

        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(7);
            batch.Rollback();
        }

        Assert.Equal(0, notifications);
        Assert.Equal([1, 2], context.Items.Values);
    }

    [Fact]
    public void BatchCommit_FailingOperation_AppliesNothing_RaisesNoNotification_AndIsNotReappliedOnDispose()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2]);
        var total = context.Items.Collect().Select(items => items.Sum());
        Assert.Equal(3, total.Value);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;
        var version = SourceVersion(context);

        IBatchOperation<int> batch = context.ItemsSource.BeginBatch();
        batch.Push(3);
        batch.Remove(1);
        batch.Replace(2, 20);
        batch.Replace(42, 43); // 42 is not present
        batch.Push(4);

        Assert.Throws<InvalidOperationException>(batch.Commit);
        batch.Dispose(); // must not apply the batch again (or throw again)

        Assert.Equal(0, notifications);
        Assert.Equal(version, SourceVersion(context));
        Assert.Equal([1, 2], context.Items.Values);
        Assert.Equal([1, 2], context.ItemsSource.GetSnapshot());
        Assert.Equal(3, total.Value);
    }

    [Fact]
    public void BatchCommit_ReplaceFailingOnlyBecauseOfAnEarlierOperation_AppliesNothing()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2]);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        // Each operation is valid on its own; the Replace fails at its point of the batch
        using (IBatchOperation<int> removeThenReplace = context.ItemsSource.BeginBatch())
        {
            removeThenReplace.Remove(1);
            removeThenReplace.Replace(1, 10); // 1 was removed earlier in the batch
            Assert.Throws<InvalidOperationException>(removeThenReplace.Commit);
        }

        using (IBatchOperation<int> pushThenReplace = context.ItemsSource.BeginBatch())
        {
            pushThenReplace.Push(3);
            pushThenReplace.Replace(2, 3); // 3 was pushed earlier in the batch
            Assert.Throws<InvalidOperationException>(pushThenReplace.Commit);
        }

        Assert.Equal(0, notifications);
        Assert.Equal([1, 2], context.Items.Values);
    }

    [Fact]
    public void BatchCommit_NullItem_AppliesNothing()
    {
        using var context = new ItemsContextOfStrings();
        context.ItemsSource.Push("a");
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        IBatchOperation<string> batch = context.ItemsSource.BeginBatch();
        batch.Push("b");
        batch.Push(null!);

        Assert.Throws<ArgumentNullException>(batch.Commit);

        Assert.Equal(0, notifications);
        Assert.Equal(["a"], context.Items.Values);
    }

    [Fact]
    public void BatchCommit_OperationsThatCancelOut_RaiseNoNotification()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2, 3]);
        var runs = 0;
        var total = context.Items.Collect().Select(items => { runs++; return items.Sum(); });
        Assert.Equal(6, total.Value);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;
        var version = SourceVersion(context);

        // Push then Remove of a new item
        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(4);
            batch.Remove(4);
        }

        // Replace and replace back
        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
        {
            batch.Replace(2, 20);
            batch.Replace(20, 2);
        }

        // Remove and push back the last item (it is appended again, at the same place)
        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
        {
            batch.Remove(3);
            batch.Push(3);
        }

        Assert.Equal(0, notifications);
        Assert.Equal(version, SourceVersion(context));
        Assert.Equal([1, 2, 3], context.Items.Values);
        Assert.Equal(6, total.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void BatchCommit_RandomBatches_MatchOneByOneSemantics_AllOrNothing()
    {
        var random = new Random(70);
        using var context = new ItemsContext();
        var model = new System.Collections.Generic.List<int>();
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        for (var round = 0; round < 2_000; round++)
        {
            // Apply the batch to a copy of the model one operation at a time
            var expected = new System.Collections.Generic.List<int>(model);
            var fails = false;
            IBatchOperation<int> batch = context.ItemsSource.BeginBatch();
            for (var i = random.Next(1, 6); i > 0; i--)
            {
                var item = random.Next(8);
                switch (random.Next(3))
                {
                    case 0:
                        batch.Push(item);
                        if (!expected.Contains(item))
                            expected.Add(item);
                        break;
                    case 1:
                        batch.Remove(item);
                        expected.Remove(item);
                        break;
                    default:
                        var replacement = random.Next(8);
                        batch.Replace(item, replacement);
                        if (item == replacement)
                            break;
                        var index = expected.IndexOf(item);
                        if (index < 0 || expected.Contains(replacement))
                            fails = true;
                        else
                            expected[index] = replacement;
                        break;
                }
            }

            var before = notifications;
            if (fails)
            {
                Assert.ThrowsAny<InvalidOperationException>(batch.Commit);
                expected = model; // nothing applied
            }
            else
            {
                batch.Commit();
            }

            Assert.Equal(expected, context.Items.Values);
            Assert.Equal(expected.SequenceEqual(model) ? before : before + 1, notifications);
            model = expected;
        }
    }

    [Fact]
    public void BatchCommit_SameItemsInAnotherOrder_IsAChange()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([1, 2, 3]);
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
        {
            batch.Remove(1);
            batch.Push(1); // moves 1 to the end
        }

        Assert.Equal(1, notifications);
        Assert.Equal([2, 3, 1], context.Items.Values);
    }

    [Fact]
    public void Batch_OperationsAfterCommit_Throw()
    {
        using var context = new ItemsContext();
        IBatchOperation<int> batch = context.ItemsSource.BeginBatch();
        batch.Push(1);
        batch.Commit();

        Assert.Throws<InvalidOperationException>(() => batch.Push(2));
        Assert.Throws<InvalidOperationException>(() => batch.Remove(1));
        Assert.Throws<InvalidOperationException>(() => batch.Replace(1, 3));
        Assert.Throws<InvalidOperationException>(batch.Commit);
        batch.Rollback(); // nothing pending: no-op
        batch.Dispose();

        Assert.Throws<ObjectDisposedException>(() => batch.Push(2));
        Assert.Equal([1], context.Items.Values);
    }

    [Fact]
    public void Batch_OperationsAfterFailedCommit_Throw_AndRollbackIsANoOp()
    {
        using var context = new ItemsContext();
        IBatchOperation<int> batch = context.ItemsSource.BeginBatch();
        batch.Replace(1, 2); // 1 is not present

        Assert.Throws<InvalidOperationException>(batch.Commit);
        batch.Rollback(); // e.g. in an error handler: does not throw and hide the Commit's exception

        Assert.Throws<InvalidOperationException>(() => batch.Push(2));
        batch.Dispose();
        Assert.Empty(context.Items.Values);
    }

    [Fact]
    public void Batch_CanBeUsedAgainAfterRollback_AndDisposeCommitsTheNewOperations()
    {
        using var context = new ItemsContext();

        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(1);
            batch.Rollback();
            batch.Push(2);
        }

        Assert.Equal([2], context.Items.Values);
    }

    [Fact]
    public void BatchDispose_WithFailingOperation_ThrowsAndAppliesNothing()
    {
        using var context = new ItemsContext();
        context.ItemsSource.Push(1);
        IBatchOperation<int> batch = context.ItemsSource.BeginBatch();
        batch.Push(2);
        batch.Replace(42, 43);

        Assert.Throws<InvalidOperationException>(batch.Dispose);
        batch.Dispose(); // disposed: does nothing

        Assert.Equal([1], context.Items.Values);
        Assert.Throws<ObjectDisposedException>(() => batch.Push(3));
    }

    [Fact]
    public void BatchDispose_CommitsPendingOperationsWithOneNotification()
    {
        using var context = new ItemsContext();
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        using (IBatchOperation<int> batch = context.ItemsSource.BeginBatch())
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
