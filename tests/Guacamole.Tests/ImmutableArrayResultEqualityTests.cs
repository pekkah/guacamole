using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #59: stages whose results are <see cref="ImmutableArray{T}"/> (or value tuples holding
/// one, as produced by <c>Combine</c>) compare them element-wise by default, so a stage that
/// recomputes to an equal array keeps its version and the stages downstream of it stay cached.
/// <see cref="EqualityComparer{T}.Default"/> compares immutable arrays by reference.
/// </summary>
public partial class ImmutableArrayResultEqualityTests
{
    /// <summary>Compares immutable arrays by their underlying array reference (the old default).</summary>
    private sealed class ByArrayReference : IEqualityComparer<ImmutableArray<string>>
    {
        public bool Equals(ImmutableArray<string> x, ImmutableArray<string> y) => x == y;
        public int GetHashCode(ImmutableArray<string> obj) => obj.GetHashCode();
    }

    /// <summary>Treats arrays of the same length as equal.</summary>
    private sealed class SameLength : IEqualityComparer<ImmutableArray<string>>
    {
        public bool Equals(ImmutableArray<string> x, ImmutableArray<string> y) => x.Length == y.Length;
        public int GetHashCode(ImmutableArray<string> obj) => obj.Length;
    }

    [ComputingContext]
    private partial class CsvContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<string> Csv { get; }
        public partial IncrementalValueProvider<string> Separator { get; }
    }

    private static ImmutableArray<string> Tokens(string csv) =>
        csv.Split(',').Select(static t => t.Trim()).ToImmutableArray();

    private sealed class Counter
    {
        public int Runs { get; private set; }

        public string Join(ImmutableArray<string> tokens, string separator = "|")
        {
            Runs++;
            return string.Join(separator, tokens);
        }
    }

    [Fact]
    public void UnreadSelectReturningEqualArray_CutsOffDownstream()
    {
        using var context = new CsvContext();
        var counter = new Counter();
        var tokens = context.Csv.Select(Tokens); // never read itself
        var joined = tokens.Select(t => counter.Join(t));
        context.CsvSource.Set("a,b");
        Assert.Equal("a|b", joined.Value);
        var version = context.Graph.Versions.GetOutputVersion(tokens.Node.Id);

        context.CsvSource.Set("a, b"); // recomputes tokens to a new, element-wise equal array

        Assert.Equal("a|b", joined.Value);
        Assert.Equal(1, counter.Runs);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(tokens.Node.Id));
    }

    [Fact]
    public void SelectReturningChangedArray_Propagates()
    {
        using var context = new CsvContext();
        var counter = new Counter();
        var joined = context.Csv.Select(Tokens).Select(t => counter.Join(t));
        context.CsvSource.Set("a,b");
        Assert.Equal("a|b", joined.Value);

        context.CsvSource.Set("a,c");
        Assert.Equal("a|c", joined.Value);
        Assert.Equal(2, counter.Runs);

        context.CsvSource.Set("c,a"); // same items, different order
        Assert.Equal("c|a", joined.Value);
        Assert.Equal(3, counter.Runs);

        context.CsvSource.Set("c,a,d"); // added item
        Assert.Equal("c|a|d", joined.Value);
        Assert.Equal(4, counter.Runs);
    }

    [Fact]
    public void CombineOfArrayStage_EqualTuple_CutsOffDownstream()
    {
        using var context = new CsvContext();
        var counter = new Counter();
        // The array stage compares by reference, so it counts every recompute as a change and
        // Combine runs again; the combined tuple is equal component-wise and stops it there.
        var tokens = context.Csv.Select(Tokens, new ByArrayReference());
        var combined = tokens.Combine(context.Separator);
        var joined = combined.Select(pair => counter.Join(pair.Item1, pair.Item2));
        context.CsvSource.Set("a,b");
        context.SeparatorSource.Set("+");
        Assert.Equal("a+b", joined.Value);
        var version = context.Graph.Versions.GetOutputVersion(combined.Node.Id);

        context.CsvSource.Set("a, b");

        Assert.Equal("a+b", joined.Value);
        Assert.Equal(1, counter.Runs);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(combined.Node.Id));
    }

    [Fact]
    public void CombineOfArrayStage_ChangedArrayOrOtherComponent_Propagates()
    {
        using var context = new CsvContext();
        var counter = new Counter();
        var joined = context.Csv.Select(Tokens).Combine(context.Separator)
            .Select(pair => counter.Join(pair.Item1, pair.Item2));
        context.CsvSource.Set("a,b");
        context.SeparatorSource.Set("+");
        Assert.Equal("a+b", joined.Value);

        context.CsvSource.Set("a,c");
        Assert.Equal("a+c", joined.Value);
        Assert.Equal(2, counter.Runs);

        context.SeparatorSource.Set("-");
        Assert.Equal("a-c", joined.Value);
        Assert.Equal(3, counter.Runs);
    }

    [Fact]
    public void CustomComparer_ByReference_OverridesElementWiseDefault()
    {
        using var context = new CsvContext();
        var counter = new Counter();
        var joined = context.Csv.Select(Tokens, new ByArrayReference()).Select(t => counter.Join(t));
        context.CsvSource.Set("a,b");
        Assert.Equal("a|b", joined.Value);

        context.CsvSource.Set("a, b"); // equal items, but a new array: a change for this comparer

        Assert.Equal("a|b", joined.Value);
        Assert.Equal(2, counter.Runs);
    }

    [Fact]
    public void CustomComparer_Lenient_OverridesElementWiseDefault()
    {
        using var context = new CsvContext();
        var counter = new Counter();
        var joined = context.Csv.Select(Tokens, new SameLength()).Select(t => counter.Join(t));
        context.CsvSource.Set("a,b");
        Assert.Equal("a|b", joined.Value);

        context.CsvSource.Set("x,y"); // different items, same length: unchanged for this comparer

        Assert.Equal("a|b", joined.Value);
        Assert.Equal(1, counter.Runs);
    }

    [Fact]
    public void OutputHandler_OnArrayStage_IsNotCalledForEqualArray()
    {
        using var context = new CsvContext();
        var calls = new List<string>();
        context.RegisterOutput(context.Csv.Select(Tokens), (tokens, _) => calls.Add(string.Join("|", tokens)));

        context.CsvSource.Set("a,b");
        context.CsvSource.Set("a, b");
        context.CsvSource.Set("a,c");

        Assert.Equal(["a|b", "a|c"], calls);
    }

    [Fact]
    public void OperatorNodes_UseStructuralDefault_UnlessAComparerIsPassed()
    {
        using var context = new CsvContext();
        var tokens = context.Csv.Select(Tokens);
        var words = tokens.SelectMany(static t => t);

        Assert.Same(ImmutableArrayEqualityComparer<string>.Default, Comparer(tokens.Node));
        Assert.Same(ImmutableArrayEqualityComparer<string>.Default, Comparer(tokens.Where(static t => t.Length > 0).Node));
        Assert.IsType<ValueTupleEqualityComparer<ImmutableArray<string>, string>>(Comparer(tokens.Combine(context.Separator).Node));
        Assert.Same(ImmutableArrayEqualityComparer<string>.Default, Comparer(words.Select(static w => ImmutableArray.Create(w)).Node));
        Assert.Same(ImmutableArrayEqualityComparer<string>.Default, Comparer(words.Select(static w => ImmutableArray.Create(w)).Where(static a => a.Length > 0).Node));
        Assert.Same(ImmutableArrayEqualityComparer<string>.Default, Comparer(context.Csv.SelectMany(static c => new[] { Tokens(c) }).Node));
        Assert.Same(ImmutableArrayEqualityComparer<string>.Default, Comparer(words.SelectMany(static w => new[] { Tokens(w) }).Node));
        Assert.IsType<ValueTupleEqualityComparer<string, ImmutableArray<string>>>(Comparer(words.Combine(tokens).Node));
        Assert.Same(ImmutableArrayEqualityComparer<ImmutableArray<string>>.Default, Comparer(words.Select(static w => ImmutableArray.Create(w)).Collect().Node));

        var custom = new ByArrayReference();
        Assert.Same(custom, Comparer(context.Csv.Select(Tokens, custom).Node));
        Assert.Same(custom, Comparer(tokens.Where(static t => t.Length > 0, custom).Node));

        // Types with value equality keep EqualityComparer<T>.Default
        Assert.Same(EqualityComparer<string>.Default, Comparer(context.Csv.Select(static c => c.ToUpperInvariant()).Node));
        Assert.Same(EqualityComparer<(string, string)>.Default, Comparer(context.Csv.Combine(context.Separator).Node));
    }

    private static object Comparer(object node) =>
        node.GetType().GetProperty(nameof(IHasComparer<int>.Comparer))!.GetValue(node)!;
}
