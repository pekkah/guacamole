using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Base interface for all computation nodes in the graph.
/// Supports DAG structure with dependency tracking.
/// </summary>
public interface IComputationNode
{
    /// <summary>
    /// Unique identifier within computation graph.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The type of value this node produces.
    /// </summary>
    Type OutputType { get; }

    /// <summary>
    /// Direct dependencies of this node.
    /// </summary>
    ImmutableHashSet<string> Dependencies { get; }
}

/// <summary>
/// Single-value computation node.
/// </summary>
/// <typeparam name="T">Type of value produced</typeparam>
public interface IComputationNode<T> : IComputationNode
{
    /// <summary>
    /// Computes the value for this node.
    /// </summary>
    /// <param name="context">Computation context with cache and dependencies</param>
    /// <returns>Result containing computed value or error</returns>
    Result<T, ComputationError> Compute(ComputationContext context);
}

/// <summary>
/// Multiple-values computation node.
/// </summary>
/// <typeparam name="T">Type of values produced</typeparam>
public interface ICollectionComputationNode<T> : IComputationNode
{
    /// <summary>
    /// Computes the values for this node.
    /// </summary>
    /// <param name="context">Computation context with cache and dependencies</param>
    /// <returns>Result containing computed values or error</returns>
    Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context);
}

/// <summary>
/// Interface for computation nodes that support custom equality comparison.
/// When a node implements this interface, version-based caching uses the
/// provided comparer to determine if the computed value has changed.
/// </summary>
/// <typeparam name="T">Type of value the node produces</typeparam>
public interface IHasComparer<T>
{
    /// <summary>
    /// Gets the equality comparer used to compare computed values.
    /// If the comparer indicates two values are equal, the node's version
    /// will not be incremented, enabling downstream nodes to skip recomputation.
    /// </summary>
    IEqualityComparer<T> Comparer { get; }
}

/// <summary>
/// Computation context providing access to cache and dependencies.
/// </summary>
public readonly record struct ComputationContext(
    IReadOnlyDictionary<string, object> CachedValues,
    CancellationToken CancellationToken = default);
