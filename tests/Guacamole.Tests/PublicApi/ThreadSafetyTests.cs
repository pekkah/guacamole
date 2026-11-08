using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// The thread-safety claims of README "Thread Safety" and the performance guide: source operations
/// and snapshot reads can run concurrently, and reading providers does not lock the pipeline but
/// still returns consistent results (lambdas may run more than once).
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class ThreadSafetyTests
{
    private const int Writers = 4;
    private const int ItemsPerWriter = 250;

    [Fact]
    public async Task ConcurrentReads_DuringConcurrentWrites_SeeConsistentValues()
    {
        using var context = new ApiContext();
        var values = context.Items.Select(static i => i.Value).Collect();
        var summary = values.Select(static v => (Count: v.Length, Sum: v.Sum()));
        using var stop = new CancellationTokenSource();
        var inconsistencies = 0;

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                // Every item has Value == 1, so a consistent value has Sum == Count
                var (count, sum) = summary.Value;
                var snapshot = context.ItemsSource.GetSnapshot().ToArray();
                if (count != sum || snapshot.Any(static i => i.Value != 1))
                    Interlocked.Increment(ref inconsistencies);
            }
        })).ToArray();

        var writers = Enumerable.Range(0, Writers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < ItemsPerWriter; i++)
            {
                context.ItemsSource.Push(new Item($"{w}-{i}", 1));
                if (i % 10 == 0)
                    context.ItemsSource.Remove(new Item($"{w}-{i}", 1));
            }
        })).ToArray();

        await Task.WhenAll(writers);
        await stop.CancelAsync();
        await Task.WhenAll(readers);

        Assert.Equal(0, inconsistencies);
        var expected = Writers * (ItemsPerWriter - (ItemsPerWriter / 10));
        Assert.Equal((expected, expected), summary.Value);
        Assert.Equal(expected, context.ItemsSource.GetSnapshot().Count);
    }

    [Fact]
    public async Task ConcurrentReadsOfAStaleProvider_AllReturnTheCurrentValue()
    {
        using var context = new ApiContext();
        var calls = new CallCounter();
        var squared = context.Number.Select(x => calls.Pass(x * x));

        for (var round = 1; round <= 20; round++)
        {
            context.NumberSource.Set(round);
            using var start = new Barrier(8);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                start.SignalAndWait();
                return squared.Value;
            })));

            Assert.All(results, r => Assert.Equal(round * round, r));
        }

        // Lambdas may run more than once per change, but at least once
        Assert.InRange(calls.Count, 20, 20 * 8);
    }

    [Fact]
    public async Task ConcurrentSets_LeaveTheLastWrittenValue_AndOutputHandlersSeeOnlySetValues()
    {
        using var context = new ApiContext();
        var delivered = new System.Collections.Concurrent.ConcurrentBag<int>();
        context.RegisterOutput(context.Number, (v, _) => delivered.Add(v));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
                context.NumberSource.Set((t * 1000) + i);
        })));

        var final = context.NumberSource.GetValue();
        Assert.Equal(final, context.Number.Value);
        Assert.All(delivered, v => Assert.InRange(v % 1000, 0, 99));
        Assert.NotEmpty(delivered);
    }
}
