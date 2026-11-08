using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Custom comparers passed to operators must decide whether a recomputed result counts as a change:
/// when the comparer says the new result equals the previous one, downstream stages stay cached and
/// output handlers don't fire.
/// </summary>
/// <remarks>
/// The equality cut-off happens at every stage, whether or not the stage is read itself (through
/// <c>Value</c> / <c>GetValue</c> / <c>GetValues</c> or an output registration), so the tests only
/// read the downstream stage.
/// </remarks>
public partial class CustomComparerBehaviorTests
{
    private sealed class IgnoreCase : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(x, y);
        public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
    }

    [ComputingContext]
    private partial class TextContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<string> Text { get; }
    }

    [ComputingContext]
    private partial class WordsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Words { get; }
        public partial IncrementalValueProvider<string> Sentence { get; }
    }

    [Fact]
    public void ValueSelect_WithComparer_EqualResult_DoesNotRecomputeDownstream()
    {
        using var context = new TextContext();
        var downstreamRuns = 0;
        var same = context.Text.Select(s => s, new IgnoreCase());
        var length = same.Select(s => { downstreamRuns++; return s.Length; });

        context.TextSource.Set("hello");
        Assert.Equal(5, length.Value);
        Assert.Equal(1, downstreamRuns);

        context.TextSource.Set("HELLO"); // equal under the comparer
        Assert.Equal(5, length.Value);
        Assert.Equal(1, downstreamRuns);

        context.TextSource.Set("world"); // a real change
        Assert.Equal(5, length.Value);
        Assert.Equal(2, downstreamRuns);
    }

    [Fact]
    public void ValueSelect_WithComparer_EqualResult_DoesNotFireOutputHandler()
    {
        using var context = new TextContext();
        var calls = new List<string>();
        var same = context.Text.Select(s => s, new IgnoreCase());
        context.RegisterOutput(same, (value, _) => calls.Add(value));

        context.TextSource.Set("hello");
        context.TextSource.Set("HELLO");
        context.TextSource.Set("world");

        Assert.Equal(["hello", "world"], calls);
    }

    [Fact]
    public void ValuesSelect_WithComparer_EqualResults_DoNotFireOutputHandler()
    {
        using var context = new WordsContext();
        var calls = 0;
        var trimmed = context.Words.Select(w => w.Trim(), new IgnoreCase());
        context.RegisterOutput(trimmed, (_, _) => calls++);

        context.WordsSource.Push("alpha");
        context.WordsSource.Replace("alpha", "ALPHA"); // equal under the comparer
        context.WordsSource.Push("beta");

        Assert.Equal(2, calls);
    }

    [Fact]
    public void ValueWhere_WithComparer_EqualResult_DoesNotRecomputeDownstream()
    {
        using var context = new TextContext();
        var downstreamRuns = 0;
        var nonEmpty = context.Text.Where(s => s.Length > 0, new IgnoreCase());
        var length = nonEmpty.Select(s => { downstreamRuns++; return s.Length; });

        context.TextSource.Set("hello");
        Assert.Equal(5, length.Value);
        context.TextSource.Set("HELLO");
        Assert.Equal(5, length.Value);

        Assert.Equal(1, downstreamRuns);
    }

    [Fact]
    public void ValuesSelect_WithComparer_EqualResults_DoNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var downstreamRuns = 0;
        var lowered = context.Words.Select(w => w.Trim(), new IgnoreCase());
        var count = lowered.Collect().Select(all => { downstreamRuns++; return all.Length; });

        context.WordsSource.Push("alpha");
        Assert.Equal(1, count.Value);
        Assert.Equal(1, downstreamRuns);

        context.WordsSource.Replace("alpha", "ALPHA"); // equal under the comparer
        Assert.Equal(1, count.Value);
        Assert.Equal(1, downstreamRuns);

        context.WordsSource.Push("beta");
        Assert.Equal(2, count.Value);
        Assert.Equal(2, downstreamRuns);
    }

    [Fact]
    public void ValuesWhere_WithComparer_EqualResults_DoNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var downstreamRuns = 0;
        var kept = context.Words.Where(w => w.Length > 0, new IgnoreCase());
        var count = kept.Collect().Select(all => { downstreamRuns++; return all.Length; });

        context.WordsSource.Push("alpha");
        Assert.Equal(1, count.Value);
        context.WordsSource.Replace("alpha", "ALPHA");
        Assert.Equal(1, count.Value);

        Assert.Equal(1, downstreamRuns);
    }

    [Fact]
    public void ValuesSelectMany_WithComparer_EqualResults_DoNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var downstreamRuns = 0;
        var parts = context.Words.SelectMany(w => w.Split('-'), new IgnoreCase());
        var count = parts.Collect().Select(all => { downstreamRuns++; return all.Length; });

        context.WordsSource.Push("a-b");
        Assert.Equal(2, count.Value);
        context.WordsSource.Replace("a-b", "A-B");
        Assert.Equal(2, count.Value);

        Assert.Equal(1, downstreamRuns);
    }

    [Fact]
    public void ValueSelectMany_WithComparer_EqualResults_DoNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var downstreamRuns = 0;
        var words = context.Sentence.SelectMany(s => s.Split(' '), new IgnoreCase());
        var count = words.Collect().Select(all => { downstreamRuns++; return all.Length; });

        context.SentenceSource.Set("hello world");
        Assert.Equal(2, count.Value);
        context.SentenceSource.Set("HELLO WORLD");
        Assert.Equal(2, count.Value);

        Assert.Equal(1, downstreamRuns);
    }
}
