using System.Collections.Generic;
using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;

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

        // Get previous value before recomputing (for equality comparison)
        T? previousValue = default;
        bool hadPrevious = Graph.ValueCache.TryGet(Node.Id, out var prev) && prev is T;
        if (hadPrevious)
        {
            previousValue = (T)prev!;
        }

        // Create context with current cache state and compute
        var cachedValues = BuildCacheContext(Node, Graph);
        var context = new ComputationContext(cachedValues);
        var result = Node.Compute(context);

        // Store result with version tracking
        if (result.IsSuccess && result.Value != null)
        {
            // Snapshot current dependency versions
            var depVersions = Graph.Versions.SnapshotDependencyVersions(Node.Dependencies);

            // Check if value actually changed using appropriate comparer
            var comparer = GetNodeComparer();
            bool valueChanged = !hadPrevious ||
                (previousValue == null && result.Value != null) ||
                (previousValue != null && result.Value == null) ||
                (previousValue != null && !comparer.Equals(previousValue, result.Value));

            // Only increment version if value changed
            long version;
            if (valueChanged)
            {
                version = Graph.Versions.IncrementOutputVersion(Node.Id);
            }
            else
            {
                version = Graph.Versions.GetOutputVersion(Node.Id);
            }

            // Store version info for future validation
            var versionInfo = new NodeVersionInfo(version, depVersions);
            Graph.SharedState.VersionInfo[Node.Id] = versionInfo;

            // Store computed value in cache
            Graph.ValueCache.Set(Node.Id, result.Value!);
        }

        return result;
    }

    /// <summary>
    /// Tries to get a valid cached value by checking if all dependency versions
    /// are still current.
    /// </summary>
    /// <param name="value">The cached value if valid</param>
    /// <returns>True if cached value is valid and can be used</returns>
    private bool TryGetValidCachedValue(out T value)
    {
        value = default!;

        // Check if we have a cached value
        if (!Graph.ValueCache.TryGet(Node.Id, out var cached) || cached is not T typedValue)
        {
            return false;
        }

        // Check if we have version info for this node
        if (!Graph.SharedState.VersionInfo.TryGetValue(Node.Id, out var versionInfo))
        {
            return false;
        }

        // For source nodes (no dependencies), check if own version has changed
        // This handles the case where MarkSourceChanged incremented the version
        if (versionInfo.DependencyVersionsAtComputation.IsEmpty)
        {
            var currentOwnVersion = Graph.Versions.GetOutputVersion(Node.Id);
            if (currentOwnVersion != versionInfo.OutputVersion)
            {
                return false; // Source has been modified since cached
            }
        }

        // Validate all dependency versions are still current
        // Note: With version propagation in MarkSourceChanged, intermediate node
        // versions are incremented when their transitive dependencies change
        foreach (var kvp in versionInfo.DependencyVersionsAtComputation)
        {
            var currentVersion = Graph.Versions.GetOutputVersion(kvp.Key);
            if (currentVersion != kvp.Value)
            {
                // A dependency's version has changed - cache is stale
                return false;
            }
        }

        // All dependency versions match - cache is valid
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

    /// <summary>
    /// Builds a dictionary of cached values relevant to this computation.
    /// Includes the node itself and all transitive dependencies.
    /// Only includes version-valid cached values to prevent stale data propagation.
    /// </summary>
    private static IReadOnlyDictionary<string, object> BuildCacheContext(
        IComputationNode node,
        ComputationGraph graph)
    {
        var dict = new Dictionary<string, object>();

        // Check if the node itself is cached and version-valid
        if (IsCachedValueValid(node.Id, graph) &&
            graph.ValueCache.TryGet(node.Id, out var nodeValue) && nodeValue != null)
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

            // Try to get cached value for this dependency (only if version-valid)
            if (IsCachedValueValid(depId, graph) &&
                graph.ValueCache.TryGet(depId, out var depValue) && depValue != null)
            {
                dict[depId] = depValue;
            }

            // Add transitive dependencies - check both static and runtime nodes
            IComputationNode? depNode = null;
            if (!graph.Nodes.TryGetValue(depId, out depNode))
            {
                // Not in static nodes, check runtime nodes
                graph.TryGetRuntimeNode(depId, out depNode);
            }

            if (depNode != null)
            {
                // Register this node for cache invalidation tracking
                // This ensures all nodes in the dependency chain are tracked
                graph.RegisterNodeIfNeeded(depNode);

                foreach (var transDep in depNode.Dependencies)
                {
                    toCheck.Enqueue(transDep);
                }
            }
        }

        return dict;
    }

    /// <summary>
    /// Checks if a cached value for a node is still version-valid.
    /// With version propagation in MarkSourceChanged, we only need to check
    /// direct dependency versions since intermediate node versions are
    /// automatically incremented when their transitive dependencies change.
    /// </summary>
    private static bool IsCachedValueValid(string nodeId, ComputationGraph graph)
    {
        // Must have a cached value
        if (!graph.ValueCache.TryGet(nodeId, out _))
            return false;

        // Must have version info
        if (!graph.SharedState.VersionInfo.TryGetValue(nodeId, out var versionInfo))
            return false;

        // For source nodes (no dependencies), check if own version has changed
        if (versionInfo.DependencyVersionsAtComputation.IsEmpty)
        {
            var currentOwnVersion = graph.Versions.GetOutputVersion(nodeId);
            return currentOwnVersion == versionInfo.OutputVersion;
        }

        // Validate all dependency versions are still current
        foreach (var kvp in versionInfo.DependencyVersionsAtComputation)
        {
            var currentVersion = graph.Versions.GetOutputVersion(kvp.Key);
            if (currentVersion != kvp.Value)
            {
                return false;
            }
        }

        return true;
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

        // Get previous value before recomputing (for equality comparison)
        T[]? previousArray = null;
        bool hadPrevious = Graph.ValueCache.TryGet(Node.Id, out var prev) && prev is IEnumerable<T>;
        if (hadPrevious && prev is IEnumerable<T> prevEnumerable)
        {
            previousArray = prevEnumerable as T[] ?? prevEnumerable.ToArray();
        }

        // Create context with current cache state and compute
        var cachedValues = BuildCacheContext(Node, Graph);
        var context = new ComputationContext(cachedValues);
        var result = Node.Compute(context);

        // Store result with version tracking
        if (result.IsSuccess && result.Value != null)
        {
            // Materialize to array for comparison and storage
            var currentArray = result.Value as T[] ?? result.Value.ToArray();

            // Snapshot current dependency versions
            var depVersions = Graph.Versions.SnapshotDependencyVersions(Node.Dependencies);

            // Check if collection actually changed using sequence equality
            var comparer = GetElementComparer();
            bool valueChanged = !hadPrevious ||
                previousArray == null ||
                !SequenceEqual(previousArray, currentArray, comparer);

            // Only increment version if value changed
            long version;
            if (valueChanged)
            {
                version = Graph.Versions.IncrementOutputVersion(Node.Id);
            }
            else
            {
                version = Graph.Versions.GetOutputVersion(Node.Id);
            }

            // Store version info for future validation
            var versionInfo = new NodeVersionInfo(version, depVersions);
            Graph.SharedState.VersionInfo[Node.Id] = versionInfo;

            // Store computed value in cache (as array for later comparison)
            Graph.ValueCache.Set(Node.Id, currentArray);

            // Return the array we already materialized
            return Result<IEnumerable<T>, ComputationError>.Success(currentArray);
        }

        return result;
    }

    /// <summary>
    /// Tries to get valid cached values by checking if all dependency versions
    /// are still current.
    /// </summary>
    private bool TryGetValidCachedValues(out IEnumerable<T> values)
    {
        values = Enumerable.Empty<T>();

        // Check if we have a cached value
        if (!Graph.ValueCache.TryGet(Node.Id, out var cached) || cached is not IEnumerable<T> typedValue)
        {
            return false;
        }

        // Check if we have version info for this node
        if (!Graph.SharedState.VersionInfo.TryGetValue(Node.Id, out var versionInfo))
        {
            return false;
        }

        // For source nodes (no dependencies), check if own version has changed
        // This handles the case where MarkSourceChanged incremented the version
        if (versionInfo.DependencyVersionsAtComputation.IsEmpty)
        {
            var currentOwnVersion = Graph.Versions.GetOutputVersion(Node.Id);
            if (currentOwnVersion != versionInfo.OutputVersion)
            {
                return false; // Source has been modified since cached
            }
        }

        // Validate all dependency versions are still current
        // Note: With version propagation in MarkSourceChanged, intermediate node
        // versions are incremented when their transitive dependencies change
        foreach (var kvp in versionInfo.DependencyVersionsAtComputation)
        {
            var currentVersion = Graph.Versions.GetOutputVersion(kvp.Key);
            if (currentVersion != kvp.Value)
            {
                // A dependency's version has changed - cache is stale
                return false;
            }
        }

        // All dependency versions match - cache is valid
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

    /// <summary>
    /// Builds a dictionary of cached values relevant to this computation.
    /// Includes the node itself and all transitive dependencies.
    /// Only includes version-valid cached values to prevent stale data propagation.
    /// </summary>
    private static IReadOnlyDictionary<string, object> BuildCacheContext(
        IComputationNode node,
        ComputationGraph graph)
    {
        var dict = new Dictionary<string, object>();

        // Check if the node itself is cached and version-valid
        if (IsCachedValueValid(node.Id, graph) &&
            graph.ValueCache.TryGet(node.Id, out var nodeValue) && nodeValue != null)
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

            // Try to get cached value for this dependency (only if version-valid)
            if (IsCachedValueValid(depId, graph) &&
                graph.ValueCache.TryGet(depId, out var depValue) && depValue != null)
            {
                dict[depId] = depValue;
            }

            // Add transitive dependencies - check both static and runtime nodes
            IComputationNode? depNode = null;
            if (!graph.Nodes.TryGetValue(depId, out depNode))
            {
                // Not in static nodes, check runtime nodes
                graph.TryGetRuntimeNode(depId, out depNode);
            }

            if (depNode != null)
            {
                // Register this node for cache invalidation tracking
                // This ensures all nodes in the dependency chain are tracked
                graph.RegisterNodeIfNeeded(depNode);

                foreach (var transDep in depNode.Dependencies)
                {
                    toCheck.Enqueue(transDep);
                }
            }
        }

        return dict;
    }

    /// <summary>
    /// Checks if a cached value for a node is still version-valid.
    /// With version propagation in MarkSourceChanged, we only need to check
    /// direct dependency versions since intermediate node versions are
    /// automatically incremented when their transitive dependencies change.
    /// </summary>
    private static bool IsCachedValueValid(string nodeId, ComputationGraph graph)
    {
        // Must have a cached value
        if (!graph.ValueCache.TryGet(nodeId, out _))
            return false;

        // Must have version info
        if (!graph.SharedState.VersionInfo.TryGetValue(nodeId, out var versionInfo))
            return false;

        // For source nodes (no dependencies), check if own version has changed
        if (versionInfo.DependencyVersionsAtComputation.IsEmpty)
        {
            var currentOwnVersion = graph.Versions.GetOutputVersion(nodeId);
            return currentOwnVersion == versionInfo.OutputVersion;
        }

        // Validate all dependency versions are still current
        foreach (var kvp in versionInfo.DependencyVersionsAtComputation)
        {
            var currentVersion = graph.Versions.GetOutputVersion(kvp.Key);
            if (currentVersion != kvp.Value)
            {
                return false;
            }
        }

        return true;
    }
}
