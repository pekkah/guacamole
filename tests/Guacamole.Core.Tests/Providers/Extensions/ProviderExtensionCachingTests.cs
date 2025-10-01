using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;
using Guacamole.Core.Providers;
using Guacamole.Core.Providers.Extensions;
using Xunit;

namespace Guacamole.Core.Tests.Providers.Extensions;

/// <summary>
/// Integration tests verifying that caching works correctly with provider extension methods.
/// Tests cache hits, cache misses, invalidation, and performance characteristics.
/// </summary>
public class ProviderExtensionCachingTests
{
    /// <summary>
    /// Test that Select creates a cached node that reuses computation results
    /// </summary>
    [Fact]
    public void Select_ShouldCache_TransformationResults()
    {
        // Arrange
        var computeCount = 0;
        var sourceNode = new CountingValueNode<int>("source", () => { computeCount++; return 42; });
        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValueProvider<int>(sourceNode, graph, default);

        // Act - Apply Select transformation
        var transformedProvider = provider.Select(x => x * 2);

        // Add the transformed node to the graph
        graph = transformedProvider.Graph.AddNode(transformedProvider.Node);

        // Compute twice - first should cache, second should reuse
        var result1 = ComputeWithCaching(transformedProvider.Node, graph);
        var result2 = ComputeWithCaching(transformedProvider.Node, graph);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(84, result1.Value);
        Assert.True(result2.IsSuccess);
        Assert.Equal(84, result2.Value);

        // Source should only be computed once if caching works
        Assert.Equal(1, computeCount);
    }

    /// <summary>
    /// Helper method that computes a node with caching support
    /// </summary>
    private static Result<T, ComputationError> ComputeWithCaching<T>(
        IComputationNode<T> node,
        ComputationGraph graph)
    {
        // Create context with current cache state as a readonly dictionary
        var cachedValues = GetCachedValuesAsDictionary(graph, node);
        var context = new ComputationContext(cachedValues);

        // Compute the node
        var result = node.Compute(context);

        // If successful, cache the result
        if (result.IsSuccess)
        {
            graph.ValueCache.Set(node.Id, result.Value!);
        }

        return result;
    }

    /// <summary>
    /// Helper for collection nodes
    /// </summary>
    private static Result<IEnumerable<T>, ComputationError> ComputeWithCaching<T>(
        ICollectionComputationNode<T> node,
        ComputationGraph graph)
    {
        // Create context with current cache state as a readonly dictionary
        var cachedValues = GetCachedValuesAsDictionary(graph, node);
        var context = new ComputationContext(cachedValues);

        // Compute the node
        var result = node.Compute(context);

        // If successful, cache the result
        if (result.IsSuccess)
        {
            graph.ValueCache.Set(node.Id, result.Value!);
        }

        return result;
    }

    /// <summary>
    /// Creates a readonly dictionary from the cache for nodes that might be needed
    /// </summary>
    private static IReadOnlyDictionary<string, object> GetCachedValuesAsDictionary(
        ComputationGraph graph,
        IComputationNode node)
    {
        var dict = new Dictionary<string, object>();

        // Add all dependencies and their dependencies (transitively)
        var toCheck = new Queue<string>(node.Dependencies);
        var checked_ = new HashSet<string>();

        while (toCheck.Count > 0)
        {
            var dep = toCheck.Dequeue();
            if (checked_.Contains(dep))
                continue;

            checked_.Add(dep);

            // Try to get cached value for this dependency
            if (graph.ValueCache.TryGet(dep, out var value) && value != null)
            {
                dict[dep] = value;
            }

            // Add dependencies of this dependency
            if (graph.Nodes.TryGetValue(dep, out var depNode))
            {
                foreach (var transDep in depNode.Dependencies)
                {
                    toCheck.Enqueue(transDep);
                }
            }
        }

        // Also check if the node itself is cached
        if (graph.ValueCache.TryGet(node.Id, out var nodeValue) && nodeValue != null)
        {
            dict[node.Id] = nodeValue;
        }

        return dict;
    }

    /// <summary>
    /// Test that chained extension methods (Select->Where->Select) cache intermediate results
    /// </summary>
    [Fact]
    public void ChainedExtensions_ShouldCache_IntermediateResults()
    {
        // Arrange
        var sourceComputeCount = 0;
        var selectComputeCount = 0;
        var whereComputeCount = 0;

        var sourceNode = new CountingCollectionNode<int>("source", () =>
        {
            sourceComputeCount++;
            return new[] { 1, 2, 3, 4, 5 };
        });

        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act - Create a chain: Select -> Where -> Select
        var pipeline = provider
            .Select(x => { selectComputeCount++; return x * 2; })  // 2, 4, 6, 8, 10
            .Where(x => { whereComputeCount++; return x > 5; })     // 6, 8, 10
            .Select(x => x + 1);                                     // 7, 9, 11

        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Compute the pipeline twice
        var result1 = pipeline.Node.Compute(context);
        var result2 = pipeline.Node.Compute(context);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(new[] { 7, 9, 11 }, result1.Value!.ToArray());
        Assert.True(result2.IsSuccess);
        Assert.Equal(new[] { 7, 9, 11 }, result2.Value!.ToArray());

        // With caching, source should only compute once
        Assert.Equal(1, sourceComputeCount);

        // Each item goes through select twice (once per computation if no caching)
        // With perfect caching, this should be 5 (number of items in source)
        // Without caching, this would be 10 (5 items * 2 computations)
        Assert.True(selectComputeCount <= 5,
            $"Select called {selectComputeCount} times, expected ≤5 with caching");
    }

    /// <summary>
    /// Test that SelectMany caches flattening operations
    /// </summary>
    [Fact]
    public void SelectMany_ShouldCache_FlatteningResults()
    {
        // Arrange
        var computeCount = 0;
        var sourceNode = new CountingCollectionNode<int>("source", () =>
        {
            computeCount++;
            return new[] { 1, 2, 3 };
        });

        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act - SelectMany that doubles each value
        var flattenedProvider = provider.SelectMany(x => new[] { x, x * 10 });
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Compute twice
        var result1 = flattenedProvider.Node.Compute(context);
        var result2 = flattenedProvider.Node.Compute(context);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(new[] { 1, 10, 2, 20, 3, 30 }, result1.Value!.ToArray());
        Assert.True(result2.IsSuccess);
        Assert.Equal(new[] { 1, 10, 2, 20, 3, 30 }, result2.Value!.ToArray());

        // Source should only compute once with caching
        Assert.Equal(1, computeCount);
    }

    /// <summary>
    /// Test that Collect caches the collection operation
    /// </summary>
    [Fact]
    public void Collect_ShouldCache_CollectionResults()
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

        // Act - Collect into ImmutableArray
        var collectedProvider = provider.Collect();
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Compute twice
        var result1 = collectedProvider.Node.Compute(context);
        var result2 = collectedProvider.Node.Compute(context);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result1.Value!.ToArray());
        Assert.True(result2.IsSuccess);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result2.Value!.ToArray());

        // Source should only compute once with caching
        Assert.Equal(1, computeCount);
    }

    /// <summary>
    /// Test that Combine caches the combination of two providers
    /// </summary>
    [Fact]
    public void Combine_ShouldCache_CombinedResults()
    {
        // Arrange
        var computeCount1 = 0;
        var computeCount2 = 0;

        var sourceNode1 = new CountingValueNode<int>("source1", () => { computeCount1++; return 42; });
        var sourceNode2 = new CountingValueNode<string>("source2", () => { computeCount2++; return "test"; });

        var graph = new ComputationGraph().AddNode(sourceNode1).AddNode(sourceNode2);
        var provider1 = new IncrementalValueProvider<int>(sourceNode1, graph, default);
        var provider2 = new IncrementalValueProvider<string>(sourceNode2, graph, default);

        // Act - Combine the two providers
        var combinedProvider = provider1.Combine(provider2);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Compute twice
        var result1 = combinedProvider.Node.Compute(context);
        var result2 = combinedProvider.Node.Compute(context);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal((42, "test"), result1.Value);
        Assert.True(result2.IsSuccess);
        Assert.Equal((42, "test"), result2.Value);

        // Each source should only compute once with caching
        Assert.Equal(1, computeCount1);
        Assert.Equal(1, computeCount2);
    }

    /// <summary>
    /// Test that Where caches filtering results
    /// </summary>
    [Fact]
    public void Where_ShouldCache_FilteredResults()
    {
        // Arrange - single value
        var computeCount = 0;
        var sourceNode = new CountingValueNode<int>("source", () => { computeCount++; return 42; });
        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValueProvider<int>(sourceNode, graph, default);

        // Act - Filter with Where
        var filteredProvider = provider.Where(x => x > 40);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Compute twice
        var result1 = filteredProvider.Node.Compute(context);
        var result2 = filteredProvider.Node.Compute(context);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(42, result1.Value);
        Assert.True(result2.IsSuccess);
        Assert.Equal(42, result2.Value);

        // Source should only compute once with caching
        Assert.Equal(1, computeCount);
    }

    /// <summary>
    /// Test that Where on collections caches filtering results
    /// </summary>
    [Fact]
    public void WhereOnCollection_ShouldCache_FilteredResults()
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

        // Act - Filter even numbers
        var filteredProvider = provider.Where(x => x % 2 == 0);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Compute twice
        var result1 = filteredProvider.Node.Compute(context);
        var result2 = filteredProvider.Node.Compute(context);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(new[] { 2, 4 }, result1.Value!.ToArray());
        Assert.True(result2.IsSuccess);
        Assert.Equal(new[] { 2, 4 }, result2.Value!.ToArray());

        // Source should only compute once with caching
        Assert.Equal(1, computeCount);
    }

    /// <summary>
    /// Test complex pipeline: Select -> Where -> SelectMany -> Collect
    /// Verifies caching at each stage
    /// </summary>
    [Fact]
    public void ComplexPipeline_ShouldCache_AllStages()
    {
        // Arrange
        var sourceComputeCount = 0;
        var sourceNode = new CountingCollectionNode<int>("source", () =>
        {
            sourceComputeCount++;
            return new[] { 1, 2, 3, 4, 5 };
        });

        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act - Build complex pipeline
        var pipeline = provider
            .Select(x => x * 2)              // 2, 4, 6, 8, 10
            .Where(x => x > 4)               // 6, 8, 10
            .SelectMany(x => new[] { x, x + 1 })  // 6,7, 8,9, 10,11
            .Collect();                       // ImmutableArray

        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Compute three times to really test caching
        var result1 = pipeline.Node.Compute(context);
        var result2 = pipeline.Node.Compute(context);
        var result3 = pipeline.Node.Compute(context);

        // Assert
        Assert.True(result1.IsSuccess);
        var expected = new[] { 6, 7, 8, 9, 10, 11 };
        Assert.Equal(expected, result1.Value!.ToArray());
        Assert.True(result2.IsSuccess);
        Assert.Equal(expected, result2.Value!.ToArray());
        Assert.True(result3.IsSuccess);
        Assert.Equal(expected, result3.Value!.ToArray());

        // Source should only compute once even with 3 pipeline executions
        Assert.Equal(1, sourceComputeCount);
    }

    /// <summary>
    /// Test that cache invalidation works when context changes
    /// </summary>
    [Fact]
    public void CacheInvalidation_WhenContextChanges_ShouldRecompute()
    {
        // Arrange
        var computeCount = 0;
        var sourceNode = new ContextDependentNode<int>("context-source", ctx =>
        {
            computeCount++;
            // Return different value based on cached values in context
            if (ctx.CachedValues.TryGetValue("multiplier", out var mult))
                return 42 * (int)mult;
            return 42;
        });

        var graph = new ComputationGraph().AddNode(sourceNode);
        var provider = new IncrementalValueProvider<int>(sourceNode, graph, default);

        var transformedProvider = provider.Select(x => x + 10);

        // Act - Compute with different contexts
        var context1 = new ComputationContext(
            ImmutableDictionary<string, object>.Empty.Add("multiplier", 1));
        var context2 = new ComputationContext(
            ImmutableDictionary<string, object>.Empty.Add("multiplier", 2));

        var result1 = transformedProvider.Node.Compute(context1);
        var result2 = transformedProvider.Node.Compute(context1); // Same context - should use cache
        var result3 = transformedProvider.Node.Compute(context2); // Different context - should recompute

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.Equal(52, result1.Value); // (42 * 1) + 10
        Assert.True(result2.IsSuccess);
        Assert.Equal(52, result2.Value); // Cached
        Assert.True(result3.IsSuccess);
        Assert.Equal(94, result3.Value); // (42 * 2) + 10, recomputed

        // Should compute once for context1, once for context2
        // If caching works, should be exactly 2, not 3
        Assert.Equal(2, computeCount);
    }
}

/// <summary>
/// Test node that counts how many times Compute is called
/// </summary>
internal class CountingValueNode<T> : IComputationNode<T>
{
    private readonly Func<T> _valueFactory;

    public CountingValueNode(string id, Func<T> valueFactory)
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
        return Result<T, ComputationError>.Success(_valueFactory());
    }
}

/// <summary>
/// Test collection node that counts how many times Compute is called
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
        return Result<IEnumerable<T>, ComputationError>.Success(_valueFactory());
    }
}

/// <summary>
/// Test node that produces different values based on context
/// </summary>
internal class ContextDependentNode<T> : IComputationNode<T>
{
    private readonly Func<ComputationContext, T> _valueFactory;

    public ContextDependentNode(string id, Func<ComputationContext, T> valueFactory)
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
        return Result<T, ComputationError>.Success(_valueFactory(context));
    }
}
