using System;
using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Abstract base class for generated computation nodes.
/// Provides common functionality for property-specific nodes in computing contexts.
/// </summary>
/// <typeparam name="T">Type of value produced by this node</typeparam>
internal abstract class ComputationNodeBase<T> : IComputationNode<T>
{
    /// <summary>
    /// Unique identifier for this node within the computation graph.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Type of value this node produces.
    /// </summary>
    public Type OutputType => typeof(T);

    /// <summary>
    /// Direct dependencies of this node.
    /// </summary>
    public ImmutableHashSet<string> Dependencies { get; protected init; }

    /// <summary>
    /// Creates a new computation node with the specified identifier.
    /// </summary>
    /// <param name="id">Unique node identifier</param>
    /// <param name="dependencies">Optional set of dependency node IDs</param>
    protected ComputationNodeBase(string id, ImmutableHashSet<string>? dependencies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Dependencies = dependencies ?? ImmutableHashSet<string>.Empty;
    }

    /// <summary>
    /// Computes the value for this node.
    /// Derived classes implement the specific computation logic.
    /// </summary>
    /// <param name="context">Computation context with cache and dependencies</param>
    /// <returns>Result containing computed value or error</returns>
    public abstract Result<T, ComputationError> Compute(ComputationContext context);
}

/// <summary>
/// Abstract base class for generated collection computation nodes.
/// Provides common functionality for property-specific collection nodes in computing contexts.
/// </summary>
/// <typeparam name="T">Type of values produced by this node</typeparam>
internal abstract class CollectionComputationNodeBase<T> : ICollectionComputationNode<T>
{
    /// <summary>
    /// Unique identifier for this node within the computation graph.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Type of value this node produces.
    /// </summary>
    public Type OutputType => typeof(IEnumerable<T>);

    /// <summary>
    /// Direct dependencies of this node.
    /// </summary>
    public ImmutableHashSet<string> Dependencies { get; protected init; }

    /// <summary>
    /// Creates a new collection computation node with the specified identifier.
    /// </summary>
    /// <param name="id">Unique node identifier</param>
    /// <param name="dependencies">Optional set of dependency node IDs</param>
    protected CollectionComputationNodeBase(string id, ImmutableHashSet<string>? dependencies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Dependencies = dependencies ?? ImmutableHashSet<string>.Empty;
    }

    /// <summary>
    /// Computes the values for this node.
    /// Derived classes implement the specific computation logic.
    /// </summary>
    /// <param name="context">Computation context with cache and dependencies</param>
    /// <returns>Result containing computed values or error</returns>
    public abstract Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context);
}
