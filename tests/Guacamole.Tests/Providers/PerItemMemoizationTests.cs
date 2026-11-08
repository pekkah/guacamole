using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.Tests.Providers;

/// <summary>
/// Collection <c>Select</c>, <c>SelectMany</c> and <c>Where</c> remember the result of their lambda
/// per input item, so a recomputation only runs the lambda for new or changed items.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class PerItemMemoizationTests
{
    [ComputingContext]
    private partial class ItemsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    /// <summary>A result type with reference equality only.</summary>
    private sealed class Box(int value)
    {
        public int Value { get; } = value;
    }

    private const int N = 50;

    private static ItemsContext CreateContext()
    {
        var context = new ItemsContext();
        context.ItemsSource.PushRange(Enumerable.Range(1, N));
        return context;
    }

    [Fact]
    public void Select_Push_RunsSelectorOnlyForTheNewItem()
    {
        using var context = CreateContext();
        var calls = 0;
        var doubled = context.Items.Select(x => { Interlocked.Increment(ref calls); return x * 2; });

        Assert.Equal(Enumerable.Range(1, N).Select(x => x * 2), doubled.Values);
        Assert.Equal(N, calls);

        context.ItemsSource.Push(N + 1);
        Assert.Equal(Enumerable.Range(1, N + 1).Select(x => x * 2), doubled.Values);
        Assert.Equal(N + 1, calls);
    }

    [Fact]
    public void Select_ReplaceAndRemove_RunSelectorOnlyForChangedItems()
    {
        using var context = CreateContext();
        var calls = 0;
        var doubled = context.Items.Select(x => { calls++; return x * 2; });
        _ = doubled.Values.ToArray();

        context.ItemsSource.Replace(10, 1000);
        Assert.Equal(2000, doubled.Values.ElementAt(9));
        Assert.Equal(N + 1, calls);

        context.ItemsSource.Remove(20);
        Assert.Equal(N - 1, doubled.Values.Count());
        Assert.Equal(N + 1, calls);

        // 10 was evicted from the memo when it was replaced, so it is computed again.
        context.ItemsSource.Replace(1000, 10);
        Assert.Equal(20, doubled.Values.ElementAt(9));
        Assert.Equal(N + 2, calls);
    }

    [Fact]
    public void Select_UnchangedItems_KeepTheirOutputInstances_SoDownstreamStagesHit()
    {
        using var context = CreateContext();
        var downstreamCalls = 0;
        var boxes = context.Items.Select(x => new Box(x));
        var values = boxes.Select(box => { downstreamCalls++; return box.Value; });

        var before = boxes.Values.ToArray();
        Assert.Equal(Enumerable.Range(1, N), values.Values);
        Assert.Equal(N, downstreamCalls);

        context.ItemsSource.Push(N + 1);
        var after = boxes.Values.ToArray();

        for (var i = 0; i < N; i++)
            Assert.Same(before[i], after[i]);

        Assert.Equal(Enumerable.Range(1, N + 1), values.Values);
        Assert.Equal(N + 1, downstreamCalls);
    }

    [Fact]
    public void SelectMany_Push_RunsSelectorOnlyForTheNewItem()
    {
        using var context = CreateContext();
        var calls = 0;
        var pairs = context.Items.SelectMany(x => { calls++; return new[] { x, -x }; });

        Assert.Equal(2 * N, pairs.Values.Count());
        Assert.Equal(N, calls);

        context.ItemsSource.Push(N + 1);
        Assert.Equal([N + 1, -(N + 1)], pairs.Values.TakeLast(2));
        Assert.Equal(N + 1, calls);

        context.ItemsSource.Replace(1, 100);
        Assert.Equal([100, -100, 2, -2], pairs.Values.Take(4));
        Assert.Equal(N + 2, calls);
    }

    [Fact]
    public void Where_Push_RunsPredicateOnlyForTheNewItem()
    {
        using var context = CreateContext();
        var calls = 0;
        var even = context.Items.Where(x => { calls++; return x % 2 == 0; });

        Assert.Equal(N / 2, even.Values.Count());
        Assert.Equal(N, calls);

        context.ItemsSource.Push(N + 2);
        Assert.Equal(N + 2, even.Values.Last());
        Assert.Equal(N + 1, calls);
    }

    [Fact]
    public void Select_NullItems_AreNotMemoizedButHandled()
    {
        using var context = CreateContext();
        var calls = 0;
        var names = context.Items.Select(x => x % 10 == 0 ? null : x.ToString());
        var lengths = names.Select(s => { calls++; return s?.Length ?? -1; });

        var first = lengths.Values.ToArray();
        Assert.Equal(N, first.Length);
        Assert.Equal(-1, first[9]);
        Assert.Equal(N, calls);

        // The upstream Select records the delta of its output (one inserted item), so the unchanged
        // items, null ones included, keep their results and only the new item runs (#61).
        context.ItemsSource.Push(N + 1);
        Assert.Equal(2, lengths.Values.Last());
        Assert.Equal(N + 1, calls);

        // Without a delta (40 pushes overflow the source's change log), all items are walked: the
        // five null items rerun, the other previous items hit the memo, and the new items run.
        context.ItemsSource.PushRange(Enumerable.Range(N + 2, 40));
        Assert.Equal(2, lengths.Values.Last());
        Assert.Equal(N + 1 + N / 10 + 40, calls);
    }

    [Fact]
    public void Select_SelectorException_IsAnErrorAndKeepsThePreviousMemo()
    {
        using var context = CreateContext();
        var calls = 0;
        var inverse = context.Items.Select(x => { calls++; return 100 / x; });
        _ = inverse.Values.ToArray();
        Assert.Equal(N, calls);

        context.ItemsSource.Push(0);
        var failed = inverse.GetValues();
        Assert.True(failed.IsError);
        Assert.Equal(new DivideByZeroException().Message, failed.ErrorValue!.Message);

        // Removing the bad item restores the result from the memo kept from before the failure.
        context.ItemsSource.Remove(0);
        var callsBefore = calls;
        Assert.Equal(Enumerable.Range(1, N).Select(x => 100 / x), inverse.Values);
        Assert.Equal(callsBefore, calls);
    }

    [Fact]
    public async Task Select_ParallelReads_AreConsistent()
    {
        using var context = CreateContext();
        var calls = 0;
        var doubled = context.Items.Select(x => { Interlocked.Increment(ref calls); return (x, x * 2); });
        _ = doubled.Values.ToArray();

        context.ItemsSource.Push(N + 1);
        var expected = Enumerable.Range(1, N + 1).Select(x => (x, x * 2)).ToArray();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => doubled.Values.ToArray(), TestContext.Current.CancellationToken)));

        foreach (var result in results)
            Assert.Equal(expected, result);

        // Racing readers may each compute the new item, but never the unchanged ones.
        Assert.InRange(calls, N + 1, N + 8);
    }

    [Fact]
    public async Task Select_ConcurrentWritesAndReads_ProduceCorrectItems()
    {
        using var context = CreateContext();
        var doubled = context.Items.Select(x => (x, x * 2));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                context.ItemsSource.Push(1000 + i);
                if (i % 3 == 0)
                    context.ItemsSource.Remove(1000 + i / 2);
            }
        }, cts.Token);

        // Bounded, yielding readers: keep the test from starving other tests running in parallel.
        var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200 && !writer.IsCompleted; i++)
            {
                foreach (var (x, twice) in doubled.Values)
                    Assert.Equal(x * 2, twice);
                Thread.Yield();
            }
        }, cts.Token)).ToArray();

        await writer;
        await Task.WhenAll(readers);

        var expected = context.ItemsSource.GetSnapshot().Select(x => (x, x * 2));
        Assert.Equal(expected, doubled.Values);
    }
}
