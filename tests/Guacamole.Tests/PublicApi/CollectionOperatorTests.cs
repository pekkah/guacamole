using System.Collections.Immutable;
using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// The <see cref="IncrementalValuesProviderExtensions"/> operators (Select, Where, SelectMany,
/// Collect, Combine, Merge, Flatten) and their comparer overloads, as documented in their XML docs,
/// README "Provider Extension Methods" and the performance guide: argument validation, ordering,
/// per-item memoization and equality cut-off.
/// </summary>
public class CollectionOperatorTests
{
    private sealed class NameComparer : IEqualityComparer<Item>
    {
        public bool Equals(Item? x, Item? y) => x?.Name == y?.Name;

        public int GetHashCode(Item obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }

    [Fact]
    public void Operators_RejectNullDelegatesAndComparers()
    {
        using var context = new ApiContext();
        var items = context.Items;
        var comparer = EqualityComparer<Item>.Default;

        Assert.Throws<ArgumentNullException>(() => items.Select<Item, Item>(null!));
        Assert.Throws<ArgumentNullException>(() => items.Select(static i => i, null!));
        Assert.Throws<ArgumentNullException>(() => items.Select<Item, Item>(null!, comparer));
        Assert.Throws<ArgumentNullException>(() => items.Where(null!));
        Assert.Throws<ArgumentNullException>(() => items.Where(static _ => true, null!));
        Assert.Throws<ArgumentNullException>(() => items.Where(null!, comparer));
        Assert.Throws<ArgumentNullException>(() => items.SelectMany<Item, Item>(null!));
        Assert.Throws<ArgumentNullException>(() => items.SelectMany(static i => new[] { i }, null!));
        Assert.Throws<ArgumentNullException>(() => items.SelectMany<Item, Item>(null!, comparer));
        Assert.Throws<ArgumentNullException>(() => items.Collect(null!));
    }

    [Fact]
    public void Select_Where_SelectMany_PreserveTheSourceOrder()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("c", 3), new Item("a", 1), new Item("d", 4), new Item("b", 2)]);

        Assert.Equal(["c", "a", "d", "b"], context.Items.Select(static i => i.Name).Values);
        Assert.Equal(["d", "b"], context.Items.Where(static i => i.Value % 2 == 0).Select(static i => i.Name).Values);
        Assert.Equal(
            ["c", "c", "a", "a", "d", "d", "b", "b"],
            context.Items.SelectMany(static i => new[] { i.Name, i.Name }).Values);
    }

    [Fact]
    public void Collect_ReturnsTheItemsInOrder_AndIsEmptyForAnEmptySource()
    {
        using var context = new ApiContext();
        var collected = context.Items.Collect();

        Assert.Empty(collected.Value);

        context.ItemsSource.PushRange([new Item("b", 2), new Item("a", 1)]);

        Assert.Equal([new Item("b", 2), new Item("a", 1)], collected.Value);
    }

    [Fact]
    public void Select_RunsItsLambdaOnlyForNewOrChangedItems()
    {
        using var context = new ApiContext();
        var calls = new CallCounter();
        var names = context.Items.Select(i => calls.Pass(i.Name));
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2), new Item("c", 3)]);
        Assert.Equal(3, names.Values.Count());

        context.ItemsSource.Replace(new Item("b", 2), new Item("B", 2));
        Assert.Equal(["a", "B", "c"], names.Values);

        context.ItemsSource.Push(new Item("d", 4));
        Assert.Equal(["a", "B", "c", "d"], names.Values);

        Assert.Equal(5, calls.Count);
    }

    [Fact]
    public void Select_ReusesThePreviousResultInstance_ForAnUnchangedItem()
    {
        using var context = new ApiContext();
        var wrapped = context.Items.Select(static i => new object());
        context.ItemsSource.Push(new Item("a", 1));
        var first = wrapped.Values.Single();

        context.ItemsSource.Push(new Item("b", 2));

        Assert.Same(first, wrapped.Values.First());
    }

    [Fact]
    public void Select_WithComparer_CutsOffDownstreamWork()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var byName = context.Items.Select(static i => i, new NameComparer());
        var total = byName.Collect().Select(items => downstream.Pass(items.Length));
        context.ItemsSource.Push(new Item("a", 1));
        Assert.Equal(1, total.Value);

        context.ItemsSource.Replace(new Item("a", 1), new Item("a", 99)); // same name

        Assert.Equal(1, total.Value);
        Assert.Equal(1, downstream.Count);
    }

    [Fact]
    public void Where_WithComparer_CutsOffOutputHandlers()
    {
        using var context = new ApiContext();
        var byName = context.Items.Where(static i => i.Value > 0, new NameComparer());
        var deliveries = 0;
        context.RegisterOutput(byName, (_, _) => deliveries++);

        context.ItemsSource.Push(new Item("a", 1));
        context.ItemsSource.Replace(new Item("a", 1), new Item("a", 2)); // same name

        Assert.Equal(1, deliveries);
    }

    [Fact]
    public void SelectMany_WithComparer_CutsOffDownstreamWork()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var expanded = context.Items.SelectMany(static i => new[] { i }, new NameComparer());
        var total = expanded.Collect().Select(items => downstream.Pass(items.Length));
        context.ItemsSource.Push(new Item("a", 1));
        Assert.Equal(1, total.Value);

        context.ItemsSource.Replace(new Item("a", 1), new Item("a", 2));

        Assert.Equal(1, total.Value);
        Assert.Equal(1, downstream.Count);
    }

    [Fact]
    public void Collect_OfEqualItemsInTheSameOrder_KeepsDownstreamCached_ADifferentOrderDoesNot()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var values = context.Items.Select(static i => i.Value).Collect();
        var sum = values.Select(v => downstream.Pass(v.Sum()));
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2)]);
        Assert.Equal(3, sum.Value);

        // Same values, same order
        context.ItemsSource.Replace(new Item("a", 1), new Item("x", 1));
        Assert.Equal(3, sum.Value);
        Assert.Equal(1, downstream.Count);

        // Same values, different order: a change
        context.ItemsSource.Remove(new Item("x", 1));
        context.ItemsSource.Push(new Item("x", 1));
        Assert.Equal(3, sum.Value);
        Assert.Equal(2, downstream.Count);
    }

    [Fact]
    public void Collect_WithComparer_ComparesTheItemsWithIt()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var collected = context.Items.Collect(new NameComparer());
        var count = collected.Select(items => downstream.Pass(items.Length));
        context.ItemsSource.Push(new Item("a", 1));
        Assert.Equal(1, count.Value);

        context.ItemsSource.Replace(new Item("a", 1), new Item("a", 2));

        Assert.Equal(1, count.Value);
        Assert.Equal(1, downstream.Count);
    }

    [Fact]
    public void Combine_PairsEveryItemWithTheValue_InItemOrder()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("b", 2), new Item("a", 1)]);
        context.NumberSource.Set(10);

        var scaled = context.Items.Combine(context.Number).Select(static pair => pair.Left.Value * pair.Right);

        Assert.Equal([20, 10], scaled.Values);

        context.NumberSource.Set(100);
        Assert.Equal([200, 100], scaled.Values);
    }

    [Fact]
    public void Combine_WithAFailingValue_IsAnError()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));

        // Number was never set
        var result = context.Items.Combine(context.Number).GetValues();

        Assert.Contains("never been initialized", ResultAssert.Error(result).Message);
    }

    [Fact]
    public void Merge_ListsTheFirstProvidersItemsThenTheSeconds()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2)]);
        context.OtherItemsSource.PushRange([new Item("z", 26), new Item("a", 1)]);

        var merged = context.Items.Merge(context.OtherItems);

        // Concatenation: items present in both appear twice
        Assert.Equal([new Item("a", 1), new Item("b", 2), new Item("z", 26), new Item("a", 1)], merged.Values);
        Assert.Equal(
            [new Item("z", 26), new Item("a", 1), new Item("a", 1), new Item("b", 2)],
            context.OtherItems.Merge(context.Items).Values);
    }

    [Fact]
    public void Merge_FollowsChangesOfEitherInput()
    {
        using var context = new ApiContext();
        var merged = context.Items.Merge(context.OtherItems).Select(static i => i.Name);

        context.OtherItemsSource.Push(new Item("o", 0));
        Assert.Equal(["o"], merged.Values);

        context.ItemsSource.Push(new Item("i", 0));
        Assert.Equal(["i", "o"], merged.Values);

        context.OtherItemsSource.Clear();
        Assert.Equal(["i"], merged.Values);
    }

    [Fact]
    public void Flatten_OfACollectionOfArrays_ConcatenatesThemInOrder()
    {
        using var context = new ApiContext();
        context.WordsSource.PushRange(["ab", "c"]);

        var chars = context.Words.Select(static w => w.ToImmutableArray()).Flatten();

        Assert.Equal(['a', 'b', 'c'], chars.Values);
    }

    [Fact]
    public void Flatten_OfANestedArrayValue_ConcatenatesTheInnerArraysInOrder()
    {
        using var context = new ApiContext();
        context.WordsSource.PushRange(["ab", "", "c"]);

        var nested = context.Words.Select(static w => w.ToImmutableArray()).Collect();
        var chars = nested.Flatten();

        Assert.Equal(['a', 'b', 'c'], chars.Values);

        context.WordsSource.Remove("ab");
        Assert.Equal(['c'], chars.Values);
    }
}
