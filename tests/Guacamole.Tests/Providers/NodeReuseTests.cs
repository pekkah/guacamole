using System.Collections.Concurrent;
using System.Linq;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Providers;

/// <summary>
/// Tests that repeated, identical operator calls (same operation, same input nodes,
/// equal delegates and equal extra arguments) reuse the same node. This makes
/// getter-style pipeline properties such as
/// <c>public IncrementalValueProvider&lt;int&gt; Doubled =&gt; Items.Select(x =&gt; x * 2);</c>
/// cache across accesses and keeps the graph's runtime node registry bounded.
/// </summary>
public partial class NodeReuseTests
{
    [ComputingContext]
    private partial class SourcesContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
        public partial IncrementalValueProvider<int> Other { get; }
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    /// <summary>
    /// Getter-style pipelines over a context (contexts themselves only allow partial
    /// source-backed provider properties, so the computed getters live here).
    /// </summary>
    private sealed class GetterContext : IDisposable
    {
        public SourcesContext Sources { get; } = new();
        public IIncrementalValueSource<int> NumberSource => Sources.NumberSource;
        public IIncrementalValueSource<int> OtherSource => Sources.OtherSource;
        public IIncrementalSource<int> ItemsSource => Sources.ItemsSource;
        public ComputationGraph Graph => Sources.Graph;
        public IncrementalValueProvider<int> Number => Sources.Number;
        public IncrementalValueProvider<int> Other => Sources.Other;
        public IncrementalValuesProvider<int> Items => Sources.Items;

        public int SelectorCalls;
        public int Factor = 3;

        // Non-capturing lambdas: the compiler caches the delegate instance.
        public IncrementalValueProvider<int> Doubled => Number.Select(static x => x * 2);
        public IncrementalValueProvider<int> Positive => Number.Where(static x => x > 0);
        public IncrementalValueProvider<(int, int)> Pair => Number.Combine(Other);
        public IncrementalValuesProvider<int> DoubledItems => Items.Select(static x => x * 2);
        public IncrementalValuesProvider<int> EvenItems => Items.Where(static x => x % 2 == 0);
        public IncrementalValueProvider<int> ItemCount => Items.Collect().Select(static a => a.Length);
        public IncrementalValuesProvider<int> Merged => Items.Merge(Items);
        public IncrementalValuesProvider<(int Left, int Right)> ItemsWithNumber => Items.Combine(Number);

        // Lambda capturing only 'this': a new delegate per access, but with the same
        // method and target, so Delegate.Equals considers them equal.
        public IncrementalValueProvider<int> Counted => Number.Select(x =>
        {
            Interlocked.Increment(ref SelectorCalls);
            return x * 2;
        });

        // Lambda capturing a local: a new closure object per access, so the
        // delegates are NOT equal and each access creates a distinct node.
        public IncrementalValueProvider<int> Captured
        {
            get
            {
                var factor = Factor;
                return Number.Select(x => x * factor);
            }
        }

        public void Dispose() => Sources.Dispose();
    }

    [Fact]
    public void GetterWithNonCapturingLambda_AccessedManyTimes_ReturnsSameNode()
    {
        using var context = new GetterContext();
        context.NumberSource.Set(5);

        var first = context.Doubled;
        for (var i = 0; i < 100; i++)
        {
            Assert.Same(first.Node, context.Doubled.Node);
            Assert.Equal(10, context.Doubled.Value);
        }
    }

    [Fact]
    public void GetterAccess_DoesNotGrowRuntimeNodeCount()
    {
        using var context = new GetterContext();
        context.NumberSource.Set(5);
        context.OtherSource.Set(7);
        context.ItemsSource.Push(1);
        context.ItemsSource.Push(2);

        void TouchAll()
        {
            _ = context.Doubled.Value;
            _ = context.Positive.GetValue();
            _ = context.Pair.Value;
            _ = context.DoubledItems.Values.ToArray();
            _ = context.EvenItems.Values.ToArray();
            _ = context.ItemCount.Value;
            _ = context.Merged.Values.ToArray();
            _ = context.ItemsWithNumber.Values.ToArray();
            _ = context.Counted.Value;
        }

        TouchAll();
        var countAfterFirstAccess = context.Graph.GetRuntimeNodeCount();

        for (var i = 0; i < 50; i++)
        {
            TouchAll();
        }

        Assert.Equal(countAfterFirstAccess, context.Graph.GetRuntimeNodeCount());
    }

    [Fact]
    public void GetterWithThisCapturingLambda_RunsSelectorOncePerSourceChange()
    {
        using var context = new GetterContext();
        context.NumberSource.Set(5);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(10, context.Counted.Value);
        }

        Assert.Equal(1, context.SelectorCalls);

        context.NumberSource.Set(6);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(12, context.Counted.Value);
        }

        Assert.Equal(2, context.SelectorCalls);
        Assert.Same(context.Counted.Node, context.Counted.Node);
    }

    [Fact]
    public void AllOperators_GetterAccess_ReturnSameNode()
    {
        using var context = new GetterContext();

        Assert.Same(context.Doubled.Node, context.Doubled.Node);
        Assert.Same(context.Positive.Node, context.Positive.Node);
        Assert.Same(context.Pair.Node, context.Pair.Node);
        Assert.Same(context.DoubledItems.Node, context.DoubledItems.Node);
        Assert.Same(context.EvenItems.Node, context.EvenItems.Node);
        Assert.Same(context.ItemCount.Node, context.ItemCount.Node);
        Assert.Same(context.Merged.Node, context.Merged.Node);
        Assert.Same(context.ItemsWithNumber.Node, context.ItemsWithNumber.Node);
    }

    [Fact]
    public void GetterWithLocalCapturingLambda_CreatesDistinctNodes_ByDesign()
    {
        // Expected limitation: a lambda that captures a local allocates a new closure
        // on every call, so its delegates are never equal and nodes cannot be reused.
        // Cache such pipelines in a field, or avoid capturing locals.
        using var context = new GetterContext();
        context.NumberSource.Set(2);

        var first = context.Captured;
        context.Factor = 10;
        var second = context.Captured;

        Assert.NotSame(first.Node, second.Node);
        Assert.NotEqual(first.Node.Id, second.Node.Id);
        Assert.Equal(6, first.Value);
        Assert.Equal(20, second.Value);
    }

    [Fact]
    public void SameSelectorWithDifferentComparer_CreatesDistinctNodes()
    {
        using var context = new GetterContext();
        Func<int, int> selector = static x => x;

        var a = context.Number.Select(selector);
        var b = context.Number.Select(selector, EqualityComparer<int>.Default);
        var c = context.Number.Select(selector, EqualityComparer<int>.Default);

        Assert.NotSame(a.Node, b.Node);
        Assert.Same(b.Node, c.Node);
    }

    [Fact]
    public void SameSelectorOnDifferentSources_CreatesDistinctNodes()
    {
        using var context = new GetterContext();
        context.NumberSource.Set(1);
        context.OtherSource.Set(2);
        Func<int, int> selector = static x => x * 10;

        var fromNumber = context.Number.Select(selector);
        var fromOther = context.Other.Select(selector);

        Assert.NotSame(fromNumber.Node, fromOther.Node);
        Assert.Equal(10, fromNumber.Value);
        Assert.Equal(20, fromOther.Value);
    }

    [Fact]
    public void Combine_WithSwappedArguments_ReusesPerOrderButNotAcrossOrders()
    {
        using var context = new GetterContext();

        var ab1 = context.Number.Combine(context.Other);
        var ab2 = context.Number.Combine(context.Other);
        var ba = context.Other.Combine(context.Number);

        Assert.Same(ab1.Node, ab2.Node);
        Assert.NotSame(ab1.Node, ba.Node);
    }

    [Fact]
    public void ReuseIsScopedPerContext()
    {
        using var context1 = new GetterContext();
        using var context2 = new GetterContext();
        context1.NumberSource.Set(1);
        context2.NumberSource.Set(2);

        Assert.NotSame(context1.Doubled.Node, context2.Doubled.Node);
        Assert.Equal(2, context1.Doubled.Value);
        Assert.Equal(4, context2.Doubled.Value);
    }

    [Fact]
    public async Task ConcurrentGetterAccess_ReturnsSameNode()
    {
        for (var round = 0; round < 20; round++)
        {
            using var context = new GetterContext();
            context.NumberSource.Set(3);
            var nodes = new ConcurrentBag<object>();
            using var start = new ManualResetEventSlim(false);

            var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (var i = 0; i < 20; i++)
                {
                    var provider = context.Counted;
                    nodes.Add(provider.Node);
                    Assert.Equal(6, provider.Value);
                }
            })).ToArray();

            start.Set();
            await Task.WhenAll(tasks);

            Assert.Single(nodes.Distinct());
            Assert.Equal(1, context.Graph.GetRuntimeNodeCount() - CountRootNodes(context));
        }
    }

    private static int CountRootNodes(GetterContext context)
        => context.Graph.GetRuntimeNodeIds().Count(id => !id.StartsWith("Select_", StringComparison.Ordinal));
}
