using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Output registrations under concurrent and re-entrant source changes (#92): each handler is called
/// one call at a time, never with the same value twice in a row, and once all changes have returned,
/// its last delivered value is the provider's current value.
/// </summary>
public partial class OutputRegistrationConcurrencyTests
{
    [ComputingContext]
    private partial class ConcurrencyContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }

        public partial IncrementalValuesProvider<int> Items { get; }
    }

    /// <summary>
    /// Records a handler's deliveries and checks that its calls never overlap.
    /// </summary>
    private sealed class Recorder<T>
    {
        private int _active;
        private int _maxActive;

        public List<T> Delivered { get; } = new();

        public int MaxConcurrentCalls => Volatile.Read(ref _maxActive);

        public void Record(T value)
        {
            var active = Interlocked.Increment(ref _active);
            InterlockedMax(ref _maxActive, active);
            try
            {
                // Unsynchronized on purpose: calls of one handler are serialized
                Delivered.Add(value);
                Thread.SpinWait(50); // widen the window for overlapping calls
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current)
                    return;
                current = seen;
            }
        }
    }

    private static void AssertNoConsecutiveDuplicates<T>(IReadOnlyList<T> delivered, Func<T, T, bool> equal)
    {
        for (var i = 1; i < delivered.Count; i++)
        {
            Assert.False(equal(delivered[i - 1], delivered[i]), $"Delivery {i} repeats the previous value");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ConcurrentWriters_ValueProvider_LastDeliveredIsCurrent_NoDuplicates_NoOverlap(int writers)
    {
        const int changesPerWriter = 2_000;
        using var context = new ConcurrencyContext();
        context.NumberSource.Set(-1);

        // Few distinct values, so that most changes recompute an equal value (duplicates would show)
        var bucket = context.Number.Select(static x => x % 3);
        var exact = context.Number.Select(static x => x);
        var buckets = new Recorder<int>();
        var values = new Recorder<int>();
        context.RegisterOutput(bucket, (value, _) => buckets.Record(value));
        context.RegisterOutput(exact, (value, _) => values.Record(value));

        using var start = new Barrier(writers);
        var threads = Enumerable.Range(0, writers).Select(writer => new Thread(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < changesPerWriter; i++)
            {
                context.NumberSource.Set(writer * changesPerWriter + i);
            }
        })).ToArray();
        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        Assert.Equal(1, buckets.MaxConcurrentCalls);
        Assert.Equal(1, values.MaxConcurrentCalls);
        Assert.Equal(bucket.Value, buckets.Delivered[^1]);
        Assert.Equal(exact.Value, values.Delivered[^1]);
        Assert.Equal(context.NumberSource.GetValue(), values.Delivered[^1]);
        AssertNoConsecutiveDuplicates(buckets.Delivered, static (x, y) => x == y);
        AssertNoConsecutiveDuplicates(values.Delivered, static (x, y) => x == y);
    }

    [Fact]
    public void ConcurrentWriters_CollectionProvider_LastDeliveredIsCurrent_NoDuplicates_NoOverlap()
    {
        const int writers = 6;
        const int changesPerWriter = 1_000;
        using var context = new ConcurrencyContext();
        var parities = context.Items.Select(static x => x % 2).Collect();
        var recorder = new Recorder<int[]>();
        context.RegisterOutput(parities, (value, _) => recorder.Record(value.ToArray()));

        using var start = new Barrier(writers);
        var threads = Enumerable.Range(0, writers).Select(writer => new Thread(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < changesPerWriter; i++)
            {
                // Each writer toggles its own item between an even and an odd value
                var item = writer * 2 * changesPerWriter + i;
                context.ItemsSource.Push(item);
                context.ItemsSource.Remove(item);
            }

            context.ItemsSource.Push(writer);
        })).ToArray();
        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        Assert.Equal(1, recorder.MaxConcurrentCalls);
        Assert.Equal(parities.Value.ToArray(), recorder.Delivered[^1]);
        AssertNoConsecutiveDuplicates(recorder.Delivered, static (x, y) => x.SequenceEqual(y));
    }

    [Fact]
    public void HandlerThatChangesASource_IsCalledAgainAfterItReturns_NotNested()
    {
        using var context = new ConcurrencyContext();
        var recorder = new Recorder<int>();
        var depth = 0;
        var maxDepth = 0;
        context.RegisterOutput(context.Number, (value, ctx) =>
        {
            maxDepth = Math.Max(maxDepth, ++depth);
            recorder.Record(value);
            if (value < 5)
                context.NumberSource.Set(value + 1); // re-entrant trigger
            depth--;
        });

        context.NumberSource.Set(0);

        // Every value was delivered in order, each after the previous call returned, before Set returned
        Assert.Equal([0, 1, 2, 3, 4, 5], recorder.Delivered);
        Assert.Equal(1, maxDepth);
        Assert.Equal(5, context.Number.Value);
    }

    [Fact]
    public void HandlerThatChangesASource_OtherHandlersSeeTheLatestValue()
    {
        using var context = new ConcurrencyContext();
        var others = new List<int>();
        context.RegisterOutput(context.Number, (value, _) =>
        {
            if (value == 1)
                context.NumberSource.Set(2);
        });
        context.RegisterOutput(context.Number, (value, _) => others.Add(value));

        context.NumberSource.Set(1);

        // The nested change delivered 2 to the second handler first; the outer delivery then finds
        // nothing new for it
        Assert.Equal([2], others);
        Assert.Equal(2, context.Number.Value);
    }

    [Fact]
    public void RegistrationDisposedWhileADeliveryIsPending_IsNotCalledAgain()
    {
        using var context = new ConcurrencyContext();
        var delivered = new List<int>();
        IDisposable? registration = null;
        registration = context.RegisterOutput(context.Number, (value, _) =>
        {
            delivered.Add(value);
            context.NumberSource.Set(value + 1); // queues a delivery after this call
            registration!.Dispose(); // ... which must not happen
        });

        context.NumberSource.Set(1);

        Assert.Equal([1], delivered);
        Assert.Equal(2, context.Number.Value);
    }

    [Fact]
    public void ContextDisposedWhileADeliveryIsPending_DoesNotThrow()
    {
        var context = new ConcurrencyContext();
        var delivered = new List<int>();
        context.RegisterOutput(context.Number, (value, ctx) =>
        {
            delivered.Add(value);
            context.NumberSource.Set(value + 1); // queues a delivery after this call
            ctx.Dispose(); // the queued delivery is skipped instead of reading a disposed provider
        });

        context.NumberSource.Set(1);

        Assert.Equal([1], delivered);
        Assert.True(context.IsDisposed);
    }

    [Fact]
    public void HandlerWaitingForAChangeOnAnotherThread_DoesNotDeadlock()
    {
        using var context = new ConcurrencyContext();
        var delivered = new List<int>();
        context.RegisterOutput(context.Number, (value, _) =>
        {
            delivered.Add(value);
            if (value == 1)
            {
                // Another thread changes a source while this handler runs, and the handler waits
                // for that change to return: the other thread must not wait for this handler
                var writer = new Thread(() => context.NumberSource.Set(2));
                writer.Start();
                Assert.True(writer.Join(TimeSpan.FromSeconds(30)), "The concurrent change waited for the handler");
            }
        });

        context.NumberSource.Set(1);

        // The change made during the first call is delivered by this thread after it returns
        Assert.Equal([1, 2], delivered);
    }

    [Fact]
    public void ReentrantDeliveries_ThatThrow_AreRethrownFromTheOuterChange()
    {
        using var context = new ConcurrencyContext();
        context.RegisterOutput(context.Number, (value, _) =>
        {
            if (value == 1)
                context.NumberSource.Set(2); // does not throw: delivered after this call returns
            else
                throw new InvalidOperationException($"failed for {value}");
        });

        var ex = Assert.Throws<AggregateException>(() => context.NumberSource.Set(1));

        var inner = Assert.IsType<InvalidOperationException>(Assert.Single(ex.InnerExceptions));
        Assert.Equal("failed for 2", inner.Message);
    }
}
