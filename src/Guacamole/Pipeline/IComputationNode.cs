using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Base interface for all computation nodes in the graph.
/// Supports DAG structure with dependency tracking.
/// </summary>
internal interface IComputationNode
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
internal interface IComputationNode<T> : IComputationNode
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
internal interface ICollectionComputationNode<T> : IComputationNode
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
internal interface IHasComparer<T>
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
/// <remarks>
/// Nodes must read their dependencies through <see cref="Read{T}(IComputationNode{T})"/> /
/// <see cref="Read{T}(ICollectionComputationNode{T})"/>, not by calling their <c>Compute</c>
/// directly: when the context belongs to an evaluation pass, a read brings the dependency up to date
/// (using its cached value when still valid) and records the version that was read, which is what
/// later decides whether the reading node's cached value is still valid.
/// </remarks>
internal readonly record struct ComputationContext(
    IReadOnlyDictionary<string, object> CachedValues,
    CancellationToken CancellationToken = default)
{
    /// <summary>
    /// The evaluation pass this context belongs to; <c>null</c> when a node is computed directly
    /// (dependencies are then computed inline).
    /// </summary>
    internal GraphEvaluator? Evaluator { get; init; }

    /// <summary>
    /// Reads a single-value dependency of the node being computed.
    /// </summary>
    public Result<T, ComputationError> Read<T>(IComputationNode<T> node) =>
        Evaluator is { } evaluator ? evaluator.Read(node) : node.Compute(this);

    /// <summary>
    /// Reads a collection dependency of the node being computed.
    /// </summary>
    public Result<IEnumerable<T>, ComputationError> Read<T>(ICollectionComputationNode<T> node) =>
        Evaluator is { } evaluator ? evaluator.Read(node) : node.Compute(this);

    /// <summary>
    /// Reads a dictionary dependency of the node being computed for one key: returns the dictionary
    /// result, but the reading node depends only on <paramref name="key"/>'s entry (see
    /// <see cref="GraphEvaluator.ReadKey"/>). The node must not use any other entry of the dictionary.
    /// </summary>
    public Result<ImmutableDictionary<TKey, TValue>, ComputationError> ReadKey<TKey, TValue>(IComputationNode<ImmutableDictionary<TKey, TValue>> node, TKey key)
        where TKey : notnull =>
        Evaluator is { } evaluator ? evaluator.ReadKey(node, key) : node.Compute(this);
}
