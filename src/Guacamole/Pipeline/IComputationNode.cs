using System.Collections.Immutable;
using Guacamole.Core.Common;

namespace Guacamole.Core.Pipeline;

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

/// <summary>
/// Error information for failed computations.
/// </summary>
/// <param name="Message">Error message</param>
/// <param name="InnerError">Nested error if any</param>
/// <param name="SourceLocation">Location where error occurred</param>
/// <param name="DiagnosticId">Unique diagnostic identifier</param>
public record class ComputationError(
    string Message,
    ComputationError? InnerError = null,
    Diagnostics.SourceLocation? SourceLocation = null,
    string? DiagnosticId = null)
{
    /// <summary>
    /// Creates a ComputationError from an exception.
    /// </summary>
    public static ComputationError FromException(Exception exception, string? diagnosticId = null)
    {
        return new ComputationError(
            Message: exception.Message,
            InnerError: exception.InnerException != null
                ? FromException(exception.InnerException)
                : null,
            DiagnosticId: diagnosticId);
    }
}
