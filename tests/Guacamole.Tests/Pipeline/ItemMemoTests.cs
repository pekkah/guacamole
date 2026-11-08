using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Pipeline;

/// <summary>
/// Issue #61: the per-item memo matches items by position and keeps its map across computations
/// instead of rebuilding it. It must still run the lambda exactly once per new distinct item, reuse
/// value instances for every other item, and remember exactly the items of the latest computation.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class ItemMemoTests
{
    /// <summary>A value with reference equality, so reuse is observable.</summary>
    private sealed class Box(string? item)
    {
        public string? Item { get; } = item;
    }

    private static void Mutate(List<string?> items, Random random)
    {
        switch (random.Next(8))
        {
            case 0:
                items.Insert(random.Next(items.Count + 1), $"i{random.Next(60)}");
                break;
            case 1 when items.Count > 0:
                items.RemoveAt(random.Next(items.Count));
                break;
            case 2 when items.Count > 2:
                {
                    // Remove a run of items
                    var start = random.Next(items.Count - 2);
                    items.RemoveRange(start, random.Next(1, Math.Min(5, items.Count - start)));
                    break;
                }
            case 3 when items.Count > 0:
                items[random.Next(items.Count)] = $"i{random.Next(60)}";
                break;
            case 4 when items.Count > 1:
                {
                    // Move an item
                    var from = random.Next(items.Count);
                    var item = items[from];
                    items.RemoveAt(from);
                    items.Insert(random.Next(items.Count + 1), item);
                    break;
                }
            case 5:
                items.Insert(random.Next(items.Count + 1), null);
                break;
            case 6 when items.Count > 0:
                // Duplicate an item
                items.Insert(random.Next(items.Count + 1), items[random.Next(items.Count)]);
                break;
            default:
                items.Add($"i{random.Next(60)}");
                break;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomEdits_RunTheLambdaOnlyForNewItems_AndReuseInstances(int seed)
    {
        var random = new Random(seed);
        var memo = new ItemMemo<string?, Box>();
        var calls = new List<string?>();
        Box Compute(string? item)
        {
            calls.Add(item);
            return new Box(item);
        }

        var items = Enumerable.Range(0, 20).Select(i => (string?)$"i{i}").ToList();
        var previous = new Dictionary<string, Box>();

        for (var step = 0; step < 500; step++)
        {
            var edits = random.Next(1, 4);
            for (var e = 0; e < edits; e++)
                Mutate(items, random);

            calls.Clear();
            var input = items.ToArray();
            var values = memo.Apply(input, Compute);

            Assert.Equal(input.Length, values.Length);
            for (var i = 0; i < input.Length; i++)
            {
                Assert.Equal(input[i], values[i].Item);
                if (input[i] is { } item && previous.TryGetValue(item, out var box))
                    Assert.Same(box, values[i]);
            }

            // Once per new distinct item, and once per null occurrence
            var expectedCalls = input.Where(item => item is not null).Distinct().Count(item => !previous.ContainsKey(item!))
                + input.Count(item => item is null);
            Assert.True(expectedCalls == calls.Count, $"seed {seed}, step {step}: {calls.Count} calls, expected {expectedCalls}");

            // Equal items share one instance
            for (var i = 0; i < input.Length; i++)
            {
                for (var k = i + 1; k < input.Length; k++)
                {
                    if (input[i] is not null && input[i] == input[k])
                        Assert.Same(values[i], values[k]);
                }
            }

            previous = new Dictionary<string, Box>();
            for (var i = 0; i < input.Length; i++)
            {
                if (input[i] is { } item)
                    previous[item] = values[i];
            }

            Assert.Equal(previous.Count, memo.Count);
        }
    }

    [Fact]
    public void UnchangedItems_ReturnThePreviousArray()
    {
        var memo = new ItemMemo<string, int>();
        var first = memo.Apply(new[] { "a", "b", "c" }, s => s.Length);
        var second = memo.Apply(new[] { "a", "b", "c" }, s => throw new InvalidOperationException("not called"));

        Assert.Same(first, second);
    }

    [Fact]
    public void LambdaException_LeavesTheMemoAsItWas()
    {
        var memo = new ItemMemo<string, Box>();
        var initial = memo.Apply(new[] { "a", "b", "c" }, s => new Box(s));

        Assert.Throws<InvalidOperationException>(() =>
            memo.Apply(new[] { "a", "x", "y", "boom", "c" }, s => s == "boom" ? throw new InvalidOperationException() : new Box(s)));
        Assert.Equal(3, memo.Count);

        var calls = 0;
        var again = memo.Apply(new[] { "a", "b", "c" }, s => { calls++; return new Box(s); });
        Assert.Equal(0, calls);
        Assert.Same(initial, again);
    }

    [Fact]
    public async Task ConcurrentApplies_ProduceCorrectValues_AndLeaveAConsistentMemo()
    {
        var memo = new ItemMemo<int, string>();
        var inputs = Enumerable.Range(0, 8)
            .Select(t => Enumerable.Range(t * 10, 500).ToArray())
            .ToArray();

        var tasks = inputs.Select(input => Task.Run(() =>
        {
            for (var r = 0; r < 50; r++)
            {
                var values = memo.Apply(input, i => i.ToString());
                for (var i = 0; i < input.Length; i++)
                    Assert.Equal(input[i].ToString(), values[i]);
            }
        }, TestContext.Current.CancellationToken));
        await Task.WhenAll(tasks);

        // Whatever computation was published last, a sequential apply sees a consistent memo
        var final = Enumerable.Range(0, 300).ToArray();
        var finalValues = memo.Apply(final, i => i.ToString());
        Assert.Equal(final.Select(i => i.ToString()), finalValues);
        Assert.Equal(300, memo.Count);
    }
    /// <summary>An input node that hands out one given delta.</summary>
    private sealed class FixedDeltaSource<T>(ArrayDelta<T> delta) : IDeltaSource<T>
    {
        public ArrayDelta<T>? GetDelta(T[] from, T[] to) => delta.Describes(from, to) ? delta : null;
    }

    [Fact]
    public void Delta_ComputesOnlyEditedItems_AndKeepsTheMapExact()
    {
        var memo = new ItemMemo<string, Box>();
        var runs = new List<string>();
        Box Compute(string item)
        {
            runs.Add(item);
            return new Box(item);
        }

        string[] first = ["a", "b", "c", "d", "b"];
        var firstValues = memo.Apply(first, Compute);
        runs.Clear();

        // Replace c by x, remove the first b, insert c (moved) and a new y
        string[] second = ["a", "x", "d", "c", "b", "y"];
        var delta = new ArrayDelta<string>(first, second,
            [new ItemEdit(1, -1), new ItemEdit(2, 1), new ItemEdit(-1, 3), new ItemEdit(-1, 5)]);
        var secondValues = memo.Apply(second, Compute, new FixedDeltaSource<string>(delta), out var step);

        Assert.Same(delta, step.InputDelta);
        Assert.Same(first, step.PreviousItems);
        Assert.Same(firstValues, step.PreviousValues);
        Assert.Equal(["x", "y"], runs); // the moved c and the unchanged items reuse their values
        Assert.Equal(second, secondValues.Select(v => v.Item));
        Assert.Same(firstValues[0], secondValues[0]);
        Assert.Same(firstValues[2], secondValues[3]); // c, moved
        Assert.Same(firstValues[4], secondValues[4]);
        Assert.Equal(6, memo.Count); // a, x, d, c, b, y

        // Removing every b and d through a delta forgets them
        string[] third = ["a", "x", "c", "y"];
        var removal = new ArrayDelta<string>(second, third, [new ItemEdit(2, -1), new ItemEdit(4, -1)]);
        memo.Apply(third, Compute, new FixedDeltaSource<string>(removal), out step);
        Assert.NotNull(step.InputDelta);
        Assert.Equal(4, memo.Count);

        // Without a matching delta the items are walked
        string[] fourth = ["a", "x", "c", "y", "z"];
        memo.Apply(fourth, Compute, new FixedDeltaSource<string>(removal), out step);
        Assert.Null(step.InputDelta);
        Assert.Equal(5, memo.Count);
    }

    [Fact]
    public void Delta_IdenticalValues_ReturnThePreviousArray()
    {
        var memo = new ItemMemo<string, int>();
        string[] first = ["a", "bb", "c"];
        var firstValues = memo.Apply(first, s => s.Length);

        // bb replaced by an item with the identical value (int)
        string[] second = ["a", "xx", "c"];
        var delta = new ArrayDelta<string>(first, second, [new ItemEdit(1, 1)]);
        var secondValues = memo.Apply(second, s => s.Length, new FixedDeltaSource<string>(delta), out _);

        Assert.Same(firstValues, secondValues);
        Assert.Equal(3, memo.Count);
    }

    [Fact]
    public void Delta_LambdaThrows_LeavesTheMemoUnchanged()
    {
        var memo = new ItemMemo<string, Box>();
        string[] first = ["a", "b"];
        memo.Apply(first, s => new Box(s));

        string[] second = ["a", "boom", "c"];
        var delta = new ArrayDelta<string>(first, second, [new ItemEdit(1, 1), new ItemEdit(-1, 2)]);
        Assert.Throws<InvalidOperationException>(() =>
            memo.Apply(second, s => s == "boom" ? throw new InvalidOperationException() : new Box(s), new FixedDeltaSource<string>(delta), out _));
        Assert.Equal(2, memo.Count);

        // The previous computation is still the base for the next delta
        string[] third = ["a", "b", "d"];
        var next = new ArrayDelta<string>(first, third, [new ItemEdit(-1, 2)]);
        var values = memo.Apply(third, s => new Box(s), new FixedDeltaSource<string>(next), out var step);
        Assert.NotNull(step.InputDelta);
        Assert.Equal(third, values.Select(v => v.Item));
        Assert.Equal(3, memo.Count);
    }
}
