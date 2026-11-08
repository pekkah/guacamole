using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;

namespace Guacamole.Benchmarks;

/// <summary>
/// Item-level deltas where a stage does not read its input after every change (#80), over
/// <see cref="Items"/> items, K = 1 replaced item per change:
/// <list type="bullet">
/// <item><description><see cref="LaggingReader"/>: a cheap pipeline over the source is read after
/// each of 4 changes, and <c>Items.Select(work).Collect().Select(Sum)</c> only after the fourth. The
/// lagging <c>Select</c> gets the composition of the source's 4 deltas from its delta history; with
/// only the latest delta kept it walked all N items.</description></item>
/// <item><description><see cref="MergedPipeline"/>: <c>first.Merge(second).Select(work).Collect().Select(Sum)</c>
/// over two sources of N/2 items, one item of the first replaced per read. <c>Merge</c> passes on
/// its inputs' deltas; when it did not, the <c>Select</c> after it walked all N items.</description></item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class DeltaPropagationBenchmarks
{
    private BenchmarkContext _laggingContext = null!;
    private IIncrementalSource<Item> _laggingSource = null!;
    private Item[] _laggingItems = null!;
    private IncrementalValueProvider<int> _count;
    private IncrementalValueProvider<long> _laggingTotal;

    private BenchmarkContext _mergeContext = null!;
    private IIncrementalSource<Item> _first = null!;
    private Item[] _firstItems = null!;
    private IncrementalValueProvider<long> _mergedTotal;

    private int _start;

    /// <summary>Number of items (in all sources together).</summary>
    [Params(10_000)]
    public int Items { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _laggingContext = new BenchmarkContext();
        _laggingSource = _laggingContext.AddSource<Item>("Items");
        _laggingItems = CollectionScenario.CreateItems(Items);
        _laggingSource.PushRange(_laggingItems);
        var items = _laggingSource.AsProvider();
        _count = items.Select(static item => item.Id).Collect().Select(static ids => ids.Length);
        _laggingTotal = items.Select(static item => Work.Mix(item)).Collect().Select(static values => Sum(values));
        _ = _count.Value;
        _ = _laggingTotal.Value;

        _mergeContext = new BenchmarkContext();
        _first = _mergeContext.AddSource<Item>("First");
        var second = _mergeContext.AddSource<Item>("Second");
        _firstItems = CollectionScenario.CreateItems(Items / 2);
        _first.PushRange(_firstItems);
        second.PushRange(Enumerable.Range(Items / 2, Items - (Items / 2)).Select(static i => new Item(i, i)));
        _mergedTotal = _first.AsProvider()
            .Merge(second.AsProvider())
            .Select(static item => Work.Mix(item))
            .Collect()
            .Select(static values => Sum(values));
        _ = _mergedTotal.Value;

        _start = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _laggingContext.Dispose();
        _mergeContext.Dispose();
    }

    /// <summary>4 changes, each followed by a read of a cheap pipeline, then a read of the total.</summary>
    [Benchmark]
    public long LaggingReader()
    {
        long count = 0;
        for (var i = 0; i < 4; i++)
        {
            CollectionScenario.ReplaceItems(_laggingItems, _laggingSource, _start++, 1);
            count += _count.Value;
        }

        return count + _laggingTotal.Value;
    }

    /// <summary>One change in the first of two merged sources, then a read of the total.</summary>
    [Benchmark]
    public long MergedPipeline()
    {
        CollectionScenario.ReplaceItems(_firstItems, _first, _start++, 1);
        return _mergedTotal.Value;
    }

    private static long Sum(ImmutableArray<long> values)
    {
        long sum = 0;
        foreach (var value in values)
            sum += value;
        return sum;
    }
}
