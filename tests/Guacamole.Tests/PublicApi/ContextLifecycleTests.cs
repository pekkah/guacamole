using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// <see cref="ComputingContextBase"/> lifecycle and configuration as documented in its XML docs,
/// README and the performance guide: <c>Dispose</c>, <c>IsDisposed</c>, <c>TrimUnusedNodes</c>,
/// <c>CacheCapacity</c> and <c>AutoTrimInterval</c>.
/// </summary>
public partial class ContextLifecycleTests
{
    [ComputingContext]
    private partial class SmallCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 2;

        public partial IncrementalValueProvider<int> Number { get; }
    }

    [ComputingContext]
    private partial class ZeroCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 0;
    }

    [ComputingContext]
    private partial class NegativeAutoTrimContext : ComputingContextBase
    {
        protected override int AutoTrimInterval => -1;
    }

    [ComputingContext]
    private partial class AutoTrimContext : ComputingContextBase
    {
        protected override int AutoTrimInterval => 1;

        public partial IncrementalValueProvider<int> Number { get; }
    }

    [Fact]
    public void DefaultCacheCapacity_IsTenThousand()
    {
        Assert.Equal(10_000, ComputingContextBase.DefaultCacheCapacity);
    }

    [Fact]
    public void Dispose_SetsIsDisposed_AndIsIdempotent()
    {
        var context = new ApiContext();
        Assert.False(context.IsDisposed);

        context.Dispose();
        context.Dispose();

        Assert.True(context.IsDisposed);
    }

    [Fact]
    public void AfterDispose_SourceChangesNoLongerRunOutputHandlers()
    {
        var context = new ApiContext();
        var calls = 0;
        context.RegisterOutput(context.Number, (_, _) => calls++);
        context.RegisterOutput(context.Items, (_, _) => calls++);

        context.Dispose();
        context.NumberSource.Set(1);
        context.ItemsSource.Push(new Item("a", 1));

        Assert.Equal(0, calls);
    }

    [Fact]
    public void AfterDispose_SourcesStillAcceptChanges()
    {
        // Documented (Dispose XML docs): sources keep working, but the disposed context no longer
        // tracks their changes, so its providers are not invalidated (they are not read here).
        var context = new ApiContext();
        context.Dispose();

        context.NumberSource.Set(2);
        context.ItemsSource.Push(new Item("a", 1));

        Assert.Equal(2, context.NumberSource.GetValue());
        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void AfterDispose_RegisterOutput_AndTrimUnusedNodes_ThrowObjectDisposedException()
    {
        var context = new ApiContext();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.RegisterOutput(context.Number, (_, _) => { }));
        Assert.Throws<ObjectDisposedException>(() => context.RegisterOutput(context.Items, (_, _) => { }));
        Assert.Throws<ObjectDisposedException>(() => context.TrimUnusedNodes());
    }

    [Fact]
    public void AfterDispose_ADisposedHandlerThatThrows_DoesNotSurface()
    {
        var context = new ApiContext();
        context.RegisterOutput(context.Number, (_, _) => throw new InvalidOperationException("must not run"));

        context.Dispose();

        context.NumberSource.Set(1); // does not throw: the registration was dropped
    }

    [Fact]
    public void CacheCapacity_BelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZeroCacheContext());
    }

    [Fact]
    public void AutoTrimInterval_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NegativeAutoTrimContext());
    }

    [Fact]
    public void SmallCache_EvictsValues_ButResultsStayCorrect()
    {
        using var context = new SmallCacheContext();
        var calls = new CallCounter();
        var stages = Enumerable.Range(1, 5)
            .Select(offset => context.Number.Select(x => calls.Pass(x + offset)))
            .ToArray();
        context.NumberSource.Set(10);

        for (var round = 0; round < 3; round++)
        {
            Assert.Equal([11, 12, 13, 14, 15], stages.Select(s => s.Value));
        }

        // More stages than cache entries: evicted values were recomputed
        Assert.True(calls.Count > stages.Length);
    }

    [Fact]
    public void TrimUnusedNodes_NeverChangesResults_OfLiveProviders()
    {
        using var context = new ApiContext();
        var doubled = context.Number.Select(static x => x * 2);
        var delivered = new List<int>();
        context.RegisterOutput(context.Number.Select(static x => x + 1), (v, _) => delivered.Add(v));
        context.NumberSource.Set(1);

        for (var i = 0; i < 3; i++)
        {
            var offset = i;
            _ = context.Number.Select(x => x + offset).Value; // throwaway capturing pipelines
            Assert.True(context.TrimUnusedNodes() >= 0);
        }

        Assert.Equal(2, doubled.Value);
        context.NumberSource.Set(2);
        Assert.Equal(4, doubled.Value);
        Assert.Equal([2, 3], delivered);
    }

    [Fact]
    public void AutoTrim_NeverChangesResults()
    {
        using var context = new AutoTrimContext();
        var doubled = context.Number.Select(static x => x * 2);
        context.NumberSource.Set(1);

        for (var i = 0; i < 50; i++)
        {
            var offset = i;
            Assert.Equal(1 + offset, context.Number.Select(x => x + offset).Value);
        }

        Assert.Equal(2, doubled.Value);
        context.NumberSource.Set(3);
        Assert.Equal(6, doubled.Value);
    }

    [Fact]
    public void ContextUsedInUsingBlock_IsDisposedAfterwards()
    {
        ApiContext context;
        using (context = new ApiContext())
        {
            context.NumberSource.Set(1);
        }

        Assert.True(context.IsDisposed);
    }
}
