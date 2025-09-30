using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;

namespace Guacamole.Core.Providers;

/// <summary>
/// Immutable structure representing a single value in a transformation pipeline
/// with automatic caching and incremental computation.
/// Thread-safe without synchronization, supports pipeline transformations via extension methods.
/// </summary>
/// <typeparam name="T">Type of the value (must be equatable for caching)</typeparam>
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
}

/// <summary>
/// Immutable structure representing zero or more values in a transformation pipeline
/// with filtering, collection, and transformation capabilities.
/// Thread-safe collection operations with efficient enumeration and lazy evaluation.
/// </summary>
/// <typeparam name="T">Type of the values</typeparam>
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
}