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
}
