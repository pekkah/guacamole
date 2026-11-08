using System.Collections.Immutable;
using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// "No value" from a single-value <c>Where</c> through every downstream operator (README "A
/// filtered-out single value is 'no value', not an error", the Where XML docs and the migration
/// guide's table): Select/Where/Combine propagate None without running their lambdas, SelectMany and
/// collection Combine produce no items, output handlers are not called, <c>.Value</c> throws and
/// <c>TryGetValue</c> returns false.
/// </summary>
public class NoValuePropagationTests
{
    private static (ApiContext Context, IncrementalValueProvider<int> Filtered) FilteredOut()
    {
        var context = new ApiContext();
        context.NumberSource.Set(-1);
        context.TextSource.Set("text");
        context.ItemsSource.Push(new Item("a", 1));
        return (context, context.Number.Where(static x => x > 0));
    }

    private static int Throws(int _) => throw new InvalidOperationException("lambda must not run for no value");

    [Fact]
    public void Where_RejectingItsInput_IsNone()
    {
        var (context, filtered) = FilteredOut();
        using var _ = context;

        ResultAssert.None(filtered.GetValue());
        Assert.False(filtered.TryGetValue(out var value));
        Assert.Equal(default, value);
        var ex = Assert.Throws<InvalidOperationException>(() => filtered.Value);
        Assert.Contains("no value", ex.Message);
    }

    [Fact]
    public void SingleValueOperators_PropagateNone_WithoutRunningTheirLambdas()
    {
        var (context, filtered) = FilteredOut();
        using var _ = context;

        ResultAssert.None(filtered.Select(Throws).GetValue());
        ResultAssert.None(filtered.Select(Throws, EqualityComparer<int>.Default).GetValue());
        ResultAssert.None(filtered.Where(static x => Throws(x) > 0).GetValue());
        ResultAssert.None(filtered.Where(static x => Throws(x) > 0, EqualityComparer<int>.Default).GetValue());
        ResultAssert.None(filtered.Combine(context.Text).GetValue());
        ResultAssert.None(context.Text.Combine(filtered).GetValue());
        ResultAssert.None(filtered.Select(static x => x).Combine(filtered).Select(static p => Throws(p.Item1)).GetValue());
    }

    [Fact]
    public void Combine_ReportsAnErrorRatherThanNone()
    {
        var (context, filtered) = FilteredOut();
        using var _ = context;
        var failing = context.Text.Select<string, int>(static _ => throw new InvalidOperationException("boom"));

        Assert.Equal("boom", ResultAssert.Error(filtered.Combine(failing).GetValue()).Message);
        Assert.Equal("boom", ResultAssert.Error(failing.Combine(filtered).GetValue()).Message);
    }

    [Fact]
    public void CollectionOperators_ProduceNoItems_WithoutRunningTheirLambdas()
    {
        var (context, filtered) = FilteredOut();
        using var _ = context;

        Assert.Empty(ResultAssert.Success(filtered.SelectMany(static x => new[] { Throws(x) }).GetValues()));
        Assert.Empty(ResultAssert.Success(filtered.SelectMany(static x => new[] { Throws(x) }, EqualityComparer<int>.Default).GetValues()));
        Assert.Empty(ResultAssert.Success(context.Items.Combine(filtered).GetValues()));
        Assert.Empty(context.Items.Combine(filtered).Select(static p => Throws(p.Right)).Values);

        // ...and downstream of those, Collect is an empty array, not None
        var collected = context.Items.Combine(filtered).Collect();
        Assert.Empty(ResultAssert.Success(collected.GetValue()));
    }

    [Fact]
    public void Flatten_OfAFilteredOutNestedArray_HasNoItems()
    {
        using var context = new ApiContext();
        context.TextSource.Set("ab");
        var nested = context.Text
            .Select(static s => ImmutableArray.Create(s.ToImmutableArray()))
            .Where(static n => n.Length > 5);

        Assert.Empty(nested.Flatten().Values);
    }

    [Fact]
    public void Merge_OfAnInputWithNoItemsBecauseOfNone_ListsTheOtherInputsItems()
    {
        var (context, filtered) = FilteredOut();
        using var _ = context;
        var none = filtered.SelectMany(static x => new[] { new Item("n", x) });

        Assert.Equal([new Item("a", 1)], context.Items.Merge(none).Values);
    }

    [Fact]
    public void OutputHandlers_OnNoneProviders_AreNotCalled_AndCalledWhenAValueAppears()
    {
        using var context = new ApiContext();
        var filtered = context.Number.Where(static x => x > 0);
        var deliveredValues = new List<int>();
        var deliveredPairs = new List<(int, string)>();
        context.RegisterOutput(filtered.Select(static x => x * 10), (v, _) => deliveredValues.Add(v));
        context.RegisterOutput(filtered.Combine(context.Text), (v, _) => deliveredPairs.Add(v));
        context.TextSource.Set("t");

        context.NumberSource.Set(-1);
        context.NumberSource.Set(-2);
        Assert.Empty(deliveredValues);
        Assert.Empty(deliveredPairs);

        context.NumberSource.Set(3);
        Assert.Equal([30], deliveredValues);
        Assert.Equal([(3, "t")], deliveredPairs);
    }

    [Fact]
    public void OutputHandler_WhenTheSameValueReappearsAfterNone_IsCalledAgain()
    {
        using var context = new ApiContext();
        var filtered = context.Number.Where(static x => x > 0).Select(static x => x > 100);
        var delivered = new List<bool>();
        context.RegisterOutput(filtered, (v, _) => delivered.Add(v));

        context.NumberSource.Set(1);   // false
        context.NumberSource.Set(2);   // false again: not delivered
        context.NumberSource.Set(-1);  // none
        context.NumberSource.Set(3);   // false reappears: delivered again

        Assert.Equal([false, false], delivered);
    }

    [Fact]
    public void CollectionOutputHandler_DownstreamOfNone_ReceivesTheEmptyCollection()
    {
        using var context = new ApiContext();
        var filtered = context.Number.Where(static x => x > 0);
        var deliveries = new List<int[]>();
        context.RegisterOutput(filtered.SelectMany(static x => Enumerable.Range(0, x)), (v, _) => deliveries.Add(v.ToArray()));

        context.NumberSource.Set(2);
        context.NumberSource.Set(-1);

        Assert.Equal(2, deliveries.Count);
        Assert.Equal([0, 1], deliveries[0]);
        Assert.Empty(deliveries[1]);
    }
}
