using System.Collections.Generic;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;

namespace Guacamole.Core.Providers;

/// <summary>
/// Immutable structure representing a single value in a transformation pipeline
/// with automatic caching and incremental computation.
/// Thread-safe without synchronization, supports pipeline transformations via extension methods.
///
/// <para>
/// <b>Equality Requirements:</b> For optimal incremental computation performance,
/// type <typeparamref name="T"/> should implement proper value equality:
/// </para>
/// <list type="bullet">
/// <item><description>Use record types (automatic structural equality)</description></item>
/// <item><description>Use value types with proper Equals() implementation</description></item>
/// <item><description>Implement IEquatable&lt;T&gt; for custom reference types</description></item>
/// <item><description>Override GetHashCode() when overriding Equals()</description></item>
/// </list>
/// <para>
/// Similar to Roslyn's incremental generators, the framework uses EqualityComparer&lt;T&gt;.Default
/// to compare old and new values. Only when values differ will dependent computations be invalidated
/// and output handlers be triggered.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the value (should implement proper equality for optimal performance)</typeparam>
public readonly record struct IncrementalValueProvider<T>(
    IComputationNode<T> Node,
    ComputationGraph Graph,
    ProviderMetadata Metadata)
{
    /// <summary>
    /// Internal computation node (implementation detail).
    /// </summary>
    public IComputationNode<T> Node { get; } = Node ?? throw new ArgumentNullException(nameof(Node));

    /// <summary>
    /// Shared computation graph for dependency tracking.
    /// </summary>
    public ComputationGraph Graph { get; } = Graph ?? throw new ArgumentNullException(nameof(Graph));

    /// <summary>
    /// Debugging and tracking information.
    /// </summary>
    public ProviderMetadata Metadata { get; } = Metadata;

    /// <summary>
    /// Gets the computed value with automatic caching orchestration.
    /// This is the main entry point for retrieving values - it handles cache lookup,
    /// computation, and cache population automatically.
    /// </summary>
    /// <returns>Result containing the computed value or error</returns>
    public Result<T, ComputationError> GetValue()
    {
        // Create context with current cache state
        var cachedValues = BuildCacheContext(Node, Graph);
        var context = new ComputationContext(cachedValues);

        // Compute the value (will use cache if available)
        var result = Node.Compute(context);

        // Store successful results in cache
        if (result.IsSuccess && result.Value != null)
        {
            Graph.ValueCache.Set(Node.Id, result.Value);
        }

        return result;
    }

    /// <summary>
    /// Convenience property to get the computed value directly.
    /// Throws if computation fails. Use GetValue() for Result-based error handling.
    /// </summary>
    public T Value
    {
        get
        {
            var result = GetValue();
            if (result.IsSuccess)
                return result.Value!;

            throw new InvalidOperationException(
                $"Failed to compute value: {result.ErrorValue?.Message ?? "Unknown error"}");
        }
    }

    /// <summary>
    /// Builds a dictionary of cached values relevant to this computation.
    /// Includes the node itself and all transitive dependencies.
    /// </summary>
    private static IReadOnlyDictionary<string, object> BuildCacheContext(
        IComputationNode node,
        ComputationGraph graph)
    {
        var dict = new Dictionary<string, object>();

        // Check if the node itself is cached
        if (graph.ValueCache.TryGet(node.Id, out var nodeValue) && nodeValue != null)
        {
            dict[node.Id] = nodeValue;
        }

        // Add all dependencies and their transitive dependencies
        var toCheck = new Queue<string>(node.Dependencies);
        var checked_ = new HashSet<string>();

        while (toCheck.Count > 0)
        {
            var depId = toCheck.Dequeue();
            if (checked_.Contains(depId))
                continue;

            checked_.Add(depId);

            // Try to get cached value for this dependency
            if (graph.ValueCache.TryGet(depId, out var depValue) && depValue != null)
            {
                dict[depId] = depValue;
            }

            // Add transitive dependencies
            if (graph.Nodes.TryGetValue(depId, out var depNode))
            {
                foreach (var transDep in depNode.Dependencies)
                {
                    toCheck.Enqueue(transDep);
                }
            }
        }

        return dict;
    }
}

/// <summary>
/// Immutable structure representing zero or more values in a transformation pipeline
/// with filtering, collection, and transformation capabilities.
/// Thread-safe collection operations with efficient enumeration and lazy evaluation.
///
/// <para>
/// <b>Equality Requirements:</b> For optimal incremental computation performance,
/// element type <typeparamref name="T"/> should implement proper value equality:
/// </para>
/// <list type="bullet">
/// <item><description>Use record types (automatic structural equality)</description></item>
/// <item><description>Use value types with proper Equals() implementation</description></item>
/// <item><description>Implement IEquatable&lt;T&gt; for custom reference types</description></item>
/// </list>
/// <para>
/// Collection changes are detected using SequenceEqual with EqualityComparer&lt;T&gt;.Default.
/// Output handlers are only triggered when the collection contents actually change.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the values (should implement proper equality for optimal performance)</typeparam>
public readonly record struct IncrementalValuesProvider<T>(
    ICollectionComputationNode<T> Node,
    ComputationGraph Graph,
    ProviderMetadata Metadata)
{
    /// <summary>
    /// Internal computation node for multiple values.
    /// </summary>
    public ICollectionComputationNode<T> Node { get; } = Node ?? throw new ArgumentNullException(nameof(Node));

    /// <summary>
    /// Shared computation graph for dependency tracking.
    /// </summary>
    public ComputationGraph Graph { get; } = Graph ?? throw new ArgumentNullException(nameof(Graph));

    /// <summary>
    /// Debugging and tracking information.
    /// </summary>
    public ProviderMetadata Metadata { get; } = Metadata;

    /// <summary>
    /// Gets the computed values with automatic caching orchestration.
    /// This is the main entry point for retrieving values - it handles cache lookup,
    /// computation, and cache population automatically.
    /// </summary>
    /// <returns>Result containing the computed values or error</returns>
    public Result<IEnumerable<T>, ComputationError> GetValues()
    {
        // Create context with current cache state
        var cachedValues = BuildCacheContext(Node, Graph);
        var context = new ComputationContext(cachedValues);

        // Compute the values (will use cache if available)
        var result = Node.Compute(context);

        // Store successful results in cache
        if (result.IsSuccess && result.Value != null)
        {
            Graph.ValueCache.Set(Node.Id, result.Value);
        }

        return result;
    }

    /// <summary>
    /// Convenience property to get the computed values directly.
    /// Throws if computation fails. Use GetValues() for Result-based error handling.
    /// </summary>
    public IEnumerable<T> Values
    {
        get
        {
            var result = GetValues();
            if (result.IsSuccess)
                return result.Value!;

            throw new InvalidOperationException(
                $"Failed to compute values: {result.ErrorValue?.Message ?? "Unknown error"}");
        }
    }

    /// <summary>
    /// Builds a dictionary of cached values relevant to this computation.
    /// Includes the node itself and all transitive dependencies.
    /// </summary>
    private static IReadOnlyDictionary<string, object> BuildCacheContext(
        IComputationNode node,
        ComputationGraph graph)
    {
        var dict = new Dictionary<string, object>();

        // Check if the node itself is cached
        if (graph.ValueCache.TryGet(node.Id, out var nodeValue) && nodeValue != null)
        {
            dict[node.Id] = nodeValue;
        }

        // Add all dependencies and their transitive dependencies
        var toCheck = new Queue<string>(node.Dependencies);
        var checked_ = new HashSet<string>();

        while (toCheck.Count > 0)
        {
            var depId = toCheck.Dequeue();
            if (checked_.Contains(depId))
                continue;

            checked_.Add(depId);

            // Try to get cached value for this dependency
            if (graph.ValueCache.TryGet(depId, out var depValue) && depValue != null)
            {
                dict[depId] = depValue;
            }

            // Add transitive dependencies
            if (graph.Nodes.TryGetValue(depId, out var depNode))
            {
                foreach (var transDep in depNode.Dependencies)
                {
                    toCheck.Enqueue(transDep);
                }
            }
        }

        return dict;
    }
}
