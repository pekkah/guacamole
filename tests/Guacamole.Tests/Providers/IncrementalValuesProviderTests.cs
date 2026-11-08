using System.Collections.Immutable;
using Guacamole.Pipeline;
using Guacamole.Tests.Common;
using Xunit;

namespace Guacamole.Tests.Providers;

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

        var largeNode = new TestValuesNode<int>("perf-test-large", Enumerable.Range(1, 100_000).ToArray());

        // Act - Measure what creating 1,000 providers allocates (deterministic, unlike wall-clock time)
        IncrementalValuesProvider<int> provider = default;
        var allocated = ScalingAssert.AllocatedBytes(() =>
        {
            for (var i = 0; i < 1_000; i++)
                provider = new IncrementalValuesProvider<int>(node, graph, metadata);
        });
        var allocatedLarge = ScalingAssert.AllocatedBytes(() =>
        {
            for (var i = 0; i < 1_000; i++)
                provider = new IncrementalValuesProvider<int>(largeNode, graph, metadata);
        });

        // Assert - Creation is O(1): the provider is a struct wrapping the node, so creating one
        // allocates nothing and neither copies nor enumerates the values
        Assert.Equal(0, allocated);
        Assert.Equal(0, allocatedLarge);
        Assert.Same(largeNode, provider.Node);
        Assert.True(typeof(IncrementalValuesProvider<int>).IsValueType);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Combine method combines values provider with value provider
    /// Similar to Roslyn's IncrementalValuesProvider.Combine
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_Combine_ShouldCombineWithSingleValue()
    {
        // Arrange
        var valuesNode = new TestValuesNode<string>("items", ["A", "B", "C"]);
        var valueNode = new TestValueNode<int>("count", 42);
        var graph = new ComputationGraph();

        var valuesProvider = new IncrementalValuesProvider<string>(valuesNode, graph, default);
        var valueProvider = new IncrementalValueProvider<int>(valueNode, graph, default);

        // Act
        var combined = valuesProvider.Combine(valueProvider);
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = combined.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var items = result.Value!.ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal(("A", 42), items[0]);
        Assert.Equal(("B", 42), items[1]);
        Assert.Equal(("C", 42), items[2]);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Combine with empty collection
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_Combine_ShouldHandleEmptyCollection()
    {
        // Arrange
        var valuesNode = new TestValuesNode<string>("empty", Array.Empty<string>());
        var valueNode = new TestValueNode<int>("value", 10);
        var graph = new ComputationGraph();

        var valuesProvider = new IncrementalValuesProvider<string>(valuesNode, graph, default);
        var valueProvider = new IncrementalValueProvider<int>(valueNode, graph, default);

        // Act
        var combined = valuesProvider.Combine(valueProvider);
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = combined.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Merge combines two values providers into one
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_Merge_ShouldCombineTwoCollections()
    {
        // Arrange
        var first = new TestValuesNode<string>("first", ["A", "B"]);
        var second = new TestValuesNode<string>("second", ["C", "D", "E"]);
        var graph = new ComputationGraph();

        var firstProvider = new IncrementalValuesProvider<string>(first, graph, default);
        var secondProvider = new IncrementalValuesProvider<string>(second, graph, default);

        // Act
        var merged = firstProvider.Merge(secondProvider);
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = merged.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var items = result.Value!.ToArray();
        Assert.Equal(5, items.Length);
        Assert.Equal("A", items[0]);
        Assert.Equal("B", items[1]);
        Assert.Equal("C", items[2]);
        Assert.Equal("D", items[3]);
        Assert.Equal("E", items[4]);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Merge with empty collections
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_Merge_ShouldHandleEmptyCollections()
    {
        // Arrange
        var first = new TestValuesNode<int>("first", [1, 2, 3]);
        var second = new TestValuesNode<int>("second", Array.Empty<int>());
        var graph = new ComputationGraph();

        var firstProvider = new IncrementalValuesProvider<int>(first, graph, default);
        var secondProvider = new IncrementalValuesProvider<int>(second, graph, default);

        // Act
        var merged = firstProvider.Merge(secondProvider);
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = merged.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var items = result.Value!.ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal(1, items[0]);
        Assert.Equal(2, items[1]);
        Assert.Equal(3, items[2]);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Merge can be chained multiple times
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_Merge_ShouldSupportChaining()
    {
        // Arrange
        var p1 = new TestValuesNode<string>("p1", ["A"]);
        var p2 = new TestValuesNode<string>("p2", ["B"]);
        var p3 = new TestValuesNode<string>("p3", ["C"]);
        var graph = new ComputationGraph();

        var provider1 = new IncrementalValuesProvider<string>(p1, graph, default);
        var provider2 = new IncrementalValuesProvider<string>(p2, graph, default);
        var provider3 = new IncrementalValuesProvider<string>(p3, graph, default);

        // Act - Chain multiple merges
        var merged = provider1.Merge(provider2).Merge(provider3);
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = merged.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var items = result.Value!.ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal("A", items[0]);
        Assert.Equal("B", items[1]);
        Assert.Equal("C", items[2]);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Flatten for IncrementalValueProvider with nested arrays
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_Flatten_ShouldFlattenNestedArrays()
    {
        // Arrange
        var nestedArray = ImmutableArray.Create(
            ImmutableArray.Create(1, 2, 3),
            ImmutableArray.Create(4, 5),
            ImmutableArray.Create(6, 7, 8, 9)
        );
        var valueNode = new TestValueNode<ImmutableArray<ImmutableArray<int>>>("nested", nestedArray);
        var graph = new ComputationGraph();
        var valueProvider = new IncrementalValueProvider<ImmutableArray<ImmutableArray<int>>>(valueNode, graph, default);

        // Act
        var flattened = valueProvider.Flatten();
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = flattened.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var items = result.Value!.ToArray();
        Assert.Equal(9, items.Length);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, items);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Flatten for IncrementalValuesProvider with array elements
    /// </summary>
    [Fact]
    public void IncrementalValuesProvider_Flatten_ShouldFlattenArrayElements()
    {
        // Arrange
        var arrays = new ImmutableArray<string>[]
        {
            ImmutableArray.Create("A", "B"),
            ImmutableArray.Create("C"),
            ImmutableArray.Create("D", "E", "F")
        };
        var valuesNode = new TestValuesNode<ImmutableArray<string>>("arrays", arrays);
        var graph = new ComputationGraph();
        var valuesProvider = new IncrementalValuesProvider<ImmutableArray<string>>(valuesNode, graph, default);

        // Act
        var flattened = valuesProvider.Flatten();
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = flattened.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var items = result.Value!.ToArray();
        Assert.Equal(6, items.Length);
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F" }, items);
    }

    /// <summary>
    /// FUNCTIONALITY: Test Flatten with empty nested arrays
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_Flatten_ShouldHandleEmptyNestedArrays()
    {
        // Arrange
        var nestedArray = ImmutableArray.Create(
            ImmutableArray.Create(1, 2),
            ImmutableArray<int>.Empty,
            ImmutableArray.Create(3)
        );
        var valueNode = new TestValueNode<ImmutableArray<ImmutableArray<int>>>("nested", nestedArray);
        var graph = new ComputationGraph();
        var valueProvider = new IncrementalValueProvider<ImmutableArray<ImmutableArray<int>>>(valueNode, graph, default);

        // Act
        var flattened = valueProvider.Flatten();
        var cachedValues = new Dictionary<string, object>();
        var context = new ComputationContext(cachedValues);
        var result = flattened.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var items = result.Value!.ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal(new[] { 1, 2, 3 }, items);
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
