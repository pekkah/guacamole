using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Tests that force garbage collections run without other tests in parallel, so the collections
/// neither slow down timing-sensitive tests nor depend on what other tests keep alive.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GarbageCollectionTestCollection
{
    public const string Name = "GarbageCollection";
}

/// <summary>
/// Issue #41: <see cref="ComputingContextBase.TrimUnusedNodes"/> keeps the node registry, cached
/// values and version records bounded when pipelines are built dynamically, without ever affecting
/// a pipeline that is still referenced.
/// </summary>
[Collection(GarbageCollectionTestCollection.Name)]
public partial class TrimUnusedNodesTests
{
    [ComputingContext]
    private partial class NumberContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    private static int Triple(int x) => x * 3;

    private static void CollectGarbage()
    {
        for (var i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>Trims, collects what the trim released, and trims again.</summary>
    private static void TrimFully(ComputingContextBase context)
    {
        context.TrimUnusedNodes();
        CollectGarbage();
        context.TrimUnusedNodes();
    }

    private static (int RuntimeNodes, int Versions, int CachedValues, int ReusableNodes) Sizes(ComputingContextBase context) => (
        context.Graph.GetRuntimeNodeCount(),
        context.Graph.Versions.Count,
        context.Graph.ValueCache.Count,
        context.Graph.SharedState.ReusableNodeCount);

    /// <summary>
    /// Builds and reads a pipeline whose lambdas capture a local (a new closure, so a new node, per
    /// call) and drops it. Not inlined, so nothing it creates stays reachable from the caller's frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildAndDropPipeline(NumberContext context, int offset)
    {
        var shifted = context.Number.Select(x => x + offset);
        var described = shifted.Where(x => x != offset - 1).Select(x => $"{x}:{offset}");
        var items = context.Items.Select(x => x + offset).Collect().Select(all => all.Length);

        Assert.Equal($"{1 + offset}:{offset}", described.Value);
        Assert.Equal(2, items.Value);
    }

    [Fact]
    public void CapturingLambdaPipelines_BuiltRepeatedly_StayBoundedAfterTrimming()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(1);
        context.ItemsSource.Push(1);
        context.ItemsSource.Push(2);
        const int Pipelines = 200;

        BuildAndDropPipeline(context, 0);
        TrimFully(context);
        var baseline = Sizes(context);

        for (var i = 0; i < Pipelines; i++)
        {
            BuildAndDropPipeline(context, i);
        }

        // Each pipeline added 6 nodes, each with a cached value and a version record
        var grown = Sizes(context);
        Assert.True(grown.RuntimeNodes >= baseline.RuntimeNodes + 6 * Pipelines, $"runtime nodes: {grown}");
        Assert.True(grown.Versions >= baseline.Versions + 6 * Pipelines, $"versions: {grown}");
        Assert.True(grown.ReusableNodes >= 6 * Pipelines, $"reusable nodes: {grown}");

        TrimFully(context);

        var trimmed = Sizes(context);
        Assert.Equal(baseline, trimmed);
        Assert.True(trimmed.RuntimeNodes <= 2, $"only the sources may remain registered: {trimmed}");
        Assert.True(trimmed.CachedValues <= 2, $"only the sources may remain cached: {trimmed}");
        Assert.Equal(0, trimmed.ReusableNodes);
    }

    [Fact]
    public void TrimUnusedNodes_ReturnsTheNumberOfRemovedNodes()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(1);
        context.ItemsSource.Push(1);
        context.ItemsSource.Push(2);
        TrimFully(context);

        BuildAndDropPipeline(context, 5);

        // The first trim only releases the context's own references: nothing was collected yet
        Assert.Equal(0, context.TrimUnusedNodes());
        CollectGarbage();
        Assert.Equal(6, context.TrimUnusedNodes());
        Assert.Equal(0, context.TrimUnusedNodes());
    }

    /// <summary>
    /// Builds a two-stage pipeline with capturing lambdas and returns only the downstream provider
    /// and the upstream node's ID.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IncrementalValueProvider<string> Downstream, string UpstreamId) BuildChain(NumberContext context, int offset, Action onRun)
    {
        var upstream = context.Number.Select(x => x + offset);
        var downstream = upstream.Select(x => { onRun(); return $"value {x}"; });
        return (downstream, upstream.Node.Id);
    }

    [Fact]
    public void LiveProvider_AndItsUpstreamNodes_SurviveTrimming()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(1);
        var runs = 0;
        var (kept, upstreamId) = BuildChain(context, 10, () => runs++);
        Assert.Equal("value 11", kept.Value);

        for (var i = 0; i < 20; i++)
        {
            BuildAndDropPipelineValueOnly(context, i);
        }

        TrimFully(context);
        TrimFully(context);

        // Neither the held node nor the upstream node it reads were removed, and the cache survived
        Assert.True(context.Graph.TryGetRuntimeNode(kept.Node.Id, out _));
        Assert.True(context.Graph.TryGetRuntimeNode(upstreamId, out _));
        Assert.Equal("value 11", kept.Value);
        Assert.Equal(1, runs);

        context.NumberSource.Set(2);
        Assert.Equal("value 12", kept.Value);
        Assert.Equal(2, runs);
        GC.KeepAlive(kept);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildAndDropPipelineValueOnly(NumberContext context, int offset)
    {
        Assert.Equal(1 + offset, context.Number.Select(x => x + offset).Value);
    }

    /// <summary>Registers an output on a pipeline built with capturing lambdas, keeping no provider.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterOutputOnDroppedPipeline(NumberContext context, int factor, List<int> received)
    {
        var scaled = context.Number.Select(x => x * factor).Where(x => x >= 0);
        context.RegisterOutput(scaled, (value, _) => received.Add(value));
    }

    [Fact]
    public void PipelineOfRegisteredOutput_SurvivesTrimming()
    {
        using var context = new NumberContext();
        var received = new List<int>();
        RegisterOutputOnDroppedPipeline(context, 7, received);

        context.NumberSource.Set(1);
        TrimFully(context);
        TrimFully(context);

        context.NumberSource.Set(2);
        context.NumberSource.Set(3);
        Assert.Equal([7, 14, 21], received);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadAndDropReusedPipeline(NumberContext context)
    {
        var tripled = context.Number.Select(Triple);
        Assert.Equal(3 * context.NumberSource.GetValue(), tripled.Value);
        return tripled.Node.Id;
    }

    [Fact]
    public void ReusedNode_StillReferenced_IsStillReusedAfterTrimming()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(2);
        var held = context.Number.Select(Triple);
        Assert.Equal(6, held.Value);

        TrimFully(context);

        var again = context.Number.Select(Triple);
        Assert.Same(held.Node, again.Node);
        Assert.Equal(6, again.Value);
    }

    [Fact]
    public void ReusedNode_RequestedAgainBeforeCollection_IsKeptAlive()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(2);
        var firstId = ReadAndDropReusedPipeline(context);

        // The trim releases the context's reference, but an identical call takes it again
        context.TrimUnusedNodes();
        var secondId = ReadAndDropReusedPipeline(context);
        CollectGarbage();
        context.TrimUnusedNodes();

        Assert.Equal(firstId, secondId);
        Assert.Equal(firstId, ReadAndDropReusedPipeline(context));
    }

    [Fact]
    public void ReusedNode_TrimmedAndCollected_IsRecreatedCleanly()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(2);
        var firstId = ReadAndDropReusedPipeline(context);

        TrimFully(context);
        Assert.False(context.Graph.TryGetRuntimeNode(firstId, out _));

        context.NumberSource.Set(4);
        var secondId = ReadAndDropReusedPipeline(context);
        Assert.NotEqual(firstId, secondId);

        // The recreated node is reused and cached like any other
        Assert.Equal(secondId, ReadAndDropReusedPipeline(context));
        context.NumberSource.Set(5);
        Assert.Equal(secondId, ReadAndDropReusedPipeline(context));
    }

    [Fact]
    public void TrimUnusedNodes_AfterDispose_Throws()
    {
        var context = new NumberContext();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.TrimUnusedNodes());
    }

    [Fact]
    public async Task TrimmingWhileOtherThreadsBuildAndRead_NeverAffectsResults()
    {
        using var context = new NumberContext();
        context.NumberSource.Set(1);
        context.ItemsSource.Push(1);
        context.ItemsSource.Push(2);
        var kept = context.Number.Select(x => x * 100);
        using var stop = new CancellationTokenSource();

        var workers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                BuildAndDropPipeline(context, worker * 100_000 + i++);
                Assert.Equal(100, kept.Value);
            }
        })).ToArray();

        for (var i = 0; i < 20; i++)
        {
            context.TrimUnusedNodes();
            CollectGarbage();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await stop.CancelAsync();
        await Task.WhenAll(workers);

        TrimFully(context);
        Assert.Equal(100, kept.Value);
        Assert.True(context.Graph.GetRuntimeNodeCount() <= 3, $"runtime nodes: {context.Graph.GetRuntimeNodeCount()}");
    }
}
