using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// The <see cref="IncrementalValueProviderExtensions"/> operators (Select, Where, Combine,
/// SelectMany) and their comparer overloads, as documented in their XML docs and README
/// "Provider Extension Methods": argument validation, laziness, caching, equality cut-off and
/// node reuse.
/// </summary>
public class ValueOperatorTests
{
    private sealed class LengthComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => x?.Length == y?.Length;

        public int GetHashCode(string obj) => obj.Length;
    }

    [Fact]
    public void Operators_RejectNullDelegatesAndComparers()
    {
        using var context = new ApiContext();
        var number = context.Number;

        Assert.Throws<ArgumentNullException>(() => number.Select<int, int>(null!));
        Assert.Throws<ArgumentNullException>(() => number.Select(static x => x, null!));
        Assert.Throws<ArgumentNullException>(() => number.Select<int, int>(null!, EqualityComparer<int>.Default));
        Assert.Throws<ArgumentNullException>(() => number.Where(null!));
        Assert.Throws<ArgumentNullException>(() => number.Where(static x => true, null!));
        Assert.Throws<ArgumentNullException>(() => number.Where(null!, EqualityComparer<int>.Default));
        Assert.Throws<ArgumentNullException>(() => number.SelectMany<int, int>(null!));
        Assert.Throws<ArgumentNullException>(() => number.SelectMany(static x => new[] { x }, null!));
        Assert.Throws<ArgumentNullException>(() => number.SelectMany<int, int>(null!, EqualityComparer<int>.Default));
    }

    [Fact]
    public void Select_IsLazy_AndCachesUntilItsInputChanges()
    {
        using var context = new ApiContext();
        var calls = new CallCounter();
        var doubled = context.Number.Select(x => calls.Pass(x * 2));

        context.NumberSource.Set(1);
        Assert.Equal(0, calls.Count); // nothing read yet

        Assert.Equal(2, doubled.Value);
        Assert.Equal(2, doubled.Value);
        Assert.Equal(1, calls.Count);

        context.NumberSource.Set(2);
        Assert.Equal(4, doubled.Value);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public void Select_WithComparer_CutsOffDownstreamWork_AndOutputHandlers()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var byLength = context.Text.Select(static s => s.ToUpperInvariant(), new LengthComparer());
        var described = byLength.Select(s => downstream.Pass($"<{s}>"));
        var delivered = new List<string>();
        context.RegisterOutput(byLength, (s, _) => delivered.Add(s));

        context.TextSource.Set("abc");
        Assert.Equal("<ABC>", described.Value);

        context.TextSource.Set("xyz"); // same length: unchanged by the comparer
        Assert.Equal("<ABC>", described.Value);
        Assert.Equal(1, downstream.Count);
        Assert.Equal(["ABC"], delivered);

        context.TextSource.Set("abcd");
        Assert.Equal("<ABCD>", described.Value);
        Assert.Equal(2, downstream.Count);
        Assert.Equal(["ABC", "ABCD"], delivered);
    }

    [Fact]
    public void Select_WithoutComparer_UsesValueEquality_ForCutOff()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var parity = context.Number.Select(static x => x % 2);
        var described = parity.Select(p => downstream.Pass(p == 0 ? "even" : "odd"));

        context.NumberSource.Set(1);
        Assert.Equal("odd", described.Value);
        context.NumberSource.Set(3);
        Assert.Equal("odd", described.Value);

        Assert.Equal(1, downstream.Count);
    }

    [Fact]
    public void Where_WithComparer_CutsOffDownstreamWork()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var nonEmpty = context.Text.Where(static s => s.Length > 0, new LengthComparer());
        var described = nonEmpty.Select(s => downstream.Pass(s.Length));

        context.TextSource.Set("ab");
        Assert.Equal(2, described.Value);
        context.TextSource.Set("cd");
        Assert.Equal(2, described.Value);

        Assert.Equal(1, downstream.Count);
    }

    [Fact]
    public void Combine_PairsTheValuesInArgumentOrder_AndRecomputesWhenEitherChanges()
    {
        using var context = new ApiContext();
        var pair = context.Number.Combine(context.Text);

        context.NumberSource.Set(1);
        context.TextSource.Set("a");
        Assert.Equal((1, "a"), pair.Value);

        context.TextSource.Set("b");
        Assert.Equal((1, "b"), pair.Value);

        context.NumberSource.Set(2);
        Assert.Equal((2, "b"), pair.Value);
    }

    [Fact]
    public void Combine_ReportsTheFirstInputsErrorBeforeTheSecondsError()
    {
        using var context = new ApiContext();
        var failingFirst = context.Number.Select<int, int>(static _ => throw new InvalidOperationException("first"));
        var failingSecond = context.Text.Select<string, string>(static _ => throw new InvalidOperationException("second"));
        context.NumberSource.Set(1);
        context.TextSource.Set("a");

        Assert.Equal("first", ResultAssert.Error(failingFirst.Combine(failingSecond).GetValue()).Message);
        Assert.Equal("second", ResultAssert.Error(failingSecond.Combine(failingFirst).GetValue()).Message);
    }

    [Fact]
    public void SelectMany_ProducesTheItemsInTheOrderTheSelectorReturnsThem()
    {
        using var context = new ApiContext();
        var chars = context.Text.SelectMany(static s => s.ToCharArray());

        context.TextSource.Set("cab");

        Assert.Equal(['c', 'a', 'b'], chars.Values);
    }

    [Fact]
    public void SelectMany_WithComparer_CutsOffDownstreamWork()
    {
        using var context = new ApiContext();
        var downstream = new CallCounter();
        var words = context.Text.SelectMany(static s => s.Split(' '), new LengthComparer());
        var total = words.Collect().Select(w => downstream.Pass(w.Length));

        context.TextSource.Set("ab cd");
        Assert.Equal(2, total.Value);

        context.TextSource.Set("xy zw"); // same lengths: unchanged by the comparer
        Assert.Equal(2, total.Value);
        Assert.Equal(1, downstream.Count);
    }

    [Fact]
    public void IdenticalOperatorCalls_ShareOneNodeAndCache()
    {
        using var context = new ApiContext();
        var calls = new CallCounter();
        Func<int, int> selector = x => calls.Pass(x + 1);
        context.NumberSource.Set(1);

        Assert.Equal(2, context.Number.Select(selector).Value);
        Assert.Equal(2, context.Number.Select(selector).Value);

        Assert.Equal(1, calls.Count);
        Assert.Equal(context.Number.Select(selector), context.Number.Select(selector));
    }

    [Fact]
    public void OperatorsOnUnchangedInputs_DoNotRerunAfterAnUnrelatedSourceChanges()
    {
        using var context = new ApiContext();
        var calls = new CallCounter();
        var doubled = context.Number.Select(x => calls.Pass(x * 2));
        context.NumberSource.Set(1);
        Assert.Equal(2, doubled.Value);

        context.TextSource.Set("unrelated");
        context.ItemsSource.Push(new Item("unrelated", 0));

        Assert.Equal(2, doubled.Value);
        Assert.Equal(1, calls.Count);
    }
}
