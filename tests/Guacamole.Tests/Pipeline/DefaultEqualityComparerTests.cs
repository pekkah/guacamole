using System.Collections.Generic;
using System.Collections.Immutable;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Pipeline;

/// <summary>
/// Issue #59: <see cref="DefaultEqualityComparer{T}"/> compares immutable arrays element-wise and
/// value tuples containing them component-wise; every other type keeps
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

    [Fact]
    public void Comparers_AreBuiltOncePerType()
    {
        Assert.Same(DefaultEqualityComparer<(ImmutableArray<int>, int)>.Instance, DefaultEqualityComparer<(ImmutableArray<int>, int)>.Instance);
        Assert.Same(DefaultEqualityComparer<ImmutableArray<string>>.Instance, ImmutableArrayEqualityComparer<string>.Default);
    }
}
