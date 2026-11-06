using System.Collections.Generic;
using System.Collections.Immutable;
using Guacamole.Pipeline;

namespace Guacamole;

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
    /// Gets the computed value with automatic caching orchestration and version-based
    /// cache validation. This is the main entry point for retrieving values.
    /// </summary>
    /// <remarks>
    /// Uses version-based lazy validation:
    /// <list type="number">
    /// <item>Check if cached value exists with valid dependency versions</item>
    /// <item>If valid, return cached value (no recomputation)</item>
    /// <item>If stale, recompute and compare with previous value</item>
    /// <item>Only increment version if value actually changed</item>
    /// </list>
    /// This enables equality-based cache skipping where unchanged intermediate
    /// values prevent downstream recomputation.
    /// </remarks>
    /// <returns>Result containing the computed value or error</returns>
    public Result<T, ComputationError> GetValue()
    {
        // Register this node for cache invalidation tracking if not already registered
        Graph.RegisterNodeIfNeeded(Node);

        // Try to use valid cached value (version-based validation)
        if (TryGetValidCachedValue(out var cachedResult))
        {
            return Result<T, ComputationError>.Success(cachedResult);
        }

        // Snapshot dependency versions BEFORE computing: a change that happens while
        // computing must not be recorded as already seen by this value.
        var depVersions = ProviderCacheCoordinator.SnapshotDependencyVersions(Node, Graph);

        // Create context with current cache state and compute
        var cachedValues = ProviderCacheCoordinator.BuildCacheContext(Node, Graph);
        var context = new ComputationContext(cachedValues);
        var result = Node.Compute(context);

        // Store result with version tracking
        if (result.IsSuccess && result.Value != null)
        {
            var value = result.Value;
            var comparer = GetNodeComparer();
            ProviderCacheCoordinator.Commit(
                Graph,
                Node.Id,
                depVersions,
                value,
                previous => previous is T previousValue && comparer.Equals(previousValue, value));
        }

        return result;
    }

    /// <summary>
    /// Tries to get a valid cached value by checking if the entire dependency chain
    /// is still valid up to source nodes. Uses recursive validation to enable
    /// equality-based cache skipping.
    /// </summary>
    /// <param name="value">The cached value if valid</param>
    /// <returns>True if cached value is valid and can be used</returns>
    private bool TryGetValidCachedValue(out T value)
    {
        value = default!;

        // Use recursive validation to check the entire dependency chain
        if (!ProviderCacheCoordinator.IsCachedValueValid(Node.Id, Graph))
        {
            return false;
        }

        // Get the cached value (we know it exists from IsCachedValueValid)
        if (!Graph.ValueCache.TryGet(Node.Id, out var cached) || cached is not T typedValue)
        {
            return false;
        }

        value = typedValue;
        return true;
    }

    /// <summary>
    /// Gets the equality comparer for this node's value type.
    /// Uses the node's custom comparer if available, otherwise uses default.
    /// </summary>
    private IEqualityComparer<T> GetNodeComparer()
    {
        if (Node is IHasComparer<T> withComparer)
        {
            return withComparer.Comparer;
        }
        return EqualityComparer<T>.Default;
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
    /// Gets the computed values with automatic caching orchestration and version-based
    /// cache validation. This is the main entry point for retrieving values.
    /// </summary>
    /// <remarks>
    /// Uses version-based lazy validation with sequence equality for collections.
    /// </remarks>
    /// <returns>Result containing the computed values or error</returns>
    public Result<IEnumerable<T>, ComputationError> GetValues()
    {
        // Register this node for cache invalidation tracking if not already registered
        Graph.RegisterNodeIfNeeded(Node);

        // Try to use valid cached value (version-based validation)
        if (TryGetValidCachedValues(out var cachedResult))
        {
            return Result<IEnumerable<T>, ComputationError>.Success(cachedResult);
        }

        // Snapshot dependency versions BEFORE computing: a change that happens while
        // computing must not be recorded as already seen by this value.
        var depVersions = ProviderCacheCoordinator.SnapshotDependencyVersions(Node, Graph);

        // Create context with current cache state and compute
        var cachedValues = ProviderCacheCoordinator.BuildCacheContext(Node, Graph);
        var context = new ComputationContext(cachedValues);
        var result = Node.Compute(context);

        // Store result with version tracking
        if (result.IsSuccess && result.Value != null)
        {
            // Materialize to array for comparison and storage
            var currentArray = result.Value as T[] ?? result.Value.ToArray();

            var comparer = GetElementComparer();
            ProviderCacheCoordinator.Commit(
                Graph,
                Node.Id,
                depVersions,
                currentArray,
                previous => previous is IEnumerable<T> previousValues &&
                    SequenceEqual(previousValues as T[] ?? previousValues.ToArray(), currentArray, comparer));

            // Return the array we already materialized
            return Result<IEnumerable<T>, ComputationError>.Success(currentArray);
        }

        return result;
    }

    /// <summary>
    /// Tries to get valid cached values by checking if the entire dependency chain
    /// is still valid up to source nodes. Uses recursive validation to enable
    /// equality-based cache skipping.
    /// </summary>
    private bool TryGetValidCachedValues(out IEnumerable<T> values)
    {
        values = Enumerable.Empty<T>();

        // Use recursive validation to check the entire dependency chain
        if (!ProviderCacheCoordinator.IsCachedValueValid(Node.Id, Graph))
        {
            return false;
        }

        // Get the cached value (we know it exists from IsCachedValueValid)
        if (!Graph.ValueCache.TryGet(Node.Id, out var cached) || cached is not IEnumerable<T> typedValue)
        {
            return false;
        }

        values = typedValue;
        return true;
    }

    /// <summary>
    /// Gets the element equality comparer for this node's collection.
    /// </summary>
    private IEqualityComparer<T> GetElementComparer()
    {
        if (Node is IHasComparer<T> withComparer)
        {
            return withComparer.Comparer;
        }
        return EqualityComparer<T>.Default;
    }

    /// <summary>
    /// Compares two sequences for equality using the specified comparer.
    /// </summary>
    private static bool SequenceEqual(T[] a, T[] b, IEqualityComparer<T> comparer)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (!comparer.Equals(a[i], b[i]))
                return false;
        }

        return true;
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
}
