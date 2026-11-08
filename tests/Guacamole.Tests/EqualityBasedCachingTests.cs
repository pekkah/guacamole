using Guacamole;
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


        // Override to track computations
        public void IncrementComputeCount() => _computeCount++;
    }

    [ComputingContext]
    private partial class EqualityTestCollectionContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Items { get; }

    }

    [Fact]
    public void SaveChanges_WhenValueChanged_InvalidatesCacheAndTriggersRecomputation()
    {
        // Arrange
        var context = new EqualityTestContext();
        int handlerCallCount = 0;
        string? lastValue = null;

        context.NameSource.Set("Alice");

        // Register handler to track recomputation
        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            handlerCallCount++;
            lastValue = value;
        });

        // Act - Set to different value
        context.NameSource.Set("Bob");

        // Assert - Handler should be called once with new value
        Assert.Equal(1, handlerCallCount);
        Assert.Equal("Bob", lastValue);
    }
    [Fact]
    public void SaveChanges_CollectionRemoveExistingItem_InvalidatesCache()
    {
        // Arrange
        var context = new EqualityTestCollectionContext();
        int handlerCallCount = 0;

        context.ItemsSource.Push("Alice");
        context.RegisterOutput(context.Items, (values, ctx) =>
        {
            handlerCallCount++;
        });

        context.ItemsSource.Push("Bob");



        // Act - Remove existing item
        context.ItemsSource.Remove("Alice");

        // Assert - Handler called twice: once for Push("Bob"), once for Remove("Alice")
        Assert.Equal(2, handlerCallCount);
    }
}
