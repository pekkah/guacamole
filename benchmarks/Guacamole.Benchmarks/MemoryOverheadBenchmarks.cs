using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;

namespace Guacamole.Benchmarks;

/// <summary>
/// Memory overhead against a plain baseline (spec 006 T033). Both methods start from the same
/// N items and compute the same total:
/// <list type="bullet">
/// <item><see cref="Direct"/> computes the per-item results into an array and sums them, which
/// is what a non-incremental implementation keeps around.</item>
/// <item><see cref="BuildAndRead"/> creates a context, pushes the items into a collection source
/// and reads <c>Items.Select(work).Collect().Select(Sum)</c>.</item>
/// </list>
/// The <c>Alloc Ratio</c> column is the allocation overhead of setting up the incremental
/// pipeline (source storage, per-item memo, collected array, cache entries). It is allocated
/// bytes, not retained bytes, and it is paid once: later changes allocate what
/// <see cref="CollectionChangeBenchmarks"/> reports.
/// </summary>
[MemoryDiagnoser]
public class MemoryOverheadBenchmarks
{
    private Item[] _items = null!;

    /// <summary>Number of items.</summary>
    [Params(1_000, 10_000)]
    public int Items { get; set; }

    [GlobalSetup]
    public void Setup() => _items = CollectionScenario.CreateItems(Items);

    /// <summary>Baseline: per-item results into an array, then the sum.</summary>
    [Benchmark(Baseline = true)]
    public long Direct()
    {
        var results = new long[_items.Length];
        for (int i = 0; i < _items.Length; i++)
            results[i] = Work.Mix(_items[i]);

        long total = 0;
        foreach (var result in results)
            total += result;
        return total;
    }

    /// <summary>A new context with the items pushed into a source, and the first read of the total.</summary>
    [Benchmark]
    public long BuildAndRead()
    {
        using var scenario = CollectionScenario.Build(_items, Work.Mix);
        return scenario.Total.Value;
    }
}
