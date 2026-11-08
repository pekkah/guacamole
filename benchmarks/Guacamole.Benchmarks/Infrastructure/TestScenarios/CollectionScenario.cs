using System.Collections.Immutable;

namespace Guacamole.Benchmarks.Infrastructure.TestScenarios;

/// <summary>An immutable collection item with value equality.</summary>
public sealed record Item(int Id, int Value);

/// <summary>
/// One collection source of N items, a per-item computation, and a total:
/// <c>Items.Select(work).Collect().Select(Sum)</c>. Replacing K items reruns the per-item
/// computation K times (per-item memoization); the <c>Select</c> still walks all N items and
/// <c>Collect</c>/<c>Sum</c> rerun over all of them. The per-item computation is a parameter so
/// tests can count calls.
/// </summary>
public sealed class CollectionScenario : IDisposable
{
    private CollectionScenario(BenchmarkContext context, IIncrementalSource<Item> source, IncrementalValueProvider<long> total, Item[] items)
    {
        Context = context;
        Source = source;
        Total = total;
        Items = items;
    }

    /// <summary>The context that owns the source.</summary>
    public BenchmarkContext Context { get; }

    /// <summary>The collection source.</summary>
    public IIncrementalSource<Item> Source { get; }

    /// <summary>Sum of the per-item computation over all items.</summary>
    public IncrementalValueProvider<long> Total { get; }

    /// <summary>The current items, in source order (kept in sync by <see cref="ReplaceItems(int, int)"/>).</summary>
    public Item[] Items { get; }

    /// <summary>Creates <paramref name="count"/> items with <c>Id = Value = index</c>.</summary>
    public static Item[] CreateItems(int count) =>
        Enumerable.Range(0, count).Select(static i => new Item(i, i)).ToArray();

    /// <summary>Builds the pipeline over <paramref name="items"/>; the scenario takes ownership of the array.</summary>
    public static CollectionScenario Build(Item[] items, Func<Item, long> itemWork)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(itemWork);

        var context = new BenchmarkContext();
        var source = context.AddSource<Item>("Items");
        source.PushRange(items);
        var total = source.AsProvider().Select(itemWork).Collect().Select(static results => Sum(results));
        return new CollectionScenario(context, source, total, items);
    }

    /// <summary>
    /// Replaces <paramref name="count"/> consecutive items of <paramref name="items"/> (wrapping
    /// around), starting at <paramref name="start"/>, with items whose value is one higher, and
    /// does the same in <paramref name="source"/> when it is not <see langword="null"/>.
    /// </summary>
    public static void ReplaceItems(Item[] items, IIncrementalSource<Item>? source, int start, int count)
    {
        for (int j = 0; j < count; j++)
        {
            int index = (start + j) % items.Length;
            var oldItem = items[index];
            var newItem = oldItem with { Value = oldItem.Value + 1 };
            source?.Replace(oldItem, newItem);
            items[index] = newItem;
        }
    }

    /// <summary>Replaces items in both <see cref="Items"/> and <see cref="Source"/>.</summary>
    public void ReplaceItems(int start, int count) => ReplaceItems(Items, Source, start, count);

    private static long Sum(ImmutableArray<long> results)
    {
        long sum = 0;
        foreach (var result in results)
            sum += result;
        return sum;
    }

    /// <inheritdoc />
    public void Dispose() => Context.Dispose();
}
