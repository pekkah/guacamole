using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #61: collection stages derive their output from their previous output and their input's
/// item-level delta (see Pipeline/ArrayDelta.cs), falling back to a full recompute when no delta is
/// available. These tests compare every stage with a direct computation over many random operation
/// sequences, check the recorded deltas, and cover the fallbacks and concurrent reads.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class CollectionDeltaTests
{
    public sealed record Item(int Key, int Value);

    public sealed record Doubled(int Key, int Value);

    [ComputingContext]
    private partial class ItemsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Item> Items { get; }
        public partial IncrementalValueProvider<int> Offset { get; }
    }

    [ComputingContext]
    private partial class SmallCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 2;

        public partial IncrementalValuesProvider<Item> Items { get; }
    }

    private static Doubled Double(Item item) => new(item.Key, item.Value * 2);

    private static bool Keep(Doubled doubled) => doubled.Value % 3 != 0;

    private static IEnumerable<int> Expand(Doubled doubled) => Enumerable.Repeat(doubled.Key, doubled.Value % 4);

    private static string? Label(Item item) => item.Value % 5 == 0 ? null : $"{item.Key}:{item.Value}";

    /// <summary>The pipelines under test, and the direct computation of each from the source's items.</summary>
    private sealed class Pipelines
    {
        public Pipelines(IncrementalValuesProvider<Item> items, IncrementalValueProvider<int> offset)
        {
            Doubled = items.Select(Double);
            Kept = Doubled.Where(Keep);
            Expanded = Kept.SelectMany(Expand);
            Collected = Expanded.Collect();
            AllKept = items.Where(i => i.Value >= 0);
            Labels = items.Select(Label);
            LabelLengths = Labels.Select(l => l?.Length ?? -1);
            Pairs = items.Combine(offset);
            Combined = Pairs.Select(t => t.Left.Value + t.Right);
            CombinedCollected = Combined.Collect();
            Fanned = items.SelectMany(i => Enumerable.Range(0, i.Value % 3).Select(k => new Doubled(i.Key, k)));
        }

        public IncrementalValuesProvider<Doubled> Doubled { get; }
        public IncrementalValuesProvider<Doubled> Kept { get; }
        public IncrementalValuesProvider<int> Expanded { get; }
        public IncrementalValueProvider<ImmutableArray<int>> Collected { get; }
        public IncrementalValuesProvider<Item> AllKept { get; }
        public IncrementalValuesProvider<string?> Labels { get; }
        public IncrementalValuesProvider<int> LabelLengths { get; }
        public IncrementalValuesProvider<(Item Left, int Right)> Pairs { get; }
        public IncrementalValuesProvider<int> Combined { get; }
        public IncrementalValueProvider<ImmutableArray<int>> CombinedCollected { get; }
        public IncrementalValuesProvider<Doubled> Fanned { get; }

        public int DeltaComputations =>
            ((SelectValuesNode<Item, Doubled>)Doubled.Node).DeltaComputations
            + ((WhereValuesNode<Doubled>)Kept.Node).DeltaComputations
            + ((SelectManyValuesNode<Doubled, int>)Expanded.Node).DeltaComputations
            + ((WhereValuesNode<Item>)AllKept.Node).DeltaComputations
            + ((SelectValuesNode<Item, string?>)Labels.Node).DeltaComputations
            + ((SelectValuesNode<string?, int>)LabelLengths.Node).DeltaComputations
            + ((CombineValuesWithValueNode<Item, int>)Pairs.Node).DeltaComputations
            + ((SelectValuesNode<(Item, int), int>)Combined.Node).DeltaComputations
            + ((SelectManyValuesNode<Item, Doubled>)Fanned.Node).DeltaComputations;

        /// <summary>Reads the given pipeline (0-9) and compares it with the direct computation.</summary>
        public void Check(int pipeline, IReadOnlyList<Item> items, int offset)
        {
            switch (pipeline)
            {
                case 0:
                    Assert.Equal(items.Select(Double), Doubled.Values);
                    break;
                case 1:
                    Assert.Equal(items.Select(Double).Where(Keep), Kept.Values);
                    break;
                case 2:
                    Assert.Equal(items.Select(Double).Where(Keep).SelectMany(Expand), Expanded.Values);
                    break;
                case 3:
                    Assert.Equal(items.Select(Double).Where(Keep).SelectMany(Expand), Collected.Value);
                    break;
                case 4:
                    Assert.Equal(items, AllKept.Values);
                    break;
                case 5:
                    Assert.Equal(items.Select(Label), Labels.Values);
                    break;
                case 6:
                    Assert.Equal(items.Select(i => Label(i)?.Length ?? -1), LabelLengths.Values);
                    break;
                case 7:
                    Assert.Equal(items.Select(i => i.Value + offset), Combined.Values);
                    break;
                case 8:
                    Assert.Equal(items.Select(i => i.Value + offset), CombinedCollected.Value);
                    break;
                default:
                    Assert.Equal(items.SelectMany(i => Enumerable.Range(0, i.Value % 3).Select(k => new Doubled(i.Key, k))), Fanned.Values);
                    break;
            }
        }

        public const int Count = 10;
    }

    /// <summary>
    /// The model of a collection source: set semantics (record equality), insertion order.
    /// Returns false (and leaves <paramref name="items"/> unchanged) when an operation would throw.
    /// </summary>
    private static bool ApplyToModel(List<Item> items, (int Kind, Item Item, Item NewItem) operation)
    {
        switch (operation.Kind)
        {
            case 0:
                if (!items.Contains(operation.Item))
                    items.Add(operation.Item);
                return true;
            case 1:
                items.Remove(operation.Item);
                return true;
            default:
                if (operation.Item == operation.NewItem)
                    return true;
                var index = items.IndexOf(operation.Item);
                if (index < 0 || items.Contains(operation.NewItem))
                    return false;
                items[index] = operation.NewItem;
                return true;
        }
    }

    private static (int Kind, Item Item, Item NewItem) RandomOperation(Random random, List<Item> items)
    {
        Item RandomItem() => random.Next(4) == 0 || items.Count == 0
            ? new Item(random.Next(40), random.Next(12))
            : items[random.Next(items.Count)];

        var kind = random.Next(10) switch
        {
            < 4 => 2, // replace (mostly a value change of an existing item)
            < 7 => 0, // push
            _ => 1, // remove
        };

        var item = RandomItem();
        var newItem = kind == 2 && random.Next(4) != 0
            ? item with { Value = random.Next(12) }
            : RandomItem();
        return (kind, item, newItem);
    }

    [Fact]
    public void RandomOperations_EveryStageMatchesTheDirectComputation()
    {
        var deltaComputations = 0;
        for (var seed = 0; seed < 150; seed++)
            deltaComputations += RunRandomScenario(seed, steps: 120);

        // The delta paths were taken (and, through lagging readers, batches and logs dropped, so
        // were the fallbacks: the checks above cover both)
        Assert.True(deltaComputations > 10_000, $"{deltaComputations} delta computations");
    }

    private static int RunRandomScenario(int seed, int steps)
    {
        var random = new Random(seed);
        using var context = new ItemsContext();
        var pipelines = new Pipelines(context.Items, context.Offset);
        var model = new List<Item>();
        var offset = 0;
        context.OffsetSource.Set(offset);

        // Remembers the latest cached output of each collection stage, to check the recorded deltas
        var outputs = new Dictionary<string, object>();

        for (var step = 0; step < steps; step++)
        {
            switch (random.Next(20))
            {
                case 0:
                    context.ItemsSource.Clear();
                    model.Clear();
                    break;
                case 1:
                    offset = random.Next(3);
                    context.OffsetSource.Set(offset);
                    break;
                case 2 or 3:
                    {
                        var range = Enumerable.Range(0, random.Next(1, 60)).Select(_ => new Item(random.Next(60), random.Next(12))).ToArray();
                        context.ItemsSource.PushRange(range);
                        foreach (var item in range)
                            ApplyToModel(model, (0, item, item));
                        break;
                    }
                case 4 or 5 or 6:
                    {
                        // A batch: all or nothing
                        var expected = new List<Item>(model);
                        var fails = false;
                        IBatchOperation<Item> batch = context.ItemsSource.BeginBatch();
                        for (var i = random.Next(1, 8); i > 0; i--)
                        {
                            var operation = RandomOperation(random, expected);
                            switch (operation.Kind)
                            {
                                case 0:
                                    batch.Push(operation.Item);
                                    break;
                                case 1:
                                    batch.Remove(operation.Item);
                                    break;
                                default:
                                    batch.Replace(operation.Item, operation.NewItem);
                                    break;
                            }

                            fails |= !fails && !ApplyToModel(expected, operation);
                        }

                        if (fails)
                        {
                            Assert.ThrowsAny<InvalidOperationException>(batch.Commit);
                        }
                        else
                        {
                            batch.Commit();
                            model = expected;
                        }

                        break;
                    }
                default:
                    {
                        // One or more single operations
                        for (var i = random.Next(1, 4); i > 0; i--)
                        {
                            var operation = RandomOperation(random, model);
                            var copy = new List<Item>(model);
                            if (!ApplyToModel(copy, operation))
                            {
                                Assert.ThrowsAny<InvalidOperationException>(() => context.ItemsSource.Replace(operation.Item, operation.NewItem));
                                continue;
                            }

                            switch (operation.Kind)
                            {
                                case 0:
                                    context.ItemsSource.Push(operation.Item);
                                    break;
                                case 1:
                                    context.ItemsSource.Remove(operation.Item);
                                    break;
                                default:
                                    context.ItemsSource.Replace(operation.Item, operation.NewItem);
                                    break;
                            }

                            model = copy;
                        }

                        break;
                    }
            }

            // Read a random subset of the pipelines, so that some stages lag behind their input
            for (var pipeline = 0; pipeline < Pipelines.Count; pipeline++)
            {
                if (random.Next(3) != 0)
                    pipelines.Check(pipeline, model, offset);
            }

            Assert.Equal(model, context.ItemsSource.GetSnapshot());
            CheckRecordedDeltas(context.Graph, pipelines, outputs);
        }

        for (var pipeline = 0; pipeline < Pipelines.Count; pipeline++)
            pipelines.Check(pipeline, model, offset);

        return pipelines.DeltaComputations;
    }

    /// <summary>
    /// For each collection stage, if it recorded the delta from the output seen at the previous
    /// check to its current cached output, checks that the delta is valid.
    /// </summary>
    private static void CheckRecordedDeltas(ComputationGraph graph, Pipelines pipelines, Dictionary<string, object> outputs)
    {
        Check(pipelines.Doubled);
        Check(pipelines.Kept);
        Check(pipelines.Expanded);
        Check(pipelines.AllKept);
        Check(pipelines.Labels);
        Check(pipelines.LabelLengths);
        Check(pipelines.Fanned);
        Check(pipelines.Pairs);
        Check(pipelines.Combined);

        void Check<T>(IncrementalValuesProvider<T> provider)
        {
            var node = provider.Node;
            if (!graph.ValueCache.TryGet(node.Id, out var cached) || cached is not CachedNodeValue { Value: T[] current })
                return;

            if (outputs.TryGetValue(node.Id, out var previous) && ((IDeltaSource<T>)node).GetDelta((T[])previous, current) is { } delta)
                DeltaAssert.Valid(delta, (T[])previous, current);

            outputs[node.Id] = current;
        }
    }

    [Fact]
    public void SourceDelta_DescribesEachKindOfChange()
    {
        using var context = new ItemsContext();
        var source = (IncrementalSource<Item>)context.ItemsSource;
        var node = (IDeltaSource<Item>)source.GetNode();
        var items = Enumerable.Range(0, 100).Select(i => new Item(i, i)).ToArray();
        source.PushRange(items);
        var previous = source.GetItems();

        void Step(Action change, int expectedEdits)
        {
            change();
            var current = source.GetItems();
            var delta = node.GetDelta(previous, current);
            Assert.NotNull(delta);
            DeltaAssert.Valid(delta, previous, current);
            Assert.Equal(expectedEdits, delta.Edits.Length);
            previous = current;
        }

        Step(() => source.Replace(items[10], items[10] with { Value = -1 }), 1);
        Step(() => source.Push(new Item(200, 0)), 1);
        Step(() => source.Remove(items[50]), 1); // creates a hole: positions are no longer slots
        Step(() => source.Remove(items[20]), 1);
        Step(() => source.Replace(items[60], items[60] with { Value = -1 }), 1);
        Step(() =>
        {
            using IBatchOperation<Item> batch = source.BeginBatch();
            batch.Remove(items[0]);
            batch.Push(new Item(201, 0));
            batch.Replace(items[99], items[99] with { Value = -1 });
            batch.Push(new Item(202, 0));
            batch.Remove(new Item(202, 0)); // pushed and removed: no edit
        }, 3);

        // A replacement by an item that was there before the change is no edit at all
        var replaced = items[30] with { Value = -1 };
        source.Replace(items[30], replaced);
        source.Replace(replaced, items[30]);
        Assert.Same(previous, source.GetItems());
    }

    [Fact]
    public void SourceDelta_DroppedWhenTheLogOverflows_AndResumedAfterwards()
    {
        using var context = new ItemsContext();
        var source = (IncrementalSource<Item>)context.ItemsSource;
        var node = (IDeltaSource<Item>)source.GetNode();
        var items = Enumerable.Range(0, 100).Select(i => new Item(i, i)).ToArray();
        source.PushRange(items);
        var doubled = context.Items.Select(Double);
        var select = (SelectValuesNode<Item, Doubled>)doubled.Node;
        Assert.Equal(items.Select(Double), doubled.Values);
        var previous = source.GetItems();

        // 60 changes before the next read: more than half the items, the log is dropped
        for (var i = 0; i < 60; i++)
        {
            source.Replace(items[i], items[i] with { Value = -i });
            items[i] = items[i] with { Value = -i };
        }

        var current = source.GetItems();
        Assert.Null(node.GetDelta(previous, current));
        Assert.Equal(items.Select(Double), doubled.Values);
        Assert.Equal(0, select.DeltaComputations);

        // The next change has a delta again
        source.Replace(items[70], items[70] with { Value = 0 });
        items[70] = items[70] with { Value = 0 };
        Assert.Equal(items.Select(Double), doubled.Values);
        Assert.Equal(1, select.DeltaComputations);
    }

    [Fact]
    public void SourceDelta_ClearAndCompaction_FallBackToAFullRecompute()
    {
        using var context = new ItemsContext();
        var source = context.ItemsSource;
        var items = Enumerable.Range(0, 200).Select(i => new Item(i, i)).ToList();
        source.PushRange(items);
        var doubled = context.Items.Select(Double);
        var kept = doubled.Where(Keep);
        var select = (SelectValuesNode<Item, Doubled>)doubled.Node;
        Assert.Equal(items.Select(Double).Where(Keep), kept.Values);

        // Removing most items one read at a time compacts the slots along the way
        var deltas = select.DeltaComputations;
        for (var i = 0; i < 150; i++)
        {
            source.Remove(items[0]);
            items.RemoveAt(0);
            Assert.Equal(items.Select(Double).Where(Keep), kept.Values);
        }

        Assert.InRange(select.DeltaComputations - deltas, 140, 149); // at least one compaction fell back

        source.Clear();
        items.Clear();
        Assert.Empty(kept.Values);

        source.Push(new Item(1, 1));
        items.Add(new Item(1, 1));
        Assert.Equal(items.Select(Double).Where(Keep), kept.Values);
        source.Push(new Item(2, 2));
        items.Add(new Item(2, 2));
        Assert.Equal(items.Select(Double).Where(Keep), kept.Values);
    }

    [Fact]
    public void LaggingStage_FallsBackToAFullRecompute()
    {
        using var context = new ItemsContext();
        var items = Enumerable.Range(0, 50).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var first = context.Items.Select(i => i.Value);
        var second = context.Items.Select(i => -i.Value);
        var firstNode = (SelectValuesNode<Item, int>)first.Node;
        Assert.Equal(items.Select(i => i.Value), first.Values);
        Assert.Equal(items.Select(i => -i.Value), second.Values);

        // `first` misses the snapshot in between: no delta from what it last read
        context.ItemsSource.Replace(items[1], new Item(1, 100));
        Assert.Equal(-100, second.Values.ElementAt(1));
        context.ItemsSource.Replace(items[2], new Item(2, 200));
        Assert.Equal(-200, second.Values.ElementAt(2));

        Assert.Equal([0, 100, 200], first.Values.Take(3));
        Assert.Equal(0, firstNode.DeltaComputations);

        context.ItemsSource.Replace(items[3], new Item(3, 300));
        Assert.Equal([0, 100, 200, 300], first.Values.Take(4));
        Assert.Equal(1, firstNode.DeltaComputations);
    }

    [Fact]
    public void NodeWithoutDeltas_FallsBackToAFullRecompute()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([new Item(1, 1), new Item(2, 2)]);
        var copy = new IncrementalValuesProvider<Item>(
            new CopyNode(context.Items.Node), context.Graph, new ProviderMetadata("copy", "test"));
        var doubled = copy.Select(Double);
        Assert.Equal([new Doubled(1, 2), new Doubled(2, 4)], doubled.Values);

        context.ItemsSource.Replace(new Item(2, 2), new Item(2, 3));

        Assert.Equal([new Doubled(1, 2), new Doubled(2, 6)], doubled.Values);
        Assert.Equal(0, ((SelectValuesNode<Item, Doubled>)doubled.Node).DeltaComputations);
    }

    /// <summary>A collection node that records no deltas.</summary>
    private sealed class CopyNode(ICollectionComputationNode<Item> source)
        : CollectionComputationNodeBase<Item>("Copy-" + Guid.NewGuid().ToString("N"), [source.Id])
    {
        public override Result<IEnumerable<Item>, ComputationError> Compute(ComputationContext context)
        {
            var result = context.Read(source);
            return result.IsSuccess ? Result<IEnumerable<Item>, ComputationError>.Success(result.ValueOrDefault!.ToArray()) : result;
        }
    }

    [Fact]
    public void LambdaError_KeepsThePreviousState_AndRecovers()
    {
        using var context = new ItemsContext();
        var items = Enumerable.Range(1, 20).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var inverse = context.Items.Select(i => 100 / i.Value).Where(v => v > 5).SelectMany(v => new[] { v, -v });
        var collected = inverse.Collect();
        int[] Expected(IEnumerable<Item> source) => source.Select(i => 100 / i.Value).Where(v => v > 5).SelectMany(v => new[] { v, -v }).ToArray();
        Assert.Equal(Expected(items), collected.Value);

        context.ItemsSource.Replace(items[4], new Item(5, 0));
        Assert.True(collected.GetValue().IsError);

        context.ItemsSource.Replace(new Item(5, 0), new Item(5, 4));
        items[4] = new Item(5, 4);
        Assert.Equal(Expected(items), collected.Value);

        context.ItemsSource.Replace(items[0], new Item(1, 2));
        items[0] = new Item(1, 2);
        Assert.Equal(Expected(items), collected.Value);
    }

    [Fact]
    public void EvictedCachedValues_StillUseDeltas()
    {
        using var context = new SmallCacheContext();
        var items = Enumerable.Range(0, 100).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var collected = context.Items.Select(Double).Where(Keep).SelectMany(Expand).Collect();
        var total = collected.Select(values => values.Sum());
        int Expected() => items.Select(Double).Where(Keep).SelectMany(Expand).Sum();
        Assert.Equal(Expected(), total.Value);

        for (var i = 0; i < 100; i += 7)
        {
            context.ItemsSource.Replace(items[i], items[i] with { Value = i + 1 });
            items[i] = items[i] with { Value = i + 1 };
            Assert.Equal(Expected(), total.Value);
        }
    }

    [Fact]
    public void ReplacedItemWithAnEqualResult_DoesNotChangeDownstream()
    {
        using var context = new ItemsContext();
        var items = Enumerable.Range(0, 100).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var keys = context.Items.Select(i => i.Key); // ignores values
        var collected = keys.Collect();
        var runs = 0;
        var total = collected.Select(k => { runs++; return k.Sum(); });
        var calls = 0;
        context.RegisterOutput(collected, (_, _) => calls++);
        Assert.Equal(4950, total.Value);
        var version = context.Graph.Versions.GetOutputVersion(keys.Node.Id);

        context.ItemsSource.Replace(items[42], items[42] with { Value = -1 });

        Assert.Equal(4950, total.Value);
        Assert.Equal(1, runs);
        Assert.Equal(1, calls);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(keys.Node.Id));
    }

    [Fact]
    public void CollectWithComparer_ComparesTheReplacedItemsWithIt()
    {
        using var context = new ItemsContext();
        var items = Enumerable.Range(0, 10).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var collected = context.Items.Collect(new KeyComparer());
        var runs = 0;
        var count = collected.Select(c => { runs++; return c.Length; });
        Assert.Equal(10, count.Value);

        context.ItemsSource.Replace(items[3], items[3] with { Value = -1 }); // same key
        Assert.Equal(10, count.Value);
        Assert.Equal(1, runs);

        context.ItemsSource.Replace(items[4], new Item(40, 4)); // another key
        Assert.Equal(10, count.Value);
        Assert.Equal(2, runs);
    }

    private sealed class KeyComparer : IEqualityComparer<Item>
    {
        public bool Equals(Item? x, Item? y) => x?.Key == y?.Key;

        public int GetHashCode(Item obj) => obj.Key;
    }

    [Fact]
    public async Task ConcurrentReadsAndChanges_StayConsistent()
    {
        using var context = new ItemsContext();
        const int ItemCount = 200;
        const int Total = ItemCount * 3;
        var keys = Enumerable.Range(0, ItemCount).ToArray();
        var model = keys.Select(k => new Item(k, 3)).ToList();
        context.ItemsSource.PushRange(model);
        context.OffsetSource.Set(1);

        // Changes move value units between items in atomic batches, so the total of the values is
        // always Total; emptied items are sometimes moved to the end
        var sum = context.Items.Select(i => i.Value).Collect().Select(v => v.Sum());
        var expandedCount = context.Items.Where(i => i.Value > 0).SelectMany(i => Enumerable.Repeat(i.Key, i.Value)).Collect().Select(v => v.Length);
        var combinedSum = context.Items.Combine(context.Offset).Select(t => t.Left.Value * t.Right).Collect().Select(v => v.Sum());
        var doubledSum = context.Items.Select(Double).Where(d => d.Value > 0).Collect().Select(v => v.Sum(d => d.Value));

        using var stop = new CancellationTokenSource();
        var failures = new List<string>();

        void Read()
        {
            while (!stop.IsCancellationRequested)
            {
                Expect(sum.Value, "sum");
                Expect(expandedCount.Value, "expanded");
                Expect(combinedSum.Value, "combined");
                Expect(doubledSum.Value / 2, "doubled");
            }
        }

        void Expect(int value, string what)
        {
            if (value != Total)
            {
                lock (failures)
                    failures.Add($"{what}: {value}");
            }
        }

        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(Read)).ToArray();
        var random = new Random(61);
        for (var round = 0; round < 3_000; round++)
        {
            var a = random.Next(model.Count);
            var b = random.Next(model.Count);
            if (a == b || model[a].Value == 0)
                continue;

            var fromItem = model[a];
            var toItem = model[b];
            var movedFrom = fromItem with { Value = fromItem.Value - 1 };
            var movedTo = toItem with { Value = toItem.Value + 1 };
            using (IBatchOperation<Item> batch = context.ItemsSource.BeginBatch())
            {
                batch.Replace(fromItem, movedFrom);
                batch.Replace(toItem, movedTo);
                if (movedFrom.Value == 0 && random.Next(2) == 0)
                {
                    // Moves the emptied item to the end
                    batch.Remove(movedFrom);
                    batch.Push(movedFrom);
                }
            }

            model = context.ItemsSource.GetSnapshot().ToList();
            if (round % 10 == 0)
                await Task.Yield();
        }

        stop.Cancel();
        await Task.WhenAll(readers);

        Assert.Empty(failures);
        Assert.Equal(Total, sum.Value);
        Assert.Equal(Total, expandedCount.Value);
        Assert.Equal(model.Sum(i => i.Value), combinedSum.Value);
        Assert.Equal(model.Where(i => i.Value > 0).SelectMany(i => Enumerable.Repeat(i.Key, i.Value)), context.Items.Where(i => i.Value > 0).SelectMany(i => Enumerable.Repeat(i.Key, i.Value)).Values);
    }
}

/// <summary>
/// Checks the invariants of an <see cref="ArrayDelta{T}"/>: it describes the two arrays, its edits
/// are sorted in both arrays, and applying it to the old array (copying the runs between edits,
/// which must hold the identical items, and taking the edited items from the new array) gives the
/// new array.
/// </summary>
internal static class DeltaAssert
{
    public static void Valid<T>(ArrayDelta<T> delta, T[] from, T[] to)
    {
        Assert.True(delta.Describes(from, to));
        Assert.Equal(from.Length, delta.FromLength);

        int f = 0, t = 0;
        foreach (var edit in delta.Edits)
        {
            Assert.False(edit.From < 0 && edit.To < 0);
            var run = edit.RunBefore(f, t);
            Assert.True(run >= 0, "edits out of order");
            if (edit.From >= 0 && edit.To >= 0)
                Assert.Equal(edit.From - f, edit.To - t); // the run is the same length on both sides
            AssertRun(run);
            if (edit.From >= 0)
                f++;
            if (edit.To >= 0)
                t++;
        }

        Assert.Equal(from.Length - f, to.Length - t);
        AssertRun(from.Length - f);

        void AssertRun(int run)
        {
            for (var k = 0; k < run; k++, f++, t++)
            {
                if (typeof(T).IsValueType)
                    Assert.Equal(from[f], to[t]);
                else
                    Assert.True(ReferenceEquals(from[f], to[t]), $"item {t} of an unchanged run is not the identical item");
            }
        }
    }
}
