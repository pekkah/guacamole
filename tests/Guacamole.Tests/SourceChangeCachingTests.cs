using System.Linq;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Regression tests: derived values must stay cached while their sources are unchanged,
/// and recompute exactly once after each source change.
/// </summary>
public partial class SourceChangeCachingTests
{
    [ComputingContext]
    private partial class CachingValueContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
    }

    [ComputingContext]
    private partial class CachingValuesContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    [Fact]
    public void Select_OnValueSource_ComputesOncePerSourceChange()
    {
        using var context = new CachingValueContext();
        var computeCount = 0;
        var doubled = context.Number.Select(x => { computeCount++; return x * 2; });

        context.NumberSource.Set(1);
        Assert.Equal(2, doubled.Value);
        Assert.Equal(2, doubled.Value);
        Assert.Equal(1, computeCount);

        context.NumberSource.Set(2);
        Assert.Equal(4, doubled.Value);
        Assert.Equal(4, doubled.Value);
        Assert.Equal(2, computeCount);

        context.NumberSource.Set(3);
        Assert.Equal(6, doubled.Value);
        Assert.Equal(6, doubled.Value);
        Assert.Equal(3, computeCount);
    }

    [Fact]
    public void ChainedSelect_OnValueSource_ComputesEachStageOncePerSourceChange()
    {
        using var context = new CachingValueContext();
        var firstCount = 0;
        var secondCount = 0;
        var pipeline = context.Number
            .Select(x => { firstCount++; return x + 1; })
            .Select(x => { secondCount++; return x * 10; });

        context.NumberSource.Set(1);
        Assert.Equal(20, pipeline.Value);
        Assert.Equal(20, pipeline.Value);
        Assert.Equal((1, 1), (firstCount, secondCount));

        context.NumberSource.Set(2);
        Assert.Equal(30, pipeline.Value);
        Assert.Equal(30, pipeline.Value);
        Assert.Equal((2, 2), (firstCount, secondCount));
    }

    [Fact]
    public void Select_OnCollectionSource_ComputesOncePerSourceChange()
    {
        using var context = new CachingValuesContext();
        var computeCount = 0;
        var doubled = context.Items.Select(x => { computeCount++; return x * 2; });

        context.ItemsSource.Push(1);
        Assert.Equal(new[] { 2 }, doubled.Values.ToArray());
        Assert.Equal(new[] { 2 }, doubled.Values.ToArray());
        Assert.Equal(1, computeCount);

        context.ItemsSource.Push(2);
        Assert.Equal(new[] { 2, 4 }, doubled.Values.ToArray());
        Assert.Equal(new[] { 2, 4 }, doubled.Values.ToArray());
        Assert.Equal(3, computeCount); // two elements recomputed once
    }

    [Fact]
    public void RegisterOutput_ThenGetValue_DoesNotRecomputeAgain()
    {
        using var context = new CachingValueContext();
        var computeCount = 0;
        var doubled = context.Number.Select(x => { computeCount++; return x * 2; });
        var outputs = new List<int>();
        context.RegisterOutput(doubled, (value, _) => outputs.Add(value));

        context.NumberSource.Set(1);
        Assert.Equal(2, doubled.Value);
        context.NumberSource.Set(2);
        Assert.Equal(4, doubled.Value);

        Assert.Equal(new[] { 2, 4 }, outputs);
        Assert.Equal(2, computeCount);
    }

    [Fact]
    public void Select_WhenOtherProviderReadsSourceDirectly_StaysCached()
    {
        using var context = new CachingValueContext();
        var computeCount = 0;
        var doubled = context.Number.Select(x => { computeCount++; return x * 2; });

        context.NumberSource.Set(1);
        Assert.Equal(2, doubled.Value);
        Assert.Equal(1, context.Number.Value); // reading the source must not invalidate dependents
        Assert.Equal(2, doubled.Value);
        Assert.Equal(1, computeCount);

        context.NumberSource.Set(5);
        Assert.Equal(5, context.Number.Value);
        Assert.Equal(10, doubled.Value);
        Assert.Equal(10, doubled.Value);
        Assert.Equal(2, computeCount);
    }

    [Fact]
    public void Select_WhenSourceChangesDuringCompute_RecomputesOnNextRead()
    {
        using var context = new CachingValueContext();
        var computeCount = 0;
        var doubled = context.Number.Select(x =>
        {
            computeCount++;
            if (computeCount == 1)
            {
                // Simulate a concurrent writer changing the source mid-computation
                context.NumberSource.Set(2);
            }
            return x * 2;
        });

        context.NumberSource.Set(1);

        // The first computation read 1; the change to 2 must not be recorded as seen
        Assert.Equal(2, doubled.Value);
        Assert.Equal(4, doubled.Value);
        Assert.Equal(4, doubled.Value);
        Assert.Equal(2, computeCount);
    }
}
