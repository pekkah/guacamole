using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// A context's value cache capacity is configured by overriding
/// <see cref="ComputingContextBase.CacheCapacity"/>.
/// </summary>
public partial class CacheCapacityTests
{
    [ComputingContext]
    private partial class DefaultCapacityContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
    }

    [ComputingContext]
    private partial class SmallCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 2;

        public partial IncrementalValueProvider<int> Number { get; }
    }

    [ComputingContext]
    private partial class InvalidCapacityContext : ComputingContextBase
    {
        protected override int CacheCapacity => 0;
    }

    [Fact]
    public void Context_ByDefault_UsesDefaultCacheCapacity()
    {
        using var context = new DefaultCapacityContext();

        Assert.Equal(ComputingContextBase.DefaultCacheCapacity, context.Graph.ValueCache.Capacity);
    }

    [Fact]
    public void Context_WithOverriddenCapacity_UsesItForItsGraph()
    {
        using var context = new SmallCacheContext();

        Assert.Equal(2, context.Graph.ValueCache.Capacity);
    }

    [Fact]
    public void Context_WithOverriddenCapacity_EvictsBeyondCapacityAndStillComputesCorrectly()
    {
        using var context = new SmallCacheContext();
        context.NumberSource.Set(3);

        var providers = Enumerable.Range(1, 5)
            .Select(i => context.Number.Select(x => x * i))
            .ToArray();

        var values = providers.Select(p => p.Value).ToArray();

        Assert.Equal(new[] { 3, 6, 9, 12, 15 }, values);
        Assert.True(context.Graph.ValueCache.Count <= 2);
        Assert.Equal(new[] { 3, 6, 9, 12, 15 }, providers.Select(p => p.Value).ToArray());
    }

    [Fact]
    public void Context_WithNonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvalidCapacityContext());
    }
}
