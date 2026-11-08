using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Collection sources keep set semantics and enumerate their items in insertion order.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class SourceOrderTests
{
    [ComputingContext]
    private partial class OrderContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Items { get; }
    }

    // Strings whose hash order differs from the push order (string hashes are randomized per process).
    private static readonly string[] ManyItems =
        Enumerable.Range(0, 200).Select(i => $"item-{(i * 7919) % 1000:D4}-{i}").ToArray();

    [Fact]
    public void Push_EnumeratesInInsertionOrder()
    {
        using var context = new OrderContext();
        foreach (var item in ManyItems)
            context.ItemsSource.Push(item);

        Assert.Equal(ManyItems, context.ItemsSource.GetSnapshot());
        Assert.Equal(ManyItems, context.Items.Values);
        Assert.Equal(ManyItems, context.Items.Collect().Value);
        Assert.Equal(ManyItems.Select(s => s.Length), context.Items.Select(s => s.Length).Values);
    }

    [Fact]
    public void PushRange_AndBatch_PreserveOrder()
    {
        using var context = new OrderContext();
        context.ItemsSource.PushRange(ManyItems.Take(100));

        using (IBatchOperation<string> batch = context.ItemsSource.BeginBatch())
        {
            foreach (var item in ManyItems.Skip(100))
                batch.Push(item);
            batch.Commit();
        }

        Assert.Equal(ManyItems, context.Items.Collect().Value);
    }

    [Fact]
    public void Push_OfExistingItem_IsNoOpAndKeepsPosition()
    {
        using var context = new OrderContext();
        var notifications = 0;
        context.ItemsSource.Changed += (_, _) => notifications++;

        context.ItemsSource.PushRange(["a", "b", "c"]);
        context.ItemsSource.Push("a");

        // One notification for the range (#62), none for pushing a present item
        Assert.Equal(1, notifications);
        Assert.Equal(["a", "b", "c"], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void Replace_KeepsPosition()
    {
        using var context = new OrderContext();
        context.ItemsSource.PushRange(["a", "b", "c"]);

        context.ItemsSource.Replace("b", "B");
        context.ItemsSource.Replace("a", "A");

        Assert.Equal(["A", "B", "c"], context.ItemsSource.GetSnapshot());
        Assert.Equal(["A", "B", "c"], context.Items.Values);
    }

    [Fact]
    public void RemoveThenPush_Appends()
    {
        using var context = new OrderContext();
        context.ItemsSource.PushRange(["a", "b", "c"]);

        context.ItemsSource.Remove("a");
        Assert.Equal(["b", "c"], context.ItemsSource.GetSnapshot());

        context.ItemsSource.Push("a");
        Assert.Equal(["b", "c", "a"], context.ItemsSource.GetSnapshot());
        Assert.Equal(["b", "c", "a"], context.Items.Collect().Value);
    }

    [Fact]
    public void Clear_ThenPush_StartsANewOrder()
    {
        using var context = new OrderContext();
        context.ItemsSource.PushRange(["a", "b"]);

        context.ItemsSource.Clear();
        context.ItemsSource.PushRange(["b", "a"]);

        Assert.Equal(["b", "a"], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void Replace_FailureLeavesOrderUnchanged()
    {
        using var context = new OrderContext();
        context.ItemsSource.PushRange(["a", "b"]);

        Assert.Throws<InvalidOperationException>(() => context.ItemsSource.Replace("a", "b"));
        Assert.Throws<InvalidOperationException>(() => context.ItemsSource.Replace("x", "y"));

        Assert.Equal(["a", "b"], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public async Task ConcurrentPushes_KeepEachWritersOrderAndNoDuplicates()
    {
        using var context = new OrderContext();
        const int writers = 4;
        const int perWriter = 250;

        await Task.WhenAll(Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                context.ItemsSource.Push($"w{w}-{i}");
                context.ItemsSource.Push($"w{w}-{i}"); // duplicate: ignored
            }
        }, TestContext.Current.CancellationToken)));

        var snapshot = context.ItemsSource.GetSnapshot().ToArray();
        Assert.Equal(writers * perWriter, snapshot.Length);
        Assert.Equal(snapshot.Length, snapshot.Distinct().Count());

        for (var w = 0; w < writers; w++)
        {
            var prefix = $"w{w}-";
            var own = snapshot.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.Equal(Enumerable.Range(0, perWriter).Select(i => $"{prefix}{i}"), own);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomOperations_KeepInsertionOrder_AcrossCompaction(int seed)
    {
        using var context = new OrderContext();
        var random = new Random(seed);
        var model = new List<string>();
        var values = context.Items.Select(s => s.Length);

        for (var step = 0; step < 2000; step++)
        {
            var item = $"x{random.Next(300)}";
            switch (random.Next(10))
            {
                case < 4:
                    if (!model.Contains(item))
                        model.Add(item);
                    context.ItemsSource.Push(item);
                    break;
                case < 8:
                    // Removals outnumber pushes now and then, so empty slots get compacted
                    if (model.Count > 0)
                    {
                        var removed = model[random.Next(model.Count)];
                        model.Remove(removed);
                        context.ItemsSource.Remove(removed);
                    }

                    break;
                case 8 when model.Count > 0 && !model.Contains(item):
                    {
                        var index = random.Next(model.Count);
                        context.ItemsSource.Replace(model[index], item);
                        model[index] = item;
                        break;
                    }
                case 9 when random.Next(50) == 0:
                    model.Clear();
                    context.ItemsSource.Clear();
                    break;
            }

            if (step % 7 == 0)
            {
                Assert.Equal(model, context.ItemsSource.GetSnapshot());
                Assert.Equal(model.Select(s => s.Length), values.Values);
            }
        }

        Assert.Equal(model, context.Items.Values);
    }

    [Fact]
    public void Snapshot_IsReadOnly()
    {
        using var context = new OrderContext();
        context.ItemsSource.PushRange(["a", "b"]);
        var snapshot = context.ItemsSource.GetSnapshot();

        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot)[0] = "z");
        Assert.Equal(["a", "b"], context.Items.Values);
    }
}
