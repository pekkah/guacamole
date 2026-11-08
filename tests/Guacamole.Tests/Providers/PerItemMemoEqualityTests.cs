using System.Linq;
using Xunit;

namespace Guacamole.Tests.Providers;

/// <summary>
/// Issue #87: the per-item memo of collection <c>Select</c>, <c>Where</c> and <c>SelectMany</c>
/// matches input items by their own equality, so an item that equals an item of the previous
/// computation reuses that item's result, even if it differs in members its equality ignores.
/// These tests pin that documented behaviour (docs/performance-guide.md, per-item memoization).
/// </summary>
public partial class PerItemMemoEqualityTests
{
    [ComputingContext]
    private partial class NumbersContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<int> Numbers { get; }
    }

    /// <summary>An item whose equality only looks at <see cref="Key"/> (loose equality).</summary>
    private sealed record LooseItem(int Key, int Payload)
    {
        public bool Equals(LooseItem? other) => other is not null && Key == other.Key;

        public override int GetHashCode() => Key;
    }

    /// <summary>The same item with full (record) equality.</summary>
    private sealed record StrictItem(int Key, int Payload);

    [Fact]
    public void LooselyEqualItem_ReusesTheResultOfTheItemItEquals()
    {
        using var context = new NumbersContext();
        context.NumbersSource.Push(1);
        var calls = new PublicApi.CallCounter();
        var items = context.Numbers.Select(n => new LooseItem(n % 10, n));
        var payloads = items.Select(item => calls.Pass(item.Payload));
        var bigOnes = items.Where(item => calls.Pass(item.Payload > 5));
        var expanded = items.SelectMany(item => calls.Pass(new[] { item.Payload }));

        Assert.Equal([1], payloads.Values);
        Assert.Empty(bigOnes.Values);
        Assert.Equal([1], expanded.Values);
        Assert.Equal(3, calls.Count);

        // 11 maps to LooseItem(1, 11), which equals LooseItem(1, 1) of the previous computation:
        // every stage reuses that item's result instead of running its lambda
        context.NumbersSource.Push(11);

        Assert.Equal([1, 1], payloads.Values);
        Assert.Empty(bigOnes.Values);
        Assert.Equal([1, 1], expanded.Values);
        Assert.Equal(3, calls.Count);
    }

    [ComputingContext]
    private partial class LooseItemsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<LooseItem> Items { get; }
    }

    [Fact]
    public void CollectionSources_UseTheSameLooseEquality()
    {
        using var context = new LooseItemsContext();
        var payloads = context.Items.Select(item => item.Payload);
        context.ItemsSource.Push(new LooseItem(1, 1));
        Assert.Equal([1], payloads.Values);

        // A loosely equal item is a duplicate (Push ignores it), and replacing an item with an equal
        // one is not a change: the source keeps the item it has
        context.ItemsSource.Push(new LooseItem(1, 2));
        context.ItemsSource.Replace(new LooseItem(1, 1), new LooseItem(1, 3));

        Assert.Equal([1], payloads.Values);
        Assert.Equal(new LooseItem(1, 1).Payload, Assert.Single(context.ItemsSource.GetSnapshot()).Payload);
    }

    [Fact]
    public void ItemsWithFullEquality_AreComputedSeparately()
    {
        using var context = new NumbersContext();
        context.NumbersSource.Push(1);
        var calls = new PublicApi.CallCounter();
        var payloads = context.Numbers
            .Select(n => new StrictItem(n % 10, n))
            .Select(item => calls.Pass(item.Payload));
        Assert.Equal([1], payloads.Values);

        context.NumbersSource.Push(11);

        Assert.Equal([1, 11], payloads.Values.ToArray());
        Assert.Equal(2, calls.Count);
    }
}
