using Guacamole;
using Guacamole.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Tests verifying that cache invalidation uses equality comparison to prevent
/// unnecessary recomputation (Roslyn-style incremental computation).
/// </summary>
public partial class EqualityBasedCachingTests
{
    [ComputingContext]
    private partial class EqualityTestContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<string> Name { get; }

        private int _computeCount = 0;

        public int ComputeCount => _computeCount;

        public EqualityTestContext(System.IServiceProvider services) : base()
        {
            Initialize();
        }

        partial void InitializeComputationNodes();

        // Override to track computations
        public void IncrementComputeCount() => _computeCount++;
    }

    [ComputingContext]
    private partial class EqualityTestCollectionContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Items { get; }

        public EqualityTestCollectionContext(System.IServiceProvider services) : base()
        {
            Initialize();
        }

        partial void InitializeComputationNodes();
    }

    private static System.IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void SaveChanges_WhenValueUnchanged_DoesNotInvalidateCache()
    {
        // Arrange
        var services = CreateServices();
        var context = new EqualityTestContext(services);
        int handlerCallCount = 0;

        context.SetName("Alice");
        context.SaveChanges();

        // Register handler to track recomputation
        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            handlerCallCount++;
        });

        // First call after registration establishes baseline
        context.SaveChanges();  // Called without mutation - handler called once to establish baseline
        Assert.Equal(1, handlerCallCount);

        // Act - Set to same value
        context.SetName("Alice");
        context.SaveChanges();

        // Assert - Handler should not be called again because value didn't change
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public void SaveChanges_WhenValueChanged_InvalidatesCacheAndTriggersRecomputation()
    {
        // Arrange
        var services = CreateServices();
        var context = new EqualityTestContext(services);
        int handlerCallCount = 0;
        string? lastValue = null;

        context.SetName("Alice");
        context.SaveChanges();

        // Register handler to track recomputation
        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            handlerCallCount++;
            lastValue = value;
        });

        // Act - Set to different value
        context.SetName("Bob");
        context.SaveChanges();

        // Assert - Handler should be called once with new value
        Assert.Equal(1, handlerCallCount);
        Assert.Equal("Bob", lastValue);
    }

    [Fact]
    public void SaveChanges_WithMultipleIdenticalSets_OnlyFirstChangeInvalidatesCache()
    {
        // Arrange
        var services = CreateServices();
        var context = new EqualityTestContext(services);
        int handlerCallCount = 0;

        context.SetName("Alice");
        context.SaveChanges();

        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            handlerCallCount++;
        });

        // First call establishes baseline
        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);

        // Act - Multiple sets to same value
        context.SetName("Alice");
        context.SaveChanges();

        context.SetName("Alice");
        context.SaveChanges();

        context.SetName("Alice");
        context.SaveChanges();

        // Assert - Handler only called once (on baseline), not called again because value never changed
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public void SaveChanges_CollectionWithNoActualChanges_DoesNotInvalidateCache()
    {
        // Arrange
        var services = CreateServices();
        var context = new EqualityTestCollectionContext(services);
        int handlerCallCount = 0;

        context.AddItems("Alice");
        context.SaveChanges();

        context.RegisterOutput(context.Items, (values, ctx) =>
        {
            handlerCallCount++;
        });

        // First call establishes baseline
        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);

        // Act - Try to remove item that doesn't exist
        context.RemoveItems("Bob");  // Bob was never added
        context.SaveChanges();

        // Assert - Handler should not be called again because collection didn't change
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public void SaveChanges_CollectionWithActualChanges_InvalidatesCacheAndTriggersRecomputation()
    {
        // Arrange
        var services = CreateServices();
        var context = new EqualityTestCollectionContext(services);
        int handlerCallCount = 0;

        context.AddItems("Alice");
        context.SaveChanges();

        context.RegisterOutput(context.Items, (values, ctx) =>
        {
            handlerCallCount++;
        });

        // Act - Add a new item
        context.AddItems("Bob");
        context.SaveChanges();

        // Assert - Handler should be called once
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public void SaveChanges_CollectionRemoveExistingItem_InvalidatesCache()
    {
        // Arrange
        var services = CreateServices();
        var context = new EqualityTestCollectionContext(services);
        int handlerCallCount = 0;

        context.AddItems("Alice");
        context.AddItems("Bob");
        context.SaveChanges();

        context.RegisterOutput(context.Items, (values, ctx) =>
        {
            handlerCallCount++;
        });

        // Act - Remove existing item
        context.RemoveItems("Alice");
        context.SaveChanges();

        // Assert - Handler should be called once
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public void SaveChanges_CollectionReplaceWithSameValue_StillInvalidatesCache()
    {
        // Arrange - For Replace, even same value causes invalidation
        // because Replace is structural change (remove + add)
        var services = CreateServices();
        var context = new EqualityTestCollectionContext(services);
        int handlerCallCount = 0;

        context.AddItems("Alice");
        context.SaveChanges();

        context.RegisterOutput(context.Items, (values, ctx) =>
        {
            handlerCallCount++;
        });

        // Act - Replace Alice with Alice
        context.ReplaceItems("Alice", "Alice");
        context.SaveChanges();

        // Assert - Handler IS called because Replace is structural change
        Assert.Equal(1, handlerCallCount);
    }
}
