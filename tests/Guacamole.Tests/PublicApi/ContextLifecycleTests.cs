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
    public void AfterDispose_SourceChanges_ThrowObjectDisposedException_AndRunNoOutputHandlers()
    {
        // Documented (Dispose XML docs, README): a disposed context no longer tracks its sources, so
        // changing them throws rather than leave its providers stale
        var context = new ApiContext();
        var calls = 0;
        context.RegisterOutput(context.Number, (_, _) => calls++);
        context.RegisterOutput(context.Items, (_, _) => calls++);
        var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("b", 2));

        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.NumberSource.Set(1));
        Assert.Throws<ObjectDisposedException>(() => context.ItemsSource.Push(new Item("a", 1)));
        Assert.Throws<ObjectDisposedException>(() => context.ItemsSource.PushRange([new Item("a", 1)]));
        Assert.Throws<ObjectDisposedException>(() => context.ItemsSource.Remove(new Item("a", 1)));
        Assert.Throws<ObjectDisposedException>(() => context.ItemsSource.Replace(new Item("a", 1), new Item("c", 3)));
        Assert.Throws<ObjectDisposedException>(() => context.ItemsSource.Clear());
        Assert.Throws<ObjectDisposedException>(() => batch.Commit());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AfterDispose_SourcesKeepTheirValues_AndCanStillBeRead()
    {
        var context = new ApiContext();
        context.NumberSource.Set(2);
        context.ItemsSource.Push(new Item("a", 1));

        context.Dispose();

        Assert.Equal(2, context.NumberSource.GetValue());
        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void AfterDispose_ReadingProviders_ThrowsObjectDisposedException()
    {
        // Documented (Dispose XML docs, README): reads would otherwise return values cached before
        // the context stopped tracking its sources
        var context = new ApiContext();
        context.NumberSource.Set(2);
        context.ItemsSource.Push(new Item("a", 1));
        var doubled = context.Number.Select(static x => x * 2);
        var names = context.Items.Select(static i => i.Name);
        Assert.Equal(4, doubled.Value); // cached before Dispose
        Assert.Equal(["a"], names.Values);

        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.Number.GetValue());
        Assert.Throws<ObjectDisposedException>(() => doubled.GetValue());
        Assert.Throws<ObjectDisposedException>(() => doubled.Value);
        Assert.Throws<ObjectDisposedException>(() => doubled.TryGetValue(out _));
        Assert.Throws<ObjectDisposedException>(() => context.Items.GetValues());
        Assert.Throws<ObjectDisposedException>(() => names.GetValues());
        Assert.Throws<ObjectDisposedException>(() => names.Values);

        // Pipelines built after Dispose read the same graph
        Assert.Throws<ObjectDisposedException>(() => context.Number.Select(static x => x + 1).Value);
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

        // The change is refused (the registration was dropped too): the handler never runs
        Assert.Throws<ObjectDisposedException>(() => context.NumberSource.Set(1));
    }

    [Fact]
    public void DisposeFromAnOutputHandler_SkipsTheRemainingHandlers_WithoutThrowing()
    {
        var context = new ApiContext();
        var later = 0;
        context.RegisterOutput(context.Number, (_, ctx) => ctx.Dispose());
        context.RegisterOutput(context.Number.Select(static x => x + 1), (_, _) => later++);

        context.NumberSource.Set(1); // the second handler's provider would throw: skipped

        Assert.True(context.IsDisposed);
        Assert.Equal(0, later);
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
