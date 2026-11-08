using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;

namespace Guacamole.Benchmarks;

/// <summary>
/// K changed items out of N (spec 006 T032): replace <see cref="Changes"/> items of an
/// <see cref="Items"/>-item source, then read <c>Items.Select(work).Collect().Select(Sum)</c>.
/// <para>
/// Per-item memoization reruns the per-item computation only for the K replaced items; the
/// <c>Select</c> still walks all N items and <c>Collect</c>/<c>Sum</c> rerun over all of them, so
/// the cost is K computations plus an O(N) walk. <see cref="DirectRecompute"/> applies the same
/// changes to a plain array and reruns the computation for all N items.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class CollectionChangeBenchmarks
{
    private CollectionScenario _scenario = null!;
    private Item[] _items = null!;
    private int _start;

    /// <summary>Number of items in the source.</summary>
    [Params(1_000, 10_000)]
    public int Items { get; set; }

    /// <summary>Number of items replaced before each read.</summary>
    [Params(1, 10, 100)]
    public int Changes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scenario = CollectionScenario.Build(CollectionScenario.CreateItems(Items), Work.Mix);
        _items = CollectionScenario.CreateItems(Items);
        _start = 0;
        _ = _scenario.Total.Value; // warm the cache
    }

    [GlobalCleanup]
    public void Cleanup() => _scenario.Dispose();

    /// <summary>Replace K items in the source, then read the total.</summary>
    [Benchmark]
    public long Incremental()
    {
        _scenario.ReplaceItems(_start, Changes);
        _start += Changes;
        return _scenario.Total.Value;
    }

    /// <summary>Non-incremental baseline: replace K items in an array, then recompute all N items.</summary>
    [Benchmark(Baseline = true)]
    public long DirectRecompute()
    {
        CollectionScenario.ReplaceItems(_items, source: null, _start, Changes);
        _start += Changes;

        long total = 0;
        foreach (var item in _items)
            total += Work.Mix(item);
        return total;
    }
}
