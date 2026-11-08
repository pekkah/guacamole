using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Pipeline;

/// <summary>
/// Issue #59: <see cref="DefaultEqualityComparer{T}"/> compares immutable arrays element-wise and
/// value tuples containing them component-wise, and (issue #100) immutable and immutable sorted
/// dictionaries entry by entry; every other type keeps
/// <see cref="EqualityComparer{T}.Default"/>.
/// </summary>
public class DefaultEqualityComparerTests
{
    [Fact]
    public void TypesWithValueEquality_UseEqualityComparerDefault()
    {
        Assert.Same(EqualityComparer<int>.Default, DefaultEqualityComparer<int>.Instance);
        Assert.Same(EqualityComparer<string>.Default, DefaultEqualityComparer<string>.Instance);
        Assert.Same(EqualityComparer<(int, string)>.Default, DefaultEqualityComparer<(int, string)>.Instance);
        Assert.Same(EqualityComparer<KeyValuePair<string, int>>.Default, DefaultEqualityComparer<KeyValuePair<string, int>>.Instance);
    }

    [Fact]
    public void OtherCollections_AreOutOfScope_AndKeepTheirOwnEquality()
    {
        Assert.Same(EqualityComparer<List<int>>.Default, DefaultEqualityComparer<List<int>>.Instance);
        Assert.Same(EqualityComparer<ImmutableList<int>>.Default, DefaultEqualityComparer<ImmutableList<int>>.Instance);
        Assert.False(DefaultEqualityComparer<List<int>>.Instance.Equals([1], [1]));
    }

    [Fact]
    public void ImmutableArray_IsComparedElementWise()
    {
        var comparer = DefaultEqualityComparer<ImmutableArray<int>>.Instance;

        Assert.Same(ImmutableArrayEqualityComparer<int>.Default, comparer);
        Assert.True(comparer.Equals([1, 2], [1, 2]));
        Assert.False(comparer.Equals([1, 2], [2, 1]));
        Assert.False(comparer.Equals([1, 2], [1, 2, 3]));
        Assert.True(comparer.Equals(default, default));
        Assert.False(comparer.Equals(default, []));
        Assert.Equal(comparer.GetHashCode([1, 2]), comparer.GetHashCode([1, 2]));
    }

    [Fact]
    public void NestedImmutableArrays_AreComparedElementWise()
    {
        var comparer = DefaultEqualityComparer<ImmutableArray<ImmutableArray<string>>>.Instance;
        ImmutableArray<ImmutableArray<string>> Build() => [["a", "b"], ["c"]];

        Assert.True(comparer.Equals(Build(), Build()));
        Assert.Equal(comparer.GetHashCode(Build()), comparer.GetHashCode(Build()));
        Assert.False(comparer.Equals(Build(), [["a", "b"], ["d"]]));
    }

    [Fact]
    public void TupleWithImmutableArray_IsComparedComponentWise()
    {
        var comparer = DefaultEqualityComparer<(ImmutableArray<int>, string?)>.Instance;

        Assert.IsType<ValueTupleEqualityComparer<ImmutableArray<int>, string?>>(comparer);
        Assert.True(comparer.Equals(([1, 2], "x"), ([1, 2], "x")));
        Assert.True(comparer.Equals(([1, 2], null), ([1, 2], null)));
        Assert.False(comparer.Equals(([1, 2], "x"), ([1, 2], "y")));
        Assert.False(comparer.Equals(([1, 2], "x"), ([1, 3], "x")));
        Assert.Equal(comparer.GetHashCode(([1, 2], "x")), comparer.GetHashCode(([1, 2], "x")));
        Assert.Equal(comparer.GetHashCode(([1, 2], null)), comparer.GetHashCode(([1, 2], null)));
    }

    [Fact]
    public void NestedTuplesAndThreeComponentTuples_AreComparedComponentWise()
    {
        var nested = DefaultEqualityComparer<((ImmutableArray<int>, int), string)>.Instance;
        Assert.True(nested.Equals((([1], 1), "x"), (([1], 1), "x")));
        Assert.False(nested.Equals((([1], 1), "x"), (([2], 1), "x")));

        var triple = DefaultEqualityComparer<(int, string, ImmutableArray<int>)>.Instance;
        Assert.IsType<ValueTupleEqualityComparer<int, string, ImmutableArray<int>>>(triple);
        Assert.True(triple.Equals((1, "x", [1, 2]), (1, "x", [1, 2])));
        Assert.False(triple.Equals((1, "x", [1, 2]), (1, "x", [1])));
        Assert.Equal(triple.GetHashCode((1, "x", [1, 2])), triple.GetHashCode((1, "x", [1, 2])));
    }

    [Fact]
    public void ImmutableArrayOfTuples_IsComparedStructurally()
    {
        var comparer = DefaultEqualityComparer<ImmutableArray<(string, ImmutableArray<int>)>>.Instance;

        Assert.True(comparer.Equals([("a", [1])], [("a", [1])]));
        Assert.False(comparer.Equals([("a", [1])], [("a", [2])]));
    }

    private sealed class NoEquality(int value)
    {
        public int Value { get; } = value;
    }

    [Fact]
    public void ImmutableDictionary_IsComparedEntryByEntry()
    {
        // Issue #100
        var comparer = DefaultEqualityComparer<ImmutableDictionary<string, int>>.Instance;
        Assert.Same(ImmutableDictionaryEqualityComparer<string, int>.Default, comparer);

        var a = ImmutableDictionary.CreateRange([KeyValuePair.Create("a", 1), KeyValuePair.Create("b", 2)]);
        var sameEntries = ImmutableDictionary<string, int>.Empty.Add("b", 2).Add("a", 1);

        Assert.True(comparer.Equals(a, a));
        Assert.True(comparer.Equals(a, sameEntries));
        Assert.True(comparer.Equals(sameEntries, a));
        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(sameEntries));
        Assert.True(comparer.Equals(ImmutableDictionary<string, int>.Empty, ImmutableDictionary.Create<string, int>()));
        Assert.True(comparer.Equals(null, null));

        Assert.False(comparer.Equals(a, a.SetItem("b", 3)));            // different value
        Assert.False(comparer.Equals(a, a.Remove("b").Add("c", 2)));    // different key, same count
        Assert.False(comparer.Equals(a, a.Add("c", 3)));                // different count
        Assert.False(comparer.Equals(a, a.Remove("b")));
        Assert.False(comparer.Equals(a, null));
        Assert.False(comparer.Equals(null, a));
    }

    [Fact]
    public void ImmutableDictionaries_WithDifferentKeyComparers_AreNotEqual()
    {
        var comparer = DefaultEqualityComparer<ImmutableDictionary<string, int>>.Instance;
        var ordinal = ImmutableDictionary.Create<string, int>(StringComparer.Ordinal).Add("a", 1);
        var ignoreCase = ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase).Add("a", 1);

        Assert.False(comparer.Equals(ordinal, ignoreCase));
        Assert.False(comparer.Equals(ignoreCase, ordinal));

        // Equal key comparers use the dictionaries' comparer to find the keys
        Assert.True(comparer.Equals(ignoreCase, ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase).Add("A", 1)));
        Assert.Equal(
            comparer.GetHashCode(ignoreCase),
            comparer.GetHashCode(ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase).Add("A", 1)));
        Assert.True(comparer.Equals(ordinal, ImmutableDictionary.Create<string, int>(StringComparer.Ordinal).Add("a", 1)));
    }

    [Fact]
    public void ImmutableDictionary_ComparesValuesWithTheirDefaultComparer()
    {
        // Nested structural values: immutable arrays and dictionaries are compared structurally too
        var nested = DefaultEqualityComparer<ImmutableDictionary<string, ImmutableArray<int>>>.Instance;
        ImmutableDictionary<string, ImmutableArray<int>> Build() => ImmutableDictionary<string, ImmutableArray<int>>.Empty.Add("a", [1, 2]).Add("b", []);
        Assert.True(nested.Equals(Build(), Build()));
        Assert.Equal(nested.GetHashCode(Build()), nested.GetHashCode(Build()));
        Assert.False(nested.Equals(Build(), Build().SetItem("a", [2, 1])));

        var deep = DefaultEqualityComparer<ImmutableDictionary<int, ImmutableDictionary<string, string?>>>.Instance;
        ImmutableDictionary<int, ImmutableDictionary<string, string?>> BuildDeep(string? value) =>
            ImmutableDictionary<int, ImmutableDictionary<string, string?>>.Empty.Add(1, ImmutableDictionary<string, string?>.Empty.Add("x", value));
        Assert.True(deep.Equals(BuildDeep("v"), BuildDeep("v")));
        Assert.True(deep.Equals(BuildDeep(null), BuildDeep(null)));
        Assert.Equal(deep.GetHashCode(BuildDeep(null)), deep.GetHashCode(BuildDeep(null)));
        Assert.False(deep.Equals(BuildDeep("v"), BuildDeep("w")));
        Assert.False(deep.Equals(BuildDeep("v"), BuildDeep(null)));

        // Values without value equality are compared by reference: only the same values are equal
        var plain = DefaultEqualityComparer<ImmutableDictionary<string, NoEquality>>.Instance;
        var value = new NoEquality(1);
        Assert.True(plain.Equals(ImmutableDictionary<string, NoEquality>.Empty.Add("a", value), ImmutableDictionary<string, NoEquality>.Empty.Add("a", value)));
        Assert.False(plain.Equals(ImmutableDictionary<string, NoEquality>.Empty.Add("a", value), ImmutableDictionary<string, NoEquality>.Empty.Add("a", new NoEquality(1))));
    }

    [Fact]
    public void ImmutableDictionary_HashIsIndependentOfInsertionOrder()
    {
        var comparer = DefaultEqualityComparer<ImmutableDictionary<int, string>>.Instance;
        var keys = Enumerable.Range(0, 1000).ToArray();
        var forward = keys.ToImmutableDictionary(k => k, k => $"v{k}");
        var backward = keys.Reverse().Aggregate(ImmutableDictionary<int, string>.Empty, (d, k) => d.Add(k, $"v{k}"));

        Assert.True(comparer.Equals(forward, backward));
        Assert.Equal(comparer.GetHashCode(forward), comparer.GetHashCode(backward));
    }

    [Fact]
    public void TupleWithImmutableDictionary_IsComparedComponentWise()
    {
        var comparer = DefaultEqualityComparer<(ImmutableDictionary<string, int>, int)>.Instance;
        Assert.IsType<ValueTupleEqualityComparer<ImmutableDictionary<string, int>, int>>(comparer);
        Assert.True(comparer.Equals((ImmutableDictionary<string, int>.Empty.Add("a", 1), 1), (ImmutableDictionary<string, int>.Empty.Add("a", 1), 1)));
    }

    [Fact]
    public void ImmutableSortedDictionary_IsComparedEntryByEntry()
    {
        var comparer = DefaultEqualityComparer<ImmutableSortedDictionary<string, ImmutableArray<int>>>.Instance;
        Assert.Same(ImmutableSortedDictionaryEqualityComparer<string, ImmutableArray<int>>.Default, comparer);

        ImmutableSortedDictionary<string, ImmutableArray<int>> Build() =>
            ImmutableSortedDictionary<string, ImmutableArray<int>>.Empty.Add("b", [2]).Add("a", [1]);
        Assert.True(comparer.Equals(Build(), Build()));
        Assert.Equal(comparer.GetHashCode(Build()), comparer.GetHashCode(Build()));
        Assert.False(comparer.Equals(Build(), Build().SetItem("a", [3])));
        Assert.False(comparer.Equals(Build(), Build().Remove("a").Add("c", [1])));
        Assert.False(comparer.Equals(Build(), Build().Add("c", [3])));
        Assert.False(comparer.Equals(Build(), null));

        // Different key comparers are not equal; equal ones look keys up by that comparer
        var ignoreCase = ImmutableSortedDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);
        Assert.False(DefaultEqualityComparer<ImmutableSortedDictionary<string, int>>.Instance.Equals(
            ImmutableSortedDictionary<string, int>.Empty.Add("a", 1), ignoreCase.Add("a", 1)));
        Assert.True(DefaultEqualityComparer<ImmutableSortedDictionary<string, int>>.Instance.Equals(ignoreCase.Add("a", 1), ignoreCase.Add("A", 1)));
        Assert.Equal(
            DefaultEqualityComparer<ImmutableSortedDictionary<string, int>>.Instance.GetHashCode(ignoreCase.Add("a", 1)),
            DefaultEqualityComparer<ImmutableSortedDictionary<string, int>>.Instance.GetHashCode(ignoreCase.Add("A", 1)));
    }

    [Fact]
    public void Comparers_AreBuiltOncePerType()
    {
        Assert.Same(DefaultEqualityComparer<(ImmutableArray<int>, int)>.Instance, DefaultEqualityComparer<(ImmutableArray<int>, int)>.Instance);
        Assert.Same(DefaultEqualityComparer<ImmutableArray<string>>.Instance, ImmutableArrayEqualityComparer<string>.Default);
    }
}
