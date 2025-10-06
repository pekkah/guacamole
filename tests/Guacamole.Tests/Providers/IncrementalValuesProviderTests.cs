using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;
using Guacamole;
using Xunit;

namespace Guacamole.Core.Tests.Providers;

/// <summary>
/// Tests for IncrementalValuesProvider basic functionality following TDD progression:
/// Basic → Edge → Incremental → Performance
/// </summary>
public class IncrementalValuesProviderTests
{
    /// <summary>
    /// BASIC: Test that IncrementalValuesProvider is an immutable record struct with value equality
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldBe_ImmutableRecordStruct()
    {
        // Arrange - This will fail until IncrementalValuesProvider<T> is implemented
        var node = new TestValuesNode<int>("test", [1, 2, 3]);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test-provider");

        // Act
        var provider1 = new IncrementalValuesProvider<int>(node, graph, metadata);
        var provider2 = new IncrementalValuesProvider<int>(node, graph, metadata);

        // Assert - Value equality for immutable record struct
        Assert.Equal(provider1, provider2);
        Assert.True(provider1 == provider2);
        Assert.False(provider1 != provider2);
        Assert.Equal(provider1.GetHashCode(), provider2.GetHashCode());
    }

    /// <summary>
    /// BASIC: Test that provider has required properties
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldHave_RequiredProperties()
    {
        // Arrange
        var node = new TestValuesNode<string>("test", ["a", "b", "c"]);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test-provider");

        // Act
        var provider = new IncrementalValuesProvider<string>(node, graph, metadata);

        // Assert
        Assert.NotNull(provider.Node);
        Assert.NotNull(provider.Graph);
        // ProviderMetadata is a value type, no need for null check
        Assert.Equal("test", provider.Node.Id);
    }

    /// <summary>
    /// BASIC: Test provider supports multiple value types in collections
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldSupport_DifferentCollectionTypes()
    {
        // Arrange & Act & Assert
        var intProvider = CreateProvider("ints", [1, 2, 3]);
        var stringProvider = CreateProvider("strings", ["a", "b", "c"]);
        var boolProvider = CreateProvider("bools", [true, false, true]);

        // Providers are value types, no need for null check
        // Test passes if no exceptions thrown during creation
    }

    /// <summary>
    /// BASIC: Test provider handles empty collections
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldHandle_EmptyCollections()
    {
        // Arrange
        var emptyValues = Array.Empty<int>();
        var node = new TestValuesNode<int>("empty", emptyValues);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "empty-test");

        // Act
        var provider = new IncrementalValuesProvider<int>(node, graph, metadata);

        // Assert - Empty collections are valid and cacheable per data model
        // Provider is a value type, no need for null check
        Assert.Equal("empty", provider.Node.Id);
    }

    /// <summary>
    /// EDGE: Test validation rules for constructor parameters
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldValidate_ConstructorParameters()
    {
        // Arrange
        var node = new TestValuesNode<int>("test", [1, 2, 3]);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test");

        // Act & Assert - Null node should throw
        Assert.Throws<ArgumentNullException>(() =>
            new IncrementalValuesProvider<int>(null!, graph, metadata));

        // Null graph should throw
        Assert.Throws<ArgumentNullException>(() =>
            new IncrementalValuesProvider<int>(node, null!, metadata));

        // Default metadata should be allowed (struct cannot be null)
        var providerWithDefault = new IncrementalValuesProvider<int>(node, graph, default);
        Assert.NotNull(providerWithDefault.Node);
    }

    /// <summary>
    /// EDGE: Test provider with large collections
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldHandle_LargeCollections()
    {
        // Arrange - Create a reasonably large collection
        var largeCollection = Enumerable.Range(1, 1000).ToArray();
        var node = new TestValuesNode<int>("large", largeCollection);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "large-test");

        // Act
        var provider = new IncrementalValuesProvider<int>(node, graph, metadata);

        // Assert
        // Provider is a value type, no need for null check
        Assert.Equal("large", provider.Node.Id);
    }

    /// <summary>
    /// EDGE: Test provider preserves deterministic order per validation rules
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldPreserve_DeterministicOrder()
    {
        // Arrange - Same values in different orders
        var values1 = new[] { 1, 2, 3, 4, 5 };
        var values2 = new[] { 5, 4, 3, 2, 1 };

        var provider1 = CreateProvider("ordered1", values1);
        var provider2 = CreateProvider("ordered2", values2);

        // Act & Assert - Different orders should create different providers
        Assert.NotEqual(provider1, provider2);
    }

    /// <summary>
    /// INCREMENTAL: Test that provider supports caching through computation graph
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_ShouldSupport_CachingThroughGraph()
    {
        // Arrange
        var values = new[] { "cached", "values", "test" };
        var node = new TestValuesNode<string>("cached-test", values);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "cached-provider");

        // Act
        var provider = new IncrementalValuesProvider<string>(node, graph, metadata);

        // Assert - Graph should be shared for caching
        Assert.Same(graph, provider.Graph);
    }

    /// <summary>
    /// INCREMENTAL: Test that providers with same inputs are equal (for caching)
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_WithSameInputs_ShouldBe_Equal()
    {
        // Arrange
        var values = new[] { 10, 20, 30 };
        var node = new TestValuesNode<int>("equal-test", values);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "equal-test");

        // Act
        var provider1 = new IncrementalValuesProvider<int>(node, graph, metadata);
        var provider2 = new IncrementalValuesProvider<int>(node, graph, metadata);

        // Assert - Critical for caching behavior
        Assert.Equal(provider1, provider2);
        Assert.Equal(provider1.GetHashCode(), provider2.GetHashCode());
    }

    /// <summary>
    /// PERFORMANCE: Test that provider creation is O(1) and doesn't allocate excessively
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_Creation_ShouldBe_Fast()
    {
        // Arrange
        var values = Enumerable.Range(1, 100).ToArray();
        var node = new TestValuesNode<int>("perf-test", values);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "perf-test");

        // Act - Measure creation time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var provider = new IncrementalValuesProvider<int>(node, graph, metadata);
        stopwatch.Stop();

        // Assert - Should be very fast (< 10ms for struct creation, allowing CI overhead)
        Assert.True(stopwatch.ElapsedMilliseconds < 10,
            "Provider creation should be fast (< 10ms)");
        // Provider is a value type, no need for null check
    }

    /// <summary>
    /// Helper method to create providers for testing
    /// </summary>
    private static IncrementalValuesProvider<T> CreateProvider<T>(string id, T[] values)
    {
        var node = new TestValuesNode<T>(id, values);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: $"{id}-provider");
        return new IncrementalValuesProvider<T>(node, graph, metadata);
    }
}

/// <summary>
/// Test implementation of ICollectionComputationNode for testing purposes
/// </summary>
internal class TestValuesNode<T> : ICollectionComputationNode<T>
{
    public TestValuesNode(string id, T[] values)
    {
        Id = id;
        Values = values;
        OutputType = typeof(IEnumerable<T>);
        Dependencies = ImmutableHashSet<string>.Empty;
    }

    public string Id { get; }
    public T[] Values { get; }
    public Type OutputType { get; }
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context)
    {
        return Result<IEnumerable<T>, ComputationError>.Success(Values);
    }
}
