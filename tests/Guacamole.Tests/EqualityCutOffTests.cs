using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #40: the equality cut-off ("an equal result keeps the stage's version, so downstream stays
/// cached") applies at every stage of a pipeline, including intermediate stages that are never read
/// through a provider themselves.
/// </summary>
public partial class EqualityCutOffTests
{
    [ComputingContext]
    private partial class TextContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<string> Text { get; }
        public partial IncrementalValueProvider<int> Number { get; }
    }

    [ComputingContext]
    private partial class WordsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Words { get; }
        public partial IncrementalValueProvider<string> Sentence { get; }
    }

    [ComputingContext]
    private partial class TinyCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 1;

        public partial IncrementalValueProvider<string> Text { get; }
    }

    [Fact]
    public void UnreadIntermediate_WithComparer_EqualResult_DoesNotRecomputeDownstream()
    {
        using var context = new TextContext();
        var runs = 0;
        var same = context.Text.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var length = same.Select(s => { runs++; return s.Length; });

        context.TextSource.Set("hello");
        Assert.Equal(5, length.Value);

        context.TextSource.Set("HELLO");
        Assert.Equal(5, length.Value);
        Assert.Equal(1, runs);

        context.TextSource.Set("world!");
        Assert.Equal(6, length.Value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void UnreadIntermediate_ReadLater_ReturnsItsCurrentValue()
    {
        using var context = new TextContext();
        var runs = 0;
        var same = context.Text.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var length = same.Select(s => { runs++; return s.Length; });

        context.TextSource.Set("hello");
        Assert.Equal(5, length.Value);
        context.TextSource.Set("HELLO");
        Assert.Equal(5, length.Value);

        // The comparer stage itself holds the latest value; only downstream kept its cache
        Assert.Equal("HELLO", same.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void LongChain_CutOffInTheMiddle_KeepsAllLaterStagesCached()
    {
        using var context = new TextContext();
        var trims = 0;
        var lengths = 0;
        var labels = 0;
        var trimmed = context.Text.Select(s => { trims++; return s.Trim(); });
        var folded = trimmed.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var length = folded.Select(s => { lengths++; return s.Length; });
        var label = length.Select(n => { labels++; return $"{n} chars"; });

        context.TextSource.Set(" hello ");
        Assert.Equal("5 chars", label.Value);

        context.TextSource.Set("HELLO ");
        Assert.Equal("5 chars", label.Value);
        Assert.Equal(2, trims);
        Assert.Equal(1, lengths);
        Assert.Equal(1, labels);
    }

    [Fact]
    public void UnreadIntermediate_EqualByDefaultEquality_DoesNotRecomputeDownstream()
    {
        using var context = new TextContext();
        var runs = 0;
        var length = context.Text.Select(s => s.Length);
        var parity = length.Select(n => { runs++; return n % 2 == 0 ? "even" : "odd"; });

        context.TextSource.Set("hello");
        Assert.Equal("odd", parity.Value);

        context.TextSource.Set("world"); // same length
        Assert.Equal("odd", parity.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Combine_UnreadInputWithEqualResult_DoesNotRecomputeDownstream()
    {
        using var context = new TextContext();
        var runs = 0;
        var folded = context.Text.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var combined = folded.Combine(context.Number)
            .Select(pair => { runs++; return $"{pair.Item1.Length}:{pair.Item2}"; });

        context.TextSource.Set("hello");
        context.NumberSource.Set(1);
        Assert.Equal("5:1", combined.Value);
        Assert.Equal(1, runs);

        context.TextSource.Set("HELLO");
        Assert.Equal("5:1", combined.Value);
        Assert.Equal(1, runs);

        context.NumberSource.Set(2);
        Assert.Equal("5:2", combined.Value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Diamond_UnreadSharedInputWithEqualResult_DoesNotRecomputeDownstream()
    {
        using var context = new TextContext();
        var runs = 0;
        var folded = context.Text.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var length = folded.Select(s => s.Length);
        var first = folded.Select(s => s.Length > 0 ? s.Substring(0, 1).ToLowerInvariant() : "");
        var summary = length.Combine(first).Select(pair => { runs++; return $"{pair.Item2}{pair.Item1}"; });

        context.TextSource.Set("hello");
        Assert.Equal("h5", summary.Value);

        context.TextSource.Set("HeLLo");
        Assert.Equal("h5", summary.Value);
        Assert.Equal(1, runs);

        context.TextSource.Set("help!!");
        Assert.Equal("h6", summary.Value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Where_UnreadWithComparer_EqualResult_DoesNotRecomputeDownstream()
    {
        using var context = new TextContext();
        var runs = 0;
        var nonEmpty = context.Text.Where(s => s.Length > 0, StringComparer.OrdinalIgnoreCase);
        var length = nonEmpty.Select(s => { runs++; return s.Length; });

        context.TextSource.Set("hello");
        Assert.Equal(5, length.Value);

        context.TextSource.Set("HELLO");
        Assert.Equal(5, length.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Where_UnreadValueToNoValueAndBack_DownstreamFollows()
    {
        using var context = new TextContext();
        var runs = 0;
        var longWords = context.Text.Where(s => s.Length > 3);
        var upper = longWords.Select(s => { runs++; return s.ToUpperInvariant(); });

        context.TextSource.Set("hello");
        Assert.Equal("HELLO", upper.Value);

        context.TextSource.Set("hi");
        Assert.False(upper.TryGetValue(out _));

        // Still no value: no change downstream
        context.TextSource.Set("ho");
        Assert.True(upper.GetValue().IsNone);

        // The same value as before "no value" must not be mistaken for the cached state
        context.TextSource.Set("hello");
        Assert.True(upper.TryGetValue(out var value));
        Assert.Equal("HELLO", value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Where_UnreadNoValue_ProviderReadsNoValueFromCache()
    {
        using var context = new TextContext();
        var predicateRuns = 0;
        var longWords = context.Text.Where(s => { predicateRuns++; return s.Length > 3; });

        context.TextSource.Set("hi");
        Assert.True(longWords.GetValue().IsNone);
        Assert.True(longWords.GetValue().IsNone);
        Assert.Equal(1, predicateRuns);
    }

    [Fact]
    public void ErrorInUnreadIntermediate_DownstreamRecoversWhenInputIsFixed()
    {
        using var context = new TextContext();
        var runs = 0;
        var parsed = context.Text.Select(int.Parse);
        var doubled = parsed.Select(n => { runs++; return n * 2; });

        context.TextSource.Set("21");
        Assert.Equal(42, doubled.Value);

        context.TextSource.Set("not a number");
        Assert.True(doubled.GetValue().IsError);
        Assert.True(doubled.GetValue().IsError);

        context.TextSource.Set("021"); // parses to the cached 21
        Assert.Equal(42, doubled.Value);

        context.TextSource.Set("22");
        Assert.Equal(44, doubled.Value);
        Assert.Equal(3, runs);
    }

    [Fact]
    public void ValuesSelect_UnreadWithComparer_EqualResults_DoNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var runs = 0;
        var trimmed = context.Words.Select(w => w.Trim(), StringComparer.OrdinalIgnoreCase);
        var nonEmpty = trimmed.Where(w => w.Length > 0);
        var count = nonEmpty.Collect().Select(all => { runs++; return all.Length; });

        context.WordsSource.Push("alpha");
        Assert.Equal(1, count.Value);

        context.WordsSource.Replace("alpha", " ALPHA ");
        Assert.Equal(1, count.Value);
        Assert.Equal(1, runs);

        context.WordsSource.Push("beta");
        Assert.Equal(2, count.Value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void ValuesWhere_UnreadEqualResults_DoNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var runs = 0;
        var longWords = context.Words.Where(w => w.Length > 3);
        var count = longWords.Collect().Select(all => { runs++; return all.Length; });

        context.WordsSource.Push("alpha");
        Assert.Equal(1, count.Value);

        context.WordsSource.Push("ab"); // filtered out: the Where result is unchanged
        Assert.Equal(1, count.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ValueSelectMany_UnreadWithComparer_EqualResults_DoNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var runs = 0;
        var words = context.Sentence.SelectMany(s => s.Split(' '), StringComparer.OrdinalIgnoreCase);
        var count = words.Collect().Select(all => { runs++; return all.Length; });

        context.SentenceSource.Set("hello world");
        Assert.Equal(2, count.Value);

        context.SentenceSource.Set("HELLO WORLD");
        Assert.Equal(2, count.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ValuesCombine_UnreadValueInputWithEqualResult_DoesNotRecomputeDownstream()
    {
        using var context = new WordsContext();
        var runs = 0;
        var folded = context.Sentence.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var paired = context.Words.Combine(folded);
        var count = paired.Collect().Select(all => { runs++; return all.Length; });

        context.WordsSource.Push("a");
        context.SentenceSource.Set("hello");
        Assert.Equal(1, count.Value);

        context.SentenceSource.Set("HELLO");
        Assert.Equal(1, count.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void OutputHandler_OnDownstreamOfUnreadIntermediate_DoesNotFireForEqualResult()
    {
        using var context = new TextContext();
        var runs = 0;
        var calls = new List<int>();
        var folded = context.Text.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var length = folded.Select(s => { runs++; return s.Length; });
        context.RegisterOutput(length, (value, _) => calls.Add(value));

        context.TextSource.Set("hello");
        context.TextSource.Set("HELLO");
        context.TextSource.Set("hi");

        Assert.Equal([5, 2], calls);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void EvictedIntermediate_IsRecomputedAndDownstreamStaysCorrect()
    {
        using var context = new TinyCacheContext();
        var folded = context.Text.Select(s => s, StringComparer.OrdinalIgnoreCase);
        var length = folded.Select(s => s.Length);
        var label = length.Select(n => $"{n} chars");

        foreach (var text in new[] { "hello", "HELLO", "hi", "world!", "WORLD!" })
        {
            context.TextSource.Set(text);
            Assert.Equal($"{text.Length} chars", label.Value);
            Assert.Equal(text.Length, length.Value);
        }
    }

    [Fact]
    public void RepeatedReads_WithoutSourceChange_DoNotRecompute()
    {
        using var context = new TextContext();
        var runs = 0;
        var length = context.Text.Select(s => s.Trim()).Select(s => { runs++; return s.Length; });

        context.TextSource.Set("hello");
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(5, length.Value);
        }

        Assert.Equal(1, runs);
    }
}

/// <summary>
/// Concurrent readers of a pipeline with an unread comparer stage while its source changes.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class EqualityCutOffConcurrencyTests
{
    [ComputingContext]
    private partial class NumberContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
    }

    [Fact]
    public async Task ConcurrentReadsDuringChanges_SettleOnTheLatestValue()
    {
        for (var round = 0; round < 10; round++)
        {
            using var context = new NumberContext();
            context.NumberSource.Set(0);
            var parity = context.Number.Select(n => n % 2);
            var tenfold = context.Number.Select(n => n * 10);
            var described = parity.Combine(tenfold).Select(pair => $"{pair.Item2}/{pair.Item1}");

            using var stop = new CancellationTokenSource();
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                // Reads racing with a change may combine inputs read before and after it; what
                // matters is that no stale value survives once the changes stop.
                while (!stop.IsCancellationRequested)
                {
                    Assert.Contains('/', described.Value);
                }
            })).ToArray();

            for (var i = 1; i <= 200; i++)
            {
                context.NumberSource.Set(i);
            }

            await stop.CancelAsync();
            await Task.WhenAll(readers);

            Assert.Equal("2000/0", described.Value);
        }
    }
}
