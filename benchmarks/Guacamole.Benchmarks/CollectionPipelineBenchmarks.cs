using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;

namespace Guacamole.Benchmarks;

/// <summary>
/// K changed items out of N through a longer collection pipeline: replace <see cref="Changes"/>
/// items of an <see cref="Items"/>-item source, then read
/// <c>Items.Combine(factor).Select(work).Where(even).SelectMany(fan-out 0-2).Collect().Select(Sum)</c>.
/// <para>
/// Each collection stage derives its output from its previous output and its input's item-level
/// delta (#61), so the per-item work runs K times and the stages copy their unchanged items in bulk;
/// the sum at the end is O(N). <see cref="DirectRecompute"/> applies the same changes to a plain
/// array and recomputes everything.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class CollectionPipelineBenchmarks
{
    private BenchmarkContext _context = null!;
    private IIncrementalSource<Item> _source = null!;
    private IncrementalValueProvider<long> _total;
    private Item[] _sourceItems = null!;
    private Item[] _items = null!;
    private int _start;

    /// <summary>Number of items in the source.</summary>
    [Params(10_000)]
    public int Items { get; set; }

    /// <summary>Number of items replaced before each read.</summary>
    [Params(1, 100)]
    public int Changes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _context = new BenchmarkContext();
        _source = _context.AddSource<Item>("Items");
        var factor = _context.AddValueSource<int>("Factor");
        factor.Set(1);
        _sourceItems = CollectionScenario.CreateItems(Items);
        _items = CollectionScenario.CreateItems(Items);
        _source.PushRange(_sourceItems);
        _total = _source.AsProvider()
            .Combine(factor.AsProvider())
            .Select(static pair => Work.Mix(pair.Left.Value * pair.Right))
            .Where(static value => (value & 1) == 0)
            .SelectMany(static value => FanOut(value))
            .Collect()
            .Select(static values => Sum(values));
        _start = 0;
        _ = _total.Value; // warm the cache
    }

    [GlobalCleanup]
    public void Cleanup() => _context.Dispose();

    /// <summary>Replace K items in the source, then read the total.</summary>
    [Benchmark]
    public long Incremental()
    {
        CollectionScenario.ReplaceItems(_sourceItems, _source, _start, Changes);
        _start += Changes;
        return _total.Value;
    }

    /// <summary>Non-incremental baseline: replace K items in an array, then recompute everything.</summary>
    [Benchmark(Baseline = true)]
    public long DirectRecompute()
    {
        CollectionScenario.ReplaceItems(_items, source: null, _start, Changes);
        _start += Changes;

        long total = 0;
        foreach (var item in _items)
        {
            var value = Work.Mix(item.Value);
            if ((value & 1) == 0)
            {
                foreach (var result in FanOut(value))
                    total += result;
            }
        }

        return total;
    }

    private static long[] FanOut(long value) => (value % 3) switch
    {
        0 => [],
        1 => [value],
        _ => [value, value >> 1],
    };

    private static long Sum(ImmutableArray<long> values)
    {
        long sum = 0;
        foreach (var value in values)
            sum += value;
        return sum;
    }
}
