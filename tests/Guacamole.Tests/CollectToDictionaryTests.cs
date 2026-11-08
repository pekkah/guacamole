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
/// Issue #82: <c>CollectToDictionary</c> keeps a dictionary over a collection up to date from the
/// collection's item-level deltas. These tests compare it with a full fold over many random
/// operation sequences (single operations, batches, readers lagging behind), and cover the equality
/// cut-off, conflicts, error and None propagation, node reuse and concurrent reads.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class CollectToDictionaryTests
{
    public sealed record Item(int Key, int Value);

    [ComputingContext]
    private partial class ItemsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Item> Items { get; }
        public partial IncrementalValueProvider<int> Offset { get; }
    }

    /// <summary>
    /// The semantics, as a full fold: each key maps to the value its pairs agree on, or the smallest
    /// by <paramref name="conflictComparer"/>; without one, a key with different values is a
    /// conflict (null result).
    /// </summary>
    private static Dictionary<TKey, TValue>? Fold<TKey, TValue>(IEnumerable<(TKey Key, TValue Value)> pairs, IComparer<TValue>? conflictComparer)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (var group in pairs.GroupBy(p => p.Key))
        {
            var values = group.Select(p => p.Value).Distinct().ToList();
            if (values.Count > 1 && conflictComparer is null)
                return null;

            result[group.Key] = conflictComparer is null ? values[0] : values.Min(conflictComparer)!;
        }

        return result;
    }

    private static void AssertMatches<TKey, TValue>(Dictionary<TKey, TValue>? expected, IncrementalValueProvider<ImmutableDictionary<TKey, TValue>> provider)
        where TKey : notnull
    {
        var result = provider.GetValue();
        if (expected is null)
        {
            Assert.True(result.IsError, "a conflict without a comparer is an error");
            return;
        }

        Assert.True(result.IsSuccess, result.ErrorValue?.Message);
        var actual = result.ValueOrDefault!;
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (key, value) in expected)
        {
            Assert.True(actual.TryGetValue(key, out var actualValue), $"missing key {key}");
            Assert.Equal(value, actualValue);
        }
    }

    private sealed class Pipelines
    {
        public Pipelines(IncrementalValuesProvider<Item> items)
        {
            // Many items per key: conflicts resolved by the comparer
            ByKeyMod = items.CollectToDictionary(i => i.Key % 7, i => i.Value % 5, Comparer<int>.Default);

            // Duplicates (SelectMany fans out to equal pairs) and conflicts
            Fanned = items.SelectMany(i => Enumerable.Range(0, i.Value % 3).Select(k => (Key: (i.Key + k) % 11, Value: i.Value % 2)));
            ByFannedKey = Fanned.CollectToDictionary(p => p.Key, p => p.Value, Comparer<int>.Default);

            // No comparer: items with the same key and different values are a conflict (an error)
            Strict = items.CollectToDictionary(i => i.Key, i => i.Value);

            // Keys and values after a Where, with a reverse comparer
            Filtered = items.Where(i => i.Value % 3 != 0).CollectToDictionary(i => i.Key % 13, i => i.Value, Comparer<int>.Create((a, b) => b.CompareTo(a)));
        }

        public IncrementalValueProvider<ImmutableDictionary<int, int>> ByKeyMod { get; }
        public IncrementalValuesProvider<(int Key, int Value)> Fanned { get; }
        public IncrementalValueProvider<ImmutableDictionary<int, int>> ByFannedKey { get; }
        public IncrementalValueProvider<ImmutableDictionary<int, int>> Strict { get; }
        public IncrementalValueProvider<ImmutableDictionary<int, int>> Filtered { get; }

        public const int Count = 4;

        public int DeltaComputations =>
            ((CollectToDictionaryNode<Item, int, int>)ByKeyMod.Node).DeltaComputations
            + ((CollectToDictionaryNode<(int, int), int, int>)ByFannedKey.Node).DeltaComputations
            + ((CollectToDictionaryNode<Item, int, int>)Strict.Node).DeltaComputations
            + ((CollectToDictionaryNode<Item, int, int>)Filtered.Node).DeltaComputations;

        public void Check(int pipeline, IReadOnlyList<Item> items)
        {
            switch (pipeline)
            {
                case 0:
                    AssertMatches(Fold(items.Select(i => (i.Key % 7, i.Value % 5)), Comparer<int>.Default), ByKeyMod);
                    break;
                case 1:
                    AssertMatches(Fold(items.SelectMany(i => Enumerable.Range(0, i.Value % 3).Select(k => ((i.Key + k) % 11, i.Value % 2))), Comparer<int>.Default), ByFannedKey);
                    break;
                case 2:
                    AssertMatches(Fold(items.Select(i => (i.Key, i.Value)), null), Strict);
                    break;
                default:
                    AssertMatches(Fold(items.Where(i => i.Value % 3 != 0).Select(i => (i.Key % 13, i.Value)), Comparer<int>.Create((a, b) => b.CompareTo(a))), Filtered);
                    break;
            }
        }
    }

    [Fact]
    public void RandomOperations_MatchAFullFold()
    {
        var deltaComputations = 0;
        for (var seed = 0; seed < 150; seed++)
            deltaComputations += RunRandomScenario(seed, steps: 100);

        Assert.True(deltaComputations > 5_000, $"{deltaComputations} delta computations");
    }

    private static int RunRandomScenario(int seed, int steps)
    {
        var random = new Random(seed);
        using var context = new ItemsContext();
        var pipelines = new Pipelines(context.Items);
        var model = new List<Item>();
        int[] rates = [1, 1, 2, 4, 12];
        var readEvery = Enumerable.Range(0, Pipelines.Count).Select(_ => rates[random.Next(rates.Length)]).ToArray();

        Item RandomItem() => random.Next(3) == 0 || model.Count == 0
            ? new Item(random.Next(30), random.Next(12))
            : model[random.Next(model.Count)];

        for (var step = 0; step < steps; step++)
        {
            switch (random.Next(16))
            {
                case 0:
                    context.ItemsSource.Clear();
                    model.Clear();
                    break;
                case 1:
                    {
                        var range = Enumerable.Range(0, random.Next(1, 40)).Select(_ => new Item(random.Next(30), random.Next(12))).ToArray();
                        context.ItemsSource.PushRange(range);
                        foreach (var item in range)
                        {
                            if (!model.Contains(item))
                                model.Add(item);
                        }

                        break;
                    }
                case 2 or 3 or 4:
                    {
                        // A batch of pushes, removals and value changes
                        var expected = new List<Item>(model);
                        using (IBatchOperation<Item> batch = context.ItemsSource.BeginBatch())
                        {
                            for (var i = random.Next(1, 6); i > 0; i--)
                            {
                                var item = RandomItem();
                                if (random.Next(2) == 0 && expected.Contains(item))
                                {
                                    var replacement = item with { Value = random.Next(12) };
                                    if (expected.Contains(replacement))
                                        continue;

                                    batch.Replace(item, replacement);
                                    expected[expected.IndexOf(item)] = replacement;
                                }
                                else if (expected.Contains(item))
                                {
                                    batch.Remove(item);
                                    expected.Remove(item);
                                }
                                else
                                {
                                    batch.Push(item);
                                    expected.Add(item);
                                }
                            }
                        }

                        model = expected;
                        break;
                    }
                default:
                    {
                        var item = RandomItem();
                        if (!model.Contains(item))
                        {
                            context.ItemsSource.Push(item);
                            model.Add(item);
                        }
                        else if (random.Next(3) == 0)
                        {
                            context.ItemsSource.Remove(item);
                            model.Remove(item);
                        }
                        else
                        {
                            var replacement = item with { Value = random.Next(12) };
                            if (!model.Contains(replacement))
                            {
                                context.ItemsSource.Replace(item, replacement);
                                model[model.IndexOf(item)] = replacement;
                            }
                        }

                        break;
                    }
            }

            for (var pipeline = 0; pipeline < Pipelines.Count; pipeline++)
            {
                if (random.Next(readEvery[pipeline]) == 0)
                    pipelines.Check(pipeline, model);
            }
        }

        for (var pipeline = 0; pipeline < Pipelines.Count; pipeline++)
            pipelines.Check(pipeline, model);

        return pipelines.DeltaComputations;
    }

    [Fact]
    public void Result_DoesNotDependOnTheOrderOfTheItems()
    {
        var items = Enumerable.Range(0, 200).Select(i => new Item(i, i * 7 % 13)).ToArray();
        ImmutableDictionary<int, int>? first = null;
        foreach (var seed in new[] { 1, 2, 3 })
        {
            using var context = new ItemsContext();
            var table = context.Items.CollectToDictionary(i => i.Key % 10, i => i.Value, Comparer<int>.Default);
            var random = new Random(seed);
            foreach (var item in items.OrderBy(_ => random.Next()))
            {
                context.ItemsSource.Push(item);
                if (random.Next(10) == 0)
                    _ = table.Value; // some updates from deltas, some folds
            }

            var result = table.Value;
            if (first is null)
                first = result;
            else
                Assert.Equal(first.OrderBy(kv => kv.Key), result.OrderBy(kv => kv.Key));
        }

        Assert.Equal(Enumerable.Range(0, 10).Select(k => items.Where(i => i.Key % 10 == k).Min(i => i.Value)), first!.OrderBy(kv => kv.Key).Select(kv => kv.Value));
    }

    [Fact]
    public void UnchangedEntries_KeepTheInstanceAndTheVersion()
    {
        using var context = new ItemsContext();
        var items = Enumerable.Range(0, 100).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var table = context.Items.CollectToDictionary(i => i.Key, i => i.Value / 10);
        var runs = 0;
        var count = table.Select(t => { runs++; return t.Count; });
        var calls = 0;
        using var registration = context.RegisterOutput(table, (_, _) => calls++);
        Assert.Equal(100, count.Value);
        var before = table.Value;
        var version = context.Graph.Versions.GetOutputVersion(table.Node.Id);
        var node = (CollectToDictionaryNode<Item, int, int>)table.Node;

        // Same key, same value (42 / 10 == 43 / 10): the dictionary is the identical instance
        context.ItemsSource.Replace(items[42], items[42] with { Value = 43 });

        Assert.Same(before, table.Value);
        Assert.Equal(100, count.Value);
        Assert.Equal(1, runs);
        Assert.Equal(1, calls);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(table.Node.Id));
        Assert.Equal(1, node.DeltaComputations);

        // A changed value: a new instance, downstream reruns
        context.ItemsSource.Replace(items[43], items[43] with { Value = 99 });
        Assert.NotSame(before, table.Value);
        Assert.Equal(9, table.Value[43]);
        Assert.Equal(100, count.Value);
        Assert.Equal(2, runs);
        Assert.Equal(2, calls);
        Assert.Equal(2, node.DeltaComputations);
    }

    [Fact]
    public void ChangeCheck_RecognizesTheLatestTransition_WithoutComparingEntries()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange(Enumerable.Range(0, 10).Select(i => new Item(i, i)));
        var table = context.Items.CollectToDictionary(i => i.Key, i => i.Value);
        var node = (CollectToDictionaryNode<Item, int, int>)table.Node;
        var before = table.Value;

        context.ItemsSource.Push(new Item(10, 10));
        var after = table.Value;

        Assert.True(node.TryEquals(before, after, out var equal));
        Assert.False(equal);
        Assert.True(node.TryEquals(after, after, out equal));
        Assert.True(equal);
        Assert.False(node.TryEquals(after, before, out _)); // not a transition it made: compared by the comparer
        Assert.False(node.Comparer.Equals(after, before));
        Assert.True(node.Comparer.Equals(before, before.Remove(3).Add(3, 3)));
    }

    [Fact]
    public void Conflict_WithoutComparer_IsAnError_UntilItGoesAway()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([new Item(1, 1), new Item(2, 2)]);
        var table = context.Items.CollectToDictionary(i => i.Key, i => i.Value % 4);
        Assert.Equal(2, table.Value.Count);

        // Another item with an equal value for the key: no conflict
        context.ItemsSource.Push(new Item(1, 5));
        Assert.Equal(1, table.Value[1]);

        context.ItemsSource.Push(new Item(1, 6));
        var result = table.GetValue();
        Assert.True(result.IsError);
        Assert.Contains("'1'", result.ErrorValue!.Message);
        Assert.True(table.GetValue().IsError); // read again: the same input, the same error

        context.ItemsSource.Remove(new Item(1, 6));
        Assert.Equal(1, table.Value[1]);
        Assert.Equal(2, table.Value.Count);
    }

    [Fact]
    public void Duplicates_WithEqualValues_AreNoConflict_AndCountedPerItem()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([new Item(1, 1), new Item(2, 2)]);
        var pairs = context.Items.SelectMany(i => new[] { (Key: i.Key % 2, Value: "x"), (Key: 7, Value: "y") });
        var table = pairs.CollectToDictionary(p => p.Key, p => p.Value);
        Assert.Equal(3, table.Value.Count);

        context.ItemsSource.Remove(new Item(1, 1));
        Assert.Equal(["0", "7"], table.Value.Keys.Order().Select(k => k.ToString()));

        context.ItemsSource.Remove(new Item(2, 2));
        Assert.Empty(table.Value);
    }

    [Fact]
    public void ConflictComparer_ChoosesTheSmallestValue_AndFallsBackWhenItIsRemoved()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([new Item(1, 5), new Item(1, 3), new Item(1, 8)]);
        var table = context.Items.CollectToDictionary(i => i.Key, i => i.Value, Comparer<int>.Default);
        Assert.Equal(3, table.Value[1]);

        context.ItemsSource.Remove(new Item(1, 3));
        Assert.Equal(5, table.Value[1]);
        context.ItemsSource.Replace(new Item(1, 5), new Item(1, 9));
        Assert.Equal(8, table.Value[1]);
        context.ItemsSource.Remove(new Item(1, 8));
        Assert.Equal(9, table.Value[1]);
        context.ItemsSource.Remove(new Item(1, 9));
        Assert.Empty(table.Value);
    }

    [Fact]
    public void InputAndSelectorErrors_Propagate_AndTheNextChangeUsesTheDeltaAgain()
    {
        using var context = new ItemsContext();
        var items = Enumerable.Range(1, 20).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var inverse = context.Items.Select(i => new Item(i.Key, 100 / i.Value));
        var table = inverse.CollectToDictionary(i => i.Key, i => i.Value);
        var node = (CollectToDictionaryNode<Item, int, int>)table.Node;
        Assert.Equal(20, table.Value.Count);

        // An error upstream
        context.ItemsSource.Replace(items[4], new Item(5, 0));
        Assert.True(table.GetValue().IsError);
        context.ItemsSource.Replace(new Item(5, 0), new Item(5, 4));
        Assert.Equal(25, table.Value[5]);

        // A throwing value selector
        var throwing = context.Items.CollectToDictionary(i => i.Key, i => 100 / (i.Value - 3));
        Assert.True(throwing.GetValue().IsError);
        context.ItemsSource.Replace(items[2], new Item(3, 4));
        Assert.Equal(100, throwing.Value[3]);

        var deltas = node.DeltaComputations;
        context.ItemsSource.Replace(items[9], new Item(10, 50));
        Assert.Equal(2, table.Value[10]);
        Assert.Equal(deltas + 1, node.DeltaComputations);
    }

    [Fact]
    public void NoValueUpstream_GivesAnEmptyDictionary()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([new Item(1, 1), new Item(2, 2)]);
        context.OffsetSource.Set(0);
        var table = context.Items
            .Combine(context.Offset.Where(o => o > 0))
            .CollectToDictionary(p => p.Left.Key, p => p.Left.Value + p.Right);
        Assert.Empty(table.Value);

        context.OffsetSource.Set(10);
        Assert.Equal(12, table.Value[2]);

        context.OffsetSource.Set(0);
        Assert.Empty(table.Value);
    }

    [Fact]
    public void IdenticalCalls_ReuseTheNode()
    {
        using var context = new ItemsContext();
        var first = context.Items.CollectToDictionary(KeyOf, ValueOf);
        var second = context.Items.CollectToDictionary(KeyOf, ValueOf);
        var withComparer = context.Items.CollectToDictionary(KeyOf, ValueOf, Comparer<int>.Default);
        var withKeyComparer = context.Items.CollectToDictionary(KeyOf, ValueOf, keyComparer: EqualityComparer<int>.Default);
        var otherSelector = context.Items.CollectToDictionary(KeyOf, KeyOf);

        Assert.Same(first.Node, second.Node);
        Assert.NotSame(first.Node, withComparer.Node);
        Assert.NotSame(first.Node, withKeyComparer.Node);
        Assert.NotSame(first.Node, otherSelector.Node);
        Assert.Same(withComparer.Node, context.Items.CollectToDictionary(KeyOf, ValueOf, Comparer<int>.Default).Node);
    }

    private static int KeyOf(Item item) => item.Key;

    private static int ValueOf(Item item) => item.Value;

    [Fact]
    public void Selectors_RunOncePerNewItem()
    {
        using var context = new ItemsContext();
        var items = Enumerable.Range(0, 100).Select(i => new Item(i, i)).ToArray();
        context.ItemsSource.PushRange(items);
        var calls = 0;
        var table = context.Items.CollectToDictionary(i => { calls++; return i.Key; }, i => i.Value);
        Assert.Equal(100, table.Value.Count);
        Assert.Equal(100, calls);

        calls = 0;
        context.ItemsSource.Replace(items[5], items[5] with { Value = -5 });
        Assert.Equal(-5, table.Value[5]);
        Assert.Equal(1, calls);

        context.ItemsSource.Remove(items[6]);
        Assert.False(table.Value.ContainsKey(6));
        Assert.Equal(1, calls);

        // Pushed back: an item seen before, but no longer remembered once removed
        context.ItemsSource.Push(items[6]);
        Assert.Equal(6, table.Value[6]);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void NullItems_AreCountedLikeOtherItems()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([new Item(1, 1), new Item(2, 2), new Item(3, 4)]);
        var labels = context.Items.Select(i => i.Value % 2 == 0 ? null : $"odd{i.Key}");
        var table = labels.CollectToDictionary(l => l ?? "none", l => l is null);
        Assert.Equal(["none", "odd1"], table.Value.Keys.Order(StringComparer.Ordinal));
        Assert.True(table.Value["none"]);

        context.ItemsSource.Remove(new Item(2, 2));
        Assert.True(table.Value["none"]); // Item(3, 4) still maps to null
        context.ItemsSource.Remove(new Item(3, 4));
        Assert.Equal(["odd1"], table.Value.Keys);
    }

    [Fact]
    public void KeyComparer_IsUsedForKeysAndTheResult()
    {
        using var context = new ItemsContext();
        context.ItemsSource.PushRange([new Item(1, 1), new Item(2, 2)]);
        var table = context.Items.CollectToDictionary(i => i.Key == 1 ? "a" : "A", i => i.Value, Comparer<int>.Default, StringComparer.OrdinalIgnoreCase);
        Assert.Single(table.Value);
        Assert.Equal(1, table.Value["A"]);
        Assert.Same(StringComparer.OrdinalIgnoreCase, table.Value.KeyComparer);
    }

    [Fact]
    public void NullKey_IsAnError()
    {
        using var context = new ItemsContext();
        context.ItemsSource.Push(new Item(1, 1));
        var table = context.Items.CollectToDictionary(i => i.Key == 2 ? null! : i.Key.ToString(), i => i.Value);
        Assert.Single(table.Value);
        context.ItemsSource.Push(new Item(2, 2));
        Assert.True(table.GetValue().IsError);
        context.ItemsSource.Remove(new Item(2, 2));
        Assert.Single(table.Value);
    }

    [Fact]
    public async Task ConcurrentReadsAndChanges_StayConsistent()
    {
        using var context = new ItemsContext();
        const int Keys = 100;
        var model = Enumerable.Range(0, Keys).Select(k => new Item(k, 3)).ToList();
        context.ItemsSource.PushRange(model);

        // Batches move value units between items, so the sum of the values is constant
        var table = context.Items.CollectToDictionary(i => i.Key, i => i.Value);
        var total = table.Select(t => t.Values.Sum());
        using var stop = new CancellationTokenSource();
        var failures = 0;

        void Read()
        {
            while (!stop.IsCancellationRequested)
            {
                if (total.Value != Keys * 3)
                    Interlocked.Increment(ref failures);
            }
        }

        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(Read)).ToArray();
        var random = new Random(82);
        for (var round = 0; round < 2_000; round++)
        {
            var a = random.Next(Keys);
            var b = random.Next(Keys);
            if (a == b || model[a].Value == 0)
                continue;

            var movedFrom = model[a] with { Value = model[a].Value - 1 };
            var movedTo = model[b] with { Value = model[b].Value + 1 };
            using (IBatchOperation<Item> batch = context.ItemsSource.BeginBatch())
            {
                batch.Replace(model[a], movedFrom);
                batch.Replace(model[b], movedTo);
            }

            model[a] = movedFrom;
            model[b] = movedTo;
            if (round % 10 == 0)
                await Task.Yield();
        }

        stop.Cancel();
        await Task.WhenAll(readers);

        Assert.Equal(0, failures);
        Assert.Equal(model.ToDictionary(i => i.Key, i => i.Value), table.Value.ToDictionary());
    }
}
