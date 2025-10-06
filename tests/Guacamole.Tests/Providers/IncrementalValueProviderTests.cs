using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;
using Guacamole;
using Xunit;

namespace Guacamole.Core.Tests.Providers;

/// <summary>
/// Tests for IncrementalValueProvider basic functionality following TDD progression:
/// Basic → Edge → Incremental → Performance
/// </summary>
public class IncrementalValueProviderTests
{
    /// <summary>
    /// BASIC: Test that IncrementalValueProvider is an immutable record struct with value equality
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_ShouldBe_ImmutableRecordStruct()
    {
        // Arrange - This will fail until IncrementalValueProvider<T> is implemented
        var node = new TestValueNode<int>("test", 42);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test-provider");

        // Act
        var provider1 = new IncrementalValueProvider<int>(node, graph, metadata);
        var provider2 = new IncrementalValueProvider<int>(node, graph, metadata);

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
    public void IncrementalValueProvider_ShouldHave_RequiredProperties()
    {
        // Arrange
        var node = new TestValueNode<string>("test", "value");
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test-provider");

        // Act
        var provider = new IncrementalValueProvider<string>(node, graph, metadata);

        // Assert
        Assert.NotNull(provider.Node);
        Assert.NotNull(provider.Graph);
        // ProviderMetadata is a value type, no need for null check
        Assert.Equal("test", provider.Node.Id);
    }

    /// <summary>
    /// BASIC: Test provider can be created with different value types
    /// </summary>
    [Theory]
    [InlineData(42)]
    [InlineData("test")]
    [InlineData(true)]
    public void IncrementalValueProvider_ShouldSupport_DifferentValueTypes<T>(T value)
    {
        // Arrange
        var node = new TestValueNode<T>("test", value);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test");

        // Act & Assert - Should not throw
        var provider = new IncrementalValueProvider<T>(node, graph, metadata);
        // Provider is a value type, no need for null check
    }

    /// <summary>
    /// EDGE: Test validation rules for constructor parameters
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_ShouldValidate_ConstructorParameters()
    {
        // Arrange
        var node = new TestValueNode<int>("test", 42);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test");

        // Act & Assert - Null node should throw
        Assert.Throws<ArgumentNullException>(() =>
            new IncrementalValueProvider<int>(null!, graph, metadata));

        // Null graph should throw
        Assert.Throws<ArgumentNullException>(() =>
            new IncrementalValueProvider<int>(node, null!, metadata));

        // Default metadata should be allowed (struct cannot be null)
        var providerWithDefault = new IncrementalValueProvider<int>(node, graph, default);
        Assert.NotNull(providerWithDefault.Node);
    }

    /// <summary>
    /// EDGE: Test provider behavior with empty/default values
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_ShouldHandle_DefaultValues()
    {
        // Arrange
        var node = new TestValueNode<int?>("test", null);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "test");

        // Act
        var provider = new IncrementalValueProvider<int?>(node, graph, metadata);

        // Assert - Should handle nullable types
        // Provider is a value type, no need for null check
        Assert.Equal("test", provider.Node.Id);
    }

    /// <summary>
    /// INCREMENTAL: Test that provider supports caching through computation graph
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_ShouldSupport_CachingThroughGraph()
    {
        // Arrange
        var node = new TestValueNode<string>("cached-test", "cached-value");
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "cached-provider");

        // Act
        var provider = new IncrementalValueProvider<string>(node, graph, metadata);

        // Assert - Graph should be shared for caching
        Assert.Same(graph, provider.Graph);
    }

    /// <summary>
    /// INCREMENTAL: Test that providers with same inputs are equal (for caching)
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_WithSameInputs_ShouldBe_Equal()
    {
        // Arrange
        var node = new TestValueNode<int>("equal-test", 123);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "equal-test");

        // Act
        var provider1 = new IncrementalValueProvider<int>(node, graph, metadata);
        var provider2 = new IncrementalValueProvider<int>(node, graph, metadata);

        // Assert - Critical for caching behavior
        Assert.Equal(provider1, provider2);
        Assert.Equal(provider1.GetHashCode(), provider2.GetHashCode());
    }

    /// <summary>
    /// PERFORMANCE: Test that provider creation is O(1) and doesn't allocate excessively
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_Creation_ShouldBe_Fast()
    {
        // Arrange
        var node = new TestValueNode<long>("perf-test", 999L);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata(TrackingName: "perf-test");

        // Act - Measure creation time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var provider = new IncrementalValueProvider<long>(node, graph, metadata);
        stopwatch.Stop();

        // Assert - Should be very fast (< 1ms for struct creation)
        Assert.True(stopwatch.ElapsedMilliseconds < 1,
            "Provider creation should be sub-millisecond");
        // Provider is a value type, no need for null check
    }

    /// <summary>
    /// FUNCTIONALITY: Test SelectMany transforms single value to multiple values
    /// Similar to Roslyn's IncrementalValueProvider.SelectMany
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_SelectMany_ShouldTransformToMultipleValues()
    {
        // Arrange
        var valueNode = new TestValueNode<string>("input", "abc");
        var graph = new ComputationGraph();
        var valueProvider = new IncrementalValueProvider<string>(valueNode, graph, default);

        // Act - SelectMany to split string into characters
        var result = valueProvider.SelectMany(s => s.ToCharArray().Select(c => c.ToString()));
        var context = new ComputationContext(new Dictionary<string, object>());
        var computeResult = result.Node.Compute(context);

        // Assert
        Assert.True(computeResult.IsSuccess);
        var items = computeResult.Value!.ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal("a", items[0]);
        Assert.Equal("b", items[1]);
        Assert.Equal("c", items[2]);
    }

    /// <summary>
    /// FUNCTIONALITY: Test SelectMany with empty result
    /// </summary>
    [Fact]
    public void IncrementalValueProvider_SelectMany_ShouldHandleEmptyResult()
    {
        // Arrange
        var valueNode = new TestValueNode<string>("input", "test");
        var graph = new ComputationGraph();
        var valueProvider = new IncrementalValueProvider<string>(valueNode, graph, default);

        // Act - SelectMany that returns empty collection
        var result = valueProvider.SelectMany(s => Array.Empty<int>());
        var context = new ComputationContext(new Dictionary<string, object>());
        var computeResult = result.Node.Compute(context);

        // Assert
        Assert.True(computeResult.IsSuccess);
        Assert.Empty(computeResult.Value!);
    }
}

/// <summary>
/// Test implementation of IComputationNode for testing purposes
/// </summary>
internal class TestValueNode<T> : IComputationNode<T>
{
    public TestValueNode(string id, T value)
    {
        Id = id;
        Value = value;
        OutputType = typeof(T);
        Dependencies = ImmutableHashSet<string>.Empty;
    }

    public string Id { get; }
    public T Value { get; }
    public Type OutputType { get; }
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<T, ComputationError> Compute(ComputationContext context)
    {
        return Result<T, ComputationError>.Success(Value);
    }
}
