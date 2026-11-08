using System.Collections.Immutable;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #108: <see cref="EquatableArray{T}"/> gives records with array members value equality.
/// </summary>
public class EquatableArrayTests
{
    private sealed record Module(string Name, EquatableArray<string> Members);

    private sealed record ModuleWithImmutableArray(string Name, ImmutableArray<string> Members);

    [Fact]
    public void SeparatelyBuiltArrays_WithEqualElements_AreEqual()
    {
        EquatableArray<string> first = ImmutableArray.Create("a", "b");
        EquatableArray<string> second = ImmutableArray.Create("a", "b");

        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [InlineData(new[] { "a", "b" }, new[] { "b", "a" })]
    [InlineData(new[] { "a", "b" }, new[] { "a" })]
    [InlineData(new[] { "a" }, new string[0])]
    [InlineData(new[] { "a" }, new[] { "A" })]
    public void DifferentElementsOrOrder_AreNotEqual(string[] left, string[] right)
    {
        var first = left.ToEquatableArray();
        var second = right.ToEquatableArray();

        Assert.False(first.Equals(second));
        Assert.True(first != second);
    }

    [Fact]
    public void Default_IsEmpty_AndEqualToEmptyArrays()
    {
        var value = default(EquatableArray<int>);

        Assert.True(value.IsEmpty);
        Assert.Empty(value);
        Assert.Equal(value, EquatableArray<int>.Empty);
        Assert.Equal(value, new EquatableArray<int>(ImmutableArray<int>.Empty));
        Assert.Equal(value, new EquatableArray<int>(default));
        Assert.Equal(value, (EquatableArray<int>)[]);
        Assert.Equal(value.GetHashCode(), new EquatableArray<int>(ImmutableArray<int>.Empty).GetHashCode());
        Assert.False(value.AsImmutableArray().IsDefault);
        Assert.True(value.AsSpan().IsEmpty);
        Assert.Throws<IndexOutOfRangeException>(() => value[0]);
        foreach (var _ in value)
            Assert.Fail("A default array has no elements");
    }

    [Fact]
    public void ReadOnlyListMembers_ExposeTheElements()
    {
        EquatableArray<int> numbers = [1, 2, 3];
        IReadOnlyList<int> list = numbers;

        Assert.Equal(3, numbers.Count);
        Assert.Equal(2, numbers[1]);
        Assert.Equal([1, 2, 3], list);
        Assert.Equal([1, 2, 3], numbers.AsImmutableArray());
        Assert.Equal(6, numbers.Sum());
        Assert.Equal("EquatableArray[3]", numbers.ToString());
    }

    [Fact]
    public void Wrapping_DoesNotCopy()
    {
        var array = ImmutableArray.Create(1, 2, 3);

        var wrapped = array.AsEquatableArray();

        Assert.True(wrapped.AsImmutableArray() == array); // same underlying array
    }

    [Fact]
    public void NullElements_AreSupported()
    {
        EquatableArray<string?> first = ["a", null];
        EquatableArray<string?> second = ["a", null];

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, (EquatableArray<string?>)[null, "a"]);
    }

    [Fact]
    public void NestedArrays_AreComparedElementWise()
    {
        EquatableArray<EquatableArray<int>> first = [[1, 2], [3]];
        EquatableArray<EquatableArray<int>> second = [[1, 2], [3]];
        EquatableArray<ImmutableArray<int>> immutableFirst = [ImmutableArray.Create(1, 2)];
        EquatableArray<ImmutableArray<int>> immutableSecond = [ImmutableArray.Create(1, 2)];

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, (EquatableArray<EquatableArray<int>>)[[1, 2], [4]]);

        // Like the pipeline's default comparer, nested immutable arrays compare element by element.
        Assert.Equal(immutableFirst, immutableSecond);
        Assert.Equal(immutableFirst.GetHashCode(), immutableSecond.GetHashCode());
    }

    [Fact]
    public void RecordMember_GivesTheRecordValueEquality()
    {
        var first = new Module("m", ["a", "b"]);
        var second = new Module("m", ["a", "b"]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Members = ["a"] });
        Assert.Equal(new Module("m", default), new Module("m", []));

        // An ImmutableArray member is compared by reference by the compiler-generated Equals.
        Assert.NotEqual(
            new ModuleWithImmutableArray("m", ["a", "b"]),
            new ModuleWithImmutableArray("m", ["a", "b"]));
    }

    [Fact]
    public void EqualArrays_AreOneKeyInHashedCollections()
    {
        var set = new HashSet<EquatableArray<string>> { ImmutableArray.Create("a"), ImmutableArray.Create("a"), ImmutableArray.Create("b") };

        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void DefaultEqualityComparer_UsesEquatableArrayEquals()
    {
        // EquatableArray<T> already has value equality, so the pipeline's default comparer is the
        // framework default for it, and top-level immutable arrays are still compared structurally.
        Assert.Same(EqualityComparer<EquatableArray<int>>.Default, DefaultEqualityComparer<EquatableArray<int>>.Instance);
        Assert.NotSame(EqualityComparer<ImmutableArray<int>>.Default, DefaultEqualityComparer<ImmutableArray<int>>.Instance);
        Assert.True(DefaultEqualityComparer<ImmutableArray<int>>.Instance.Equals([1, 2], [1, 2]));
    }
}
