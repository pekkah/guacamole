using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// The item-level delta path (#61) may keep a previous result only when the new one is
/// indistinguishable from it. Equality that is looser than a value's contents (a custom struct
/// equality, or <c>0.0 == -0.0</c> for floating point) must not count, or stale results leak into
/// the output where a full recompute would produce the new ones.
/// </summary>
public partial class DeltaIdentityTests
{
    /// <summary>A struct whose equality ignores case, so equal values can still differ.</summary>
    public readonly struct Loose(string text) : IEquatable<Loose>
    {
        public string Text { get; } = text;

        public bool Equals(Loose other) => string.Equals(Text, other.Text, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is Loose other && Equals(other);

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Text);
    }

    [ComputingContext]
    private partial class NumbersContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<int> Numbers { get; }
        public partial IncrementalValueProvider<int> Offset { get; }
    }

    [Fact]
    public void CombineWithValue_LooselyEqualRightValue_PairsEveryItemWithTheNewValue()
    {
        using var context = new NumbersContext();
        context.OffsetSource.Set(0);
        var right = context.Offset.Select(o => new Loose(o % 2 == 0 ? "a" : "A"));
        var pairs = context.Numbers.Combine(right);

        context.NumbersSource.PushRange([1, 2]);
        Assert.Equal(["a1", "a2"], pairs.Values.Select(p => p.Item2.Text + p.Item1));

        // The right value changes to one that is equal but distinguishable, then a left item is added
        context.OffsetSource.Set(1);
        context.NumbersSource.Push(3);

        Assert.Equal(["A1", "A2", "A3"], pairs.Values.Select(p => p.Item2.Text + p.Item1));
    }

    [Fact]
    public void Select_ReplacedItemWithNegativeZeroResult_YieldsNegativeZero()
    {
        using var context = new NumbersContext();
        var results = context.Numbers.Select(x => x == 2 ? -0.0 : 0.0);

        context.NumbersSource.PushRange([1, 3]);
        Assert.Equal(2, results.Values.Count());

        context.NumbersSource.Replace(1, 2);

        var values = results.Values.ToArray();
        Assert.True(double.IsNegative(values[0]), "the replaced item's result must be -0.0, not the previous 0.0");
        Assert.Equal(double.PositiveInfinity, 1 / values[1]);
    }

    [Fact]
    public void Select_ReplacedItemWithNegativeZeroFloatResult_YieldsNegativeZero()
    {
        using var context = new NumbersContext();
        var results = context.Numbers.Select(x => x == 2 ? -0.0f : 0.0f);

        context.NumbersSource.PushRange([1, 3]);
        _ = results.Values.ToArray();

        context.NumbersSource.Replace(1, 2);

        Assert.True(float.IsNegative(results.Values.First()));
    }

    [Fact]
    public void CombineWithValue_SameImmutableArrayRightValue_StillUsesTheDeltaAndStaysCorrect()
    {
        using var context = new NumbersContext();
        context.OffsetSource.Set(0);
        var shared = ImmutableArray.Create(7, 8);
        var right = context.Offset.Select(_ => shared);
        var sums = context.Numbers.Combine(right).Select(p => p.Item1 + p.Item2.Sum());

        context.NumbersSource.PushRange([1, 2]);
        Assert.Equal([16, 17], sums.Values);

        context.NumbersSource.Push(3);
        Assert.Equal([16, 17, 18], sums.Values);
    }
}
