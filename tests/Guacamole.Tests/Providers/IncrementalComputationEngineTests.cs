using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;
using Guacamole;
using Xunit;

namespace Guacamole.Core.Tests.Providers;

/// <summary>
/// End-to-end tests verifying the complete incremental computation engine.
/// Tests the full orchestration: GetValue(), caching, invalidation, and recomputation.
/// </summary>
public class IncrementalComputationEngineTests
{
    /// <summary>
    /// Test the complete flow: compute, cache, retrieve from cache
    /// </summary>
    [Fact]
    public void GetValue_ShouldCache_AndReuseResults()
    {
        // Arrange
        var computeCount = 0;
        var sourceNode = new CountingNode<int>("source", () => { computeCount++; return 42; });
        var graph = new ComputationGraph().AddNode(sourceNode);

        var provider = new IncrementalValueProvider<int>(
            sourceNode,
            graph,
            new ProviderMetadata(TrackingName: "test"));

        // Act - Call GetValue twice
        var result1 = provider.GetValue();
        var result2 = provider.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(42, result1.Value);
        Assert.True(result2.IsSuccess);
        Assert.Equal(42, result2.Value);

        // Source should only compute once - second call uses cache!
        Assert.Equal(1, computeCount);
    }

    /// <summary>
    /// Test that transformation pipelines cache at each stage
    /// </summary>
    [Fact]
    public void Pipeline_ShouldCache_IntermediateResults()
    {
        // Arrange
        var sourceComputeCount = 0;
        var transformComputeCount = 0;

        var sourceNode = new CountingNode<int>("source", () => { sourceComputeCount++; return 10; });
        var graph = new ComputationGraph().AddNode(sourceNode);

        var provider = new IncrementalValueProvider<int>(sourceNode, graph, default);

        // Create a pipeline: source -> multiply by 2 -> add 5
        var pipeline = provider
            .Select(x => { transformComputeCount++; return x * 2; })  // 20
            .Select(x => x + 5);                                       // 25

        // Add nodes to graph
        graph = pipeline.Graph
            .AddNode(provider.Select(x => { transformComputeCount++; return x * 2; }).Node)
            .AddNode(pipeline.Node);

        // Act - Get value twice
        var result1 = pipeline.GetValue();
        var result2 = pipeline.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(25, result1.Value);
        Assert.True(result2.IsSuccess);
        Assert.Equal(25, result2.Value);

        // Source should only compute once
        Assert.Equal(1, sourceComputeCount);

        // Transform should compute once per value (1 source value * 1 computation)
        // Without caching, this would be 2
        Assert.Equal(1, transformComputeCount);
    }

    /// <summary>
    /// Test that GetValues() works for collection providers
    /// </summary>
    [Fact]
    public void GetValues_ShouldCache_CollectionResults()
    {
        // Arrange
        var computeCount = 0;
        var sourceNode = new CountingCollectionNode<int>("source", () =>
        {
            computeCount++;
            return new[] { 1, 2, 3, 4, 5 };
        });

        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act - Get values twice
        var result1 = provider.GetValues();
        var result2 = provider.GetValues();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result1.Value!.ToArray());
        Assert.True(result2.IsSuccess);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result2.Value!.ToArray());

        // Source should only compute once - second call uses cache
        Assert.Equal(1, computeCount);
    }

    /// <summary>
    /// Test collection pipeline with filtering and transformation
    /// </summary>
    [Fact]
    public void CollectionPipeline_ShouldCache_AllStages()
    {
        // Arrange
        var sourceComputeCount = 0;
        var sourceNode = new CountingCollectionNode<int>("source", () =>
        {
            sourceComputeCount++;
            return new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        });

        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Build pipeline: filter even numbers, multiply by 2, collect
        var pipeline = provider
            .Where(x => x % 2 == 0)      // 2, 4, 6, 8, 10
            .Select(x => x * 2)           // 4, 8, 12, 16, 20
            .Collect();                   // ImmutableArray

        // Act - Get values three times
        var result1 = pipeline.GetValue();
        var result2 = pipeline.GetValue();
        var result3 = pipeline.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        var expected = new[] { 4, 8, 12, 16, 20 };
        Assert.Equal(expected, result1.Value!.ToArray());
        Assert.True(result2.IsSuccess);
        Assert.Equal(expected, result2.Value!.ToArray());
        Assert.True(result3.IsSuccess);
        Assert.Equal(expected, result3.Value!.ToArray());

        // Source should only compute once even with 3 retrievals
        Assert.Equal(1, sourceComputeCount);
    }

    /// <summary>
    /// Test that combining multiple providers caches results
    /// </summary>
    [Fact]
    public void Combine_ShouldCache_BothBranches()
    {
        // Arrange
        var compute1Count = 0;
        var compute2Count = 0;

        var node1 = new CountingNode<int>("source1", () => { compute1Count++; return 42; });
        var node2 = new CountingNode<string>("source2", () => { compute2Count++; return "test"; });

        var graph = new ComputationGraph().AddNode(node1).AddNode(node2);

        var provider1 = new IncrementalValueProvider<int>(node1, graph, default);
        var provider2 = new IncrementalValueProvider<string>(node2, graph, default);

        // Combine them
        var combined = provider1.Combine(provider2);

        // Act - Get value twice
        var result1 = combined.GetValue();
        var result2 = combined.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal((42, "test"), result1.Value);
        Assert.True(result2.IsSuccess);
        Assert.Equal((42, "test"), result2.Value);

        // Each source should only compute once
        Assert.Equal(1, compute1Count);
        Assert.Equal(1, compute2Count);
    }

    /// <summary>
    /// Test that cached results are reused across multiple GetValue calls
    /// </summary>
    [Fact]
    public void MultipleGetValues_ShouldReuse_CachedResults()
    {
        // Arrange
        var sourceComputeCount = 0;
        var transform1Count = 0;
        var transform2Count = 0;

        var sourceNode = new CountingNode<int>("source", () => { sourceComputeCount++; return 10; });
        var graph = new ComputationGraph().AddNode(sourceNode);

        var source = new IncrementalValueProvider<int>(sourceNode, graph, default);

        // Build a pipeline with multiple stages
        var step1 = source.Select(x => { transform1Count++; return x * 2; });        // 20
        var step2 = step1.Select(x => { transform2Count++; return x + 5; });         // 25

        // Act - Get values multiple times
        var result1 = step2.GetValue();
        var result2 = step2.GetValue();
        var result3 = step2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(25, result1.Value);
        Assert.True(result2.IsSuccess);
        Assert.Equal(25, result2.Value);
        Assert.True(result3.IsSuccess);
        Assert.Equal(25, result3.Value);

        // With caching, each stage computes exactly once
        Assert.Equal(1, sourceComputeCount);
        Assert.Equal(1, transform1Count);
        Assert.Equal(1, transform2Count);
    }
}

/// <summary>
/// Test node that counts computations
/// </summary>
internal class CountingNode<T> : IComputationNode<T>
{
    private readonly Func<T> _valueFactory;

    public CountingNode(string id, Func<T> valueFactory)
    {
        Id = id;
        _valueFactory = valueFactory;
        OutputType = typeof(T);
        Dependencies = ImmutableHashSet<string>.Empty;
    }

    public string Id { get; }
    public Type OutputType { get; }
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<T, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first (nodes are responsible for this)
        if (context.CachedValues.TryGetValue(Id, out var cached) && cached is T cachedValue)
        {
            return Result<T, ComputationError>.Success(cachedValue);
        }

        return Result<T, ComputationError>.Success(_valueFactory());
    }
}

/// <summary>
/// Test collection node that counts computations
/// </summary>
internal class CountingCollectionNode<T> : ICollectionComputationNode<T>
{
    private readonly Func<IEnumerable<T>> _valueFactory;

    public CountingCollectionNode(string id, Func<IEnumerable<T>> valueFactory)
    {
        Id = id;
        _valueFactory = valueFactory;
        OutputType = typeof(IEnumerable<T>);
        Dependencies = ImmutableHashSet<string>.Empty;
    }

    public string Id { get; }
    public Type OutputType { get; }
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cached) && cached is IEnumerable<T> cachedValue)
        {
            return Result<IEnumerable<T>, ComputationError>.Success(cachedValue);
        }

        return Result<IEnumerable<T>, ComputationError>.Success(_valueFactory());
    }
}
