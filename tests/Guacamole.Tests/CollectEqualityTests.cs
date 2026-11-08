using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #51: <c>Collect()</c> compares the collected arrays element-wise, so a recomputed array
/// with equal items in the same order keeps the stage's version and the stages downstream of it
/// stay cached. <see cref="ImmutableArray{T}"/>'s default equality compares the underlying array
/// references, which made every recompute count as a change.
/// </summary>
public partial class CollectEqualityTests
{
    private sealed class IgnoreCase : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(x, y);
        public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
    }

    private static readonly IEqualityComparer<string> ByReference = ReferenceEqualityComparer.Instance;

    [ComputingContext]
    private partial class WordsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Words { get; }
    }

    private sealed class Joined
    {
        private int _runs;

        public Joined(IncrementalValueProvider<ImmutableArray<string>> collected)
        {
            Collected = collected;
            Text = collected.Select(all => { _runs++; return string.Join(",", all); });
        }

        public IncrementalValueProvider<ImmutableArray<string>> Collected { get; }
        public IncrementalValueProvider<string> Text { get; }
        public int Runs => _runs;
    }

    private static long Version(ComputingContextBase context, IncrementalValueProvider<ImmutableArray<string>> provider) =>
        context.Graph.Versions.GetOutputVersion(provider.Node.Id);

    [Fact]
    public void EqualReCollect_KeepsVersion_AndDoesNotRerunDownstream()
    {
        using var context = new WordsContext();
        var joined = new Joined(context.Words.Collect());
        context.WordsSource.Push("a");
        context.WordsSource.Push("b");
        Assert.Equal("a,b", joined.Text.Value);
        var version = Version(context, joined.Collected);

        // The source changes twice and ends up with the same items: Collect runs again (its input
        // changed) and produces a new, element-wise equal array.
        context.WordsSource.Push("c");
        context.WordsSource.Remove("c");

        Assert.Equal("a,b", joined.Text.Value);
        Assert.Equal(1, joined.Runs);
        Assert.Equal(version, Version(context, joined.Collected));
        Assert.Equal(["a", "b"], joined.Collected.Value);
    }

    [Fact]
    public void ChangedItem_RerunsDownstream()
    {
        using var context = new WordsContext();
        var joined = new Joined(context.Words.Collect());
        context.WordsSource.Push("a");
        context.WordsSource.Push("b");
        Assert.Equal("a,b", joined.Text.Value);
        var version = Version(context, joined.Collected);

        context.WordsSource.Replace("b", "x");

        Assert.Equal("a,x", joined.Text.Value);
        Assert.Equal(2, joined.Runs);
        Assert.NotEqual(version, Version(context, joined.Collected));
    }

    [Fact]
    public void OrderChange_RerunsDownstream()
    {
        using var context = new WordsContext();
        var joined = new Joined(context.Words.Collect());
        context.WordsSource.Push("a");
        context.WordsSource.Push("b");
        Assert.Equal("a,b", joined.Text.Value);
        var version = Version(context, joined.Collected);

        // Removing and pushing an item again appends it: same items, different order
        context.WordsSource.Remove("a");
        context.WordsSource.Push("a");

        Assert.Equal("b,a", joined.Text.Value);
        Assert.Equal(2, joined.Runs);
        Assert.NotEqual(version, Version(context, joined.Collected));
    }

    [Fact]
    public void AddedItem_RerunsDownstream()
    {
        using var context = new WordsContext();
        var joined = new Joined(context.Words.Collect());
        context.WordsSource.Push("a");
        Assert.Equal("a", joined.Text.Value);

        context.WordsSource.Push("b");

        Assert.Equal("a,b", joined.Text.Value);
        Assert.Equal(2, joined.Runs);
    }

    [Fact]
    public void CollectWithComparer_ItemsEqualByComparer_DoNotRerunDownstream()
    {
        using var context = new WordsContext();
        var joined = new Joined(context.Words.Collect(new IgnoreCase()));
        context.WordsSource.Push("a");
        context.WordsSource.Push("b");
        Assert.Equal("a,b", joined.Text.Value);

        context.WordsSource.Replace("a", "A"); // equal under the comparer
        Assert.Equal("a,b", joined.Text.Value);
        Assert.Equal(1, joined.Runs);

        context.WordsSource.Replace("b", "c"); // a real change
        Assert.Equal("A,c", joined.Text.Value);
        Assert.Equal(2, joined.Runs);
    }

    [Fact]
    public void CollectWithoutComparer_ItemsEqualOnlyByAnotherComparer_RerunDownstream()
    {
        using var context = new WordsContext();
        var joined = new Joined(context.Words.Collect());
        context.WordsSource.Push("a");
        Assert.Equal("a", joined.Text.Value);

        context.WordsSource.Replace("a", "A");

        Assert.Equal("A", joined.Text.Value);
        Assert.Equal(2, joined.Runs);
    }

    [Fact]
    public void Collect_IdenticalCalls_ReuseTheirNode_AndComparerCallsAreSeparate()
    {
        using var context = new WordsContext();
        var comparer = new IgnoreCase();

        Assert.Same(context.Words.Collect().Node, context.Words.Collect().Node);
        Assert.Same(context.Words.Collect(comparer).Node, context.Words.Collect(comparer).Node);
        Assert.NotSame(context.Words.Collect().Node, context.Words.Collect(comparer).Node);
        Assert.NotSame(context.Words.Collect(comparer).Node, context.Words.Collect(new IgnoreCase()).Node);
    }

    [Fact]
    public void Collect_NullComparer_Throws()
    {
        using var context = new WordsContext();

        Assert.Throws<ArgumentNullException>(() => context.Words.Collect((IEqualityComparer<string>)null!));
    }

    [Fact]
    public void EqualReCollect_BehindStageThatComparesByReference_DoesNotRerunDownstream()
    {
        using var context = new WordsContext();
        // The upstream stage counts every new string instance as a change, so Collect runs again
        // whenever a word is replaced, even if the lowered words are equal.
        var lowered = context.Words.Select(w => w.ToLowerInvariant(), ByReference);
        var joined = new Joined(lowered.Collect());
        context.WordsSource.Push("a");
        Assert.Equal("a", joined.Text.Value);

        context.WordsSource.Replace("a", "A");

        Assert.Equal("a", joined.Text.Value);
        Assert.Equal(1, joined.Runs);
    }

    [Fact]
    public void OutputHandlerOnCollect_IsNotCalledForEqualReCollect()
    {
        using var context = new WordsContext();
        var calls = new List<string>();
        var lowered = context.Words.Select(w => w.ToLowerInvariant(), ByReference);
        context.RegisterOutput(lowered.Collect(), (all, _) => calls.Add(string.Join(",", all)));

        context.WordsSource.Push("a");
        context.WordsSource.Replace("a", "A"); // collects an equal ["a"]
        context.WordsSource.Push("b");

        Assert.Equal(["a", "a,b"], calls);
    }

    [Fact]
    public void OutputHandlerOnCollectWithComparer_IsNotCalledForItemsEqualByComparer()
    {
        using var context = new WordsContext();
        var calls = new List<string>();
        context.RegisterOutput(context.Words.Collect(new IgnoreCase()), (all, _) => calls.Add(string.Join(",", all)));

        context.WordsSource.Push("a");
        context.WordsSource.Replace("a", "A");
        context.WordsSource.Replace("A", "b");

        Assert.Equal(["a", "b"], calls);
    }
}
