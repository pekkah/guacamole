using System.Collections.Immutable;
using Xunit;

namespace Guacamole.Tests.Providers;

/// <summary>
/// A single-value <c>Where</c> that rejects its input produces "no value": a distinct,
/// non-error state that propagates through downstream operators without running their lambdas.
/// </summary>
public partial class WhereValueSemanticsTests
{
    [ComputingContext]
    private partial class NumberContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
        public partial IncrementalValueProvider<string> Label { get; }
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    [Fact]
    public void Where_WhenPredicateRejects_ReturnsNoValueInsteadOfError()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);

        var result = context.Number.Where(static x => x > 0).GetValue();

        Assert.True(result.IsNone);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsError);
        Assert.Null(result.ErrorValue);
    }

    [Fact]
    public void Where_WhenPredicateAccepts_ReturnsValue()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(5);

        var result = context.Number.Where(static x => x > 0).GetValue();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsNone);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void Value_WhenFilteredOut_ThrowsInvalidOperationExceptionExplainingNoValue()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);
        var positive = context.Number.Where(static x => x > 0);

        var ex = Assert.Throws<InvalidOperationException>(() => positive.Value);
        Assert.Contains("no value", ex.Message);
        Assert.Contains(nameof(IncrementalValueProvider<int>.TryGetValue), ex.Message);
    }

    [Fact]
    public void TryGetValue_ReportsWhetherAValueExists()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);
        var positive = context.Number.Where(static x => x > 0);

        Assert.False(positive.TryGetValue(out var missing));
        Assert.Equal(default, missing);

        context.NumberSource.Set(7);

        Assert.True(positive.TryGetValue(out var value));
        Assert.Equal(7, value);
    }

    [Fact]
    public void TryGetValue_WhenComputationFails_Throws()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(1);
        var failing = context.Number.Select<int, int>(static _ => throw new FormatException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => failing.TryGetValue(out _));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public void Select_AfterFilteredOutWhere_PropagatesNoValueWithoutInvokingSelector()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);
        var selectorCalls = 0;
        var doubled = context.Number
            .Where(static x => x > 0)
            .Select(x => { selectorCalls++; return x * 2; });

        var result = doubled.GetValue();

        Assert.True(result.IsNone);
        Assert.Equal(0, selectorCalls);

        context.NumberSource.Set(4);

        Assert.Equal(8, doubled.Value);
        Assert.Equal(1, selectorCalls);
    }

    [Fact]
    public void ChainedWhere_AfterFilteredOutWhere_DoesNotInvokePredicate()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);
        var predicateCalls = 0;
        var filtered = context.Number
            .Where(static x => x > 0)
            .Where(x => { predicateCalls++; return true; });

        Assert.True(filtered.GetValue().IsNone);
        Assert.Equal(0, predicateCalls);
    }

    [Fact]
    public void Combine_WithFilteredOutInput_HasNoValue()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);
        context.LabelSource.Set("x");

        var combined = context.Number.Where(static x => x > 0).Combine(context.Label);
        var reversed = context.Label.Combine(context.Number.Where(static x => x > 0));

        Assert.True(combined.GetValue().IsNone);
        Assert.True(reversed.GetValue().IsNone);
    }

    [Fact]
    public void Combine_WithFilteredOutAndFailingInputs_ReportsTheError()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);
        context.LabelSource.Set("x");
        var failing = context.Label.Select<string, string>(static _ => throw new FormatException("boom"));

        var result = context.Number.Where(static x => x > 0).Combine(failing).GetValue();

        Assert.True(result.IsError);
        Assert.Contains("boom", result.ErrorValue!.Message);
    }

    [Fact]
    public void SelectMany_FromFilteredOutValue_ProducesNoItems()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(-1);
        var selectorCalls = 0;

        var items = context.Number
            .Where(static x => x > 0)
            .SelectMany(x => { selectorCalls++; return Enumerable.Repeat(x, x); });

        Assert.Empty(items.Values);
        Assert.Equal(0, selectorCalls);
    }

    [Fact]
    public void CombineValues_WithFilteredOutValue_ProducesNoItems()
    {
        using var context = new NumberContext();
        context.ItemsSource.Push(1);
        context.ItemsSource.Push(2);
        context.NumberSource.Set(-1);

        var pairs = context.Items.Combine(context.Number.Where(static x => x > 0));

        Assert.Empty(pairs.Values);

        context.NumberSource.Set(3);

        Assert.Equal(new[] { (1, 3), (2, 3) }, pairs.Values.OrderBy(static p => p.Left).ToArray());
    }

    [Fact]
    public void Where_SwitchingBetweenValueAndNoValue_RecomputesCorrectly()
    {
        using var context = new NumberContext();
        var positive = context.Number.Where(static x => x > 0).Select(static x => x * 10);

        context.NumberSource.Set(2);
        Assert.Equal(20, positive.Value);

        context.NumberSource.Set(-2);
        Assert.True(positive.GetValue().IsNone);

        context.NumberSource.Set(2);
        Assert.Equal(20, positive.Value);
    }

    [Fact]
    public void RegisterOutput_DoesNotFireForNoValue()
    {
        using var context = new NumberContext();
        var received = new List<int>();
        context.RegisterOutput(context.Number.Where(static x => x > 0), (value, _) => received.Add(value));

        context.NumberSource.Set(-1);
        context.NumberSource.Set(-2);

        Assert.Empty(received);

        context.NumberSource.Set(3);

        Assert.Equal(new[] { 3 }, received);
    }

    [Fact]
    public void RegisterOutput_WhenValueReappearsAfterNoValue_FiresAgain()
    {
        using var context = new NumberContext();
        var received = new List<int>();
        context.RegisterOutput(context.Number.Where(static x => x > 0), (value, _) => received.Add(value));

        context.NumberSource.Set(3);
        context.NumberSource.Set(-3);
        context.NumberSource.Set(3);

        Assert.Equal(new[] { 3, 3 }, received);
    }

    [Fact]
    public void Collect_IsUnaffected_ByNoValueSemantics()
    {
        using var context = new NumberContext();
        context.ItemsSource.Push(1);

        var collected = context.Items.Collect();

        Assert.Equal(ImmutableArray.Create(1), collected.Value);
    }
}
