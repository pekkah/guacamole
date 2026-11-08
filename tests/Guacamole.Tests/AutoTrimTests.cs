using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #52: with <see cref="ComputingContextBase.AutoTrimInterval"/> set, the context trims unused
/// nodes itself as nodes are added, so dynamically built pipelines stay bounded without explicit
/// <see cref="ComputingContextBase.TrimUnusedNodes"/> calls. Off by default.
/// </summary>
[Collection(GarbageCollectionTestCollection.Name)]
public partial class AutoTrimTests
{
    private const int Interval = 60;

    [ComputingContext]
    private partial class AutoTrimContext : ComputingContextBase
    {
        protected override int AutoTrimInterval => Interval;

        public partial IncrementalValueProvider<int> Number { get; }
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    [ComputingContext]
    private partial class EveryNodeContext : ComputingContextBase
    {
        protected override int AutoTrimInterval => 1;

        public partial IncrementalValueProvider<int> Number { get; }
    }

    [ComputingContext]
    private partial class DefaultContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    [ComputingContext]
    private partial class NegativeIntervalContext : ComputingContextBase
    {
        protected override int AutoTrimInterval => -1;

        public partial IncrementalValueProvider<int> Number { get; }
    }

    private static void CollectGarbage() => NodeReclamation.CollectGarbage();

    private static (int RuntimeNodes, int Versions, int CachedValues, int ReusableNodes) Sizes(ComputingContextBase context) => (
        context.Graph.GetRuntimeNodeCount(),
        context.Graph.Versions.Count,
        context.Graph.ValueCache.Count,
        context.Graph.SharedState.ReusableNodeCount);

    private static void Initialize(IIncrementalValueSource<int> number, IIncrementalSource<int> items)
    {
        number.Set(1);
        items.Push(1);
        items.Push(2);
    }

    /// <summary>
    /// Builds and reads a pipeline of 6 nodes whose lambdas capture a local (so new nodes per call)
    /// and drops it. Not inlined, so nothing it creates stays reachable from the caller's frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildAndDropPipeline(IncrementalValueProvider<int> number, IncrementalValuesProvider<int> items, int offset)
    {
        var described = number.Select(x => x + offset).Where(x => x != offset - 1).Select(x => $"{x}:{offset}");
        var count = items.Select(x => x + offset).Collect().Select(all => all.Length);

        Assert.Equal($"{1 + offset}:{offset}", described.Value);
        Assert.Equal(2, count.Value);
    }

    /// <summary>
    /// Builds <paramref name="pipelines"/> pipelines, collecting garbage every 20 (as the runtime
    /// would under allocation), without ever calling <see cref="ComputingContextBase.TrimUnusedNodes"/>.
    /// Returns the largest registry size seen right after a collection.
    /// </summary>
    private static int BuildMany(ComputingContextBase context, IncrementalValueProvider<int> number, IncrementalValuesProvider<int> items, int pipelines, int start = 0)
    {
        var largest = 0;
        for (var i = 0; i < pipelines; i++)
        {
            BuildAndDropPipeline(number, items, start + i);
            if (i % 20 == 19)
            {
                CollectGarbage();
                largest = Math.Max(largest, context.Graph.GetRuntimeNodeCount());
            }
        }

        return largest;
    }

    [Fact]
    public void AutoTrimOn_CapturingLambdaPipelines_StayBoundedWithoutExplicitTrims()
    {
        using var context = new AutoTrimContext();
        Initialize(context.NumberSource, context.ItemsSource);
        const int Pipelines = 2_000; // 12,000 nodes

        var largest = BuildMany(context, context.Number, context.Items, Pipelines);

        var sizes = Sizes(context);
        Assert.True(context.Graph.SharedState.AutoTrimCount >= 20, $"automatic trims: {context.Graph.SharedState.AutoTrimCount}");
        Assert.True(largest < 1_000, $"largest registry: {largest}");
        Assert.True(sizes.RuntimeNodes < 1_000, $"runtime nodes: {sizes}");
        Assert.True(sizes.Versions < 1_000, $"versions: {sizes}");
        Assert.True(sizes.CachedValues < 1_000, $"cached values: {sizes}");
        Assert.True(sizes.ReusableNodes < 1_000, $"reusable nodes: {sizes}");
    }

    [Fact]
    public void AutoTrimOn_LaterTrimsReclaimNodesReleasedByEarlierOnes()
    {
        using var context = new AutoTrimContext();
        Initialize(context.NumberSource, context.ItemsSource);
        BuildMany(context, context.Number, context.Items, 200);
        var trimsBefore = context.Graph.SharedState.AutoTrimCount;

        // Keep adding nodes, with collections in between: the dropped pipelines are released by one
        // automatic trim and removed by a later one, so the registry ends up near its live size
        BuildMany(context, context.Number, context.Items, 100, start: 1_000);

        Assert.True(context.Graph.SharedState.AutoTrimCount >= trimsBefore + 2);
        Assert.True(context.Graph.GetRuntimeNodeCount() <= 4 * Interval, $"runtime nodes: {Sizes(context)}");
    }

    [Fact]
    public void AutoTrimOff_ByDefault_NothingIsTrimmed()
    {
        using var context = new DefaultContext();
        Initialize(context.NumberSource, context.ItemsSource);
        const int Pipelines = 200;

        BuildMany(context, context.Number, context.Items, Pipelines);

        var sizes = Sizes(context);
        Assert.Equal(0, context.Graph.SharedState.AutoTrimCount);
        Assert.True(sizes.RuntimeNodes >= 6 * Pipelines, $"runtime nodes: {sizes}");
        Assert.True(sizes.ReusableNodes >= 6 * Pipelines, $"reusable nodes: {sizes}");

        // Explicit trimming still works as before
        context.TrimUnusedNodes();
        CollectGarbage();
        Assert.True(context.TrimUnusedNodes() >= 6 * Pipelines);
    }

    [Fact]
    public void NegativeAutoTrimInterval_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new NegativeIntervalContext());
        Assert.Equal("AutoTrimInterval", exception.ParamName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IncrementalValueProvider<string> Downstream, string UpstreamId) BuildChain(IncrementalValueProvider<int> number, int offset, Action onRun)
    {
        var upstream = number.Select(x => x + offset);
        var downstream = upstream.Select(x => { onRun(); return $"value {x}"; });
        return (downstream, upstream.Node.Id);
    }

    [Fact]
    public void AutoTrimOn_LiveProvider_AndItsUpstreamNodes_KeepTheirCache()
    {
        using var context = new AutoTrimContext();
        Initialize(context.NumberSource, context.ItemsSource);
        var runs = 0;
        var (kept, upstreamId) = BuildChain(context.Number, 10, () => runs++);
        Assert.Equal("value 11", kept.Value);

        BuildMany(context, context.Number, context.Items, 300);

        Assert.True(context.Graph.SharedState.AutoTrimCount > 0);
        Assert.True(context.Graph.TryGetRuntimeNode(kept.Node.Id, out _));
        Assert.True(context.Graph.TryGetRuntimeNode(upstreamId, out _));
        Assert.Equal("value 11", kept.Value);
        Assert.Equal(1, runs);

        context.NumberSource.Set(2);
        Assert.Equal("value 12", kept.Value);
        Assert.Equal(2, runs);
        GC.KeepAlive(kept);
    }

    [Fact]
    public void AutoTrim_IsNotStartedInsideANodeComputation()
    {
        using var context = new EveryNodeContext();
        context.NumberSource.Set(1);
        var trimsInside = -1;
        var outer = context.Number.Select(x =>
        {
            // Building (and reading) a pipeline inside a computation adds nodes, but must not trim
            var before = context.Graph.SharedState.AutoTrimCount;
            var inner = context.Number.Select(y => y + x).Select(y => y * 2);
            var value = inner.Value;
            trimsInside = context.Graph.SharedState.AutoTrimCount - before;
            return value;
        });

        var trimsBefore = context.Graph.SharedState.AutoTrimCount;
        Assert.Equal(4, outer.Value);
        Assert.Equal(0, trimsInside);

        // The next node added outside a computation trims
        var trimsAfterComputation = context.Graph.SharedState.AutoTrimCount;
        _ = context.Number.Select(y => y + trimsBefore);
        Assert.True(context.Graph.SharedState.AutoTrimCount > trimsAfterComputation);
        Assert.Equal(4, outer.Value);
    }

    /// <summary>
    /// Issue #79: the registry left by the workers is reclaimed deterministically (explicit trims and
    /// collections once they have been joined), not by counting on a fixed number of automatic trims.
    /// Automatic trims are amortized (the next one waits for half as many new nodes as remain
    /// registered) and only remove nodes released by an earlier trim and collected since, so shrinking
    /// a registry of N nodes takes about 2N new nodes; N depends on how many pipelines the workers
    /// built and on when the collections ran, so no fixed budget of new nodes is always enough.
    /// </summary>
    [Fact]
    public async Task AutoTrimOn_ConcurrentBuildAndRead_IsSafe()
    {
        using var context = new AutoTrimContext();
        Initialize(context.NumberSource, context.ItemsSource);
        var kept = context.Number.Select(x => x * 100);
        var keptCount = context.Items.Collect().Select(all => all.Length);
        Assert.Equal(100, kept.Value);
        Assert.Equal(2, keptCount.Value);
        NodeReclamation.TrimUntilStable(context);
        var baseline = Sizes(context);
        using var stop = new CancellationTokenSource();

        var workers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                BuildAndDropPipeline(context.Number, context.Items, worker * 100_000 + i++);
                Assert.Equal(100, kept.Value);
                Assert.Equal(2, keptCount.Value);
            }

            return i;
        })).ToArray();

        for (var i = 0; i < 20; i++)
        {
            CollectGarbage();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await stop.CancelAsync();
        var built = (await Task.WhenAll(workers)).Sum();

        Assert.True(context.Graph.SharedState.AutoTrimCount > 0 || built * 6 < Interval, $"built {built}, no automatic trim");
        Assert.Equal(100, kept.Value);
        Assert.Equal(2, keptCount.Value);

        // The workers have been joined and nothing references their pipelines: once reclaimed, exactly
        // the state registered before they started remains (nothing leaked into the registry, the
        // version records or the reuse cache; the value cache may have evicted a kept value under the
        // workers' load, as the kept providers are served through their revision stamps)
        var afterWorkers = context.Graph.GetRuntimeNodeCount();
        NodeReclamation.TrimUntilStable(context);
        var reclaimed = Sizes(context);
        var message = $"after reclaiming: {reclaimed}, before the workers: {baseline}, {afterWorkers} after the workers";
        Assert.True(reclaimed.RuntimeNodes == baseline.RuntimeNodes, message);
        Assert.True(reclaimed.Versions == baseline.Versions, message);
        Assert.True(reclaimed.ReusableNodes == baseline.ReusableNodes, message);
        Assert.True(reclaimed.CachedValues <= baseline.CachedValues, message);

        // Automatic trimming carries on as usual: from the reclaimed registry, single-threaded
        // building stays bounded without explicit trims (see AutoTrimOn_LaterTrimsReclaimNodesReleasedByEarlierOnes)
        var trimsBefore = context.Graph.SharedState.AutoTrimCount;
        BuildMany(context, context.Number, context.Items, 100, start: 1_000_000);
        Assert.True(context.Graph.SharedState.AutoTrimCount >= trimsBefore + 2);
        Assert.True(context.Graph.GetRuntimeNodeCount() <= 4 * Interval, $"runtime nodes: {Sizes(context)}");
        Assert.Equal(100, kept.Value);
        Assert.Equal(2, keptCount.Value);
        GC.KeepAlive(kept);
        GC.KeepAlive(keptCount);
    }
}
