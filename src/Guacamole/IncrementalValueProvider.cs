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
            bool valueChanged;
            if (!hadPrevious)
            {
                // No previous value cached; treat as a change
                valueChanged = true;
            }
            else if (previousValue is null)
            {
                // Previously null, now non-null (result.Value != null at this point)
                valueChanged = true;
            }
            else
            {
                // Both previous and current values are non-null; use comparer
                valueChanged = !comparer.Equals(previousValue, result.Value);
            }

            // Only increment version if value changed
            long version = valueChanged
                ? Graph.Versions.IncrementOutputVersion(Node.Id)
                : Graph.Versions.GetOutputVersion(Node.Id);

            // Store version info for future validation
            var versionInfo = new NodeVersionInfo(version, depVersions);
            Graph.SharedState.VersionInfo[Node.Id] = versionInfo;

            // Store computed value in cache
            Graph.ValueCache.Set(Node.Id, result.Value!);
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
        if (!IsCachedValueValid(Node.Id, Graph))
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
    /// Uses recursive validation to check the entire dependency chain up to sources.
    /// This enables equality-based cache skipping: if an intermediate node's value
    /// hasn't changed, downstream nodes don't need to recompute.
    /// </summary>
    private static bool IsCachedValueValid(string nodeId, ComputationGraph graph)
    {
        return IsCachedValueValidRecursive(nodeId, graph, new HashSet<string>());
    }

    /// <summary>
    /// Recursive implementation of cache validation with cycle detection.
    /// Validates that the dependency chain is valid up to source nodes.
    /// </summary>
    private static bool IsCachedValueValidRecursive(
        string nodeId,
        ComputationGraph graph,
        HashSet<string> visited)
    {
        // Cycle detection (shouldn't happen in DAG but defensive)
        if (!visited.Add(nodeId))
            return true; // Already validated in this chain

        // Check if we have version info (node was computed through provider layer)
        if (graph.SharedState.VersionInfo.TryGetValue(nodeId, out var versionInfo))
        {
            // Must have a cached value if we have version info
            if (!graph.ValueCache.TryGet(nodeId, out _))
                return false;

            // For source nodes (no dependencies), check if own version has changed
            if (versionInfo.DependencyVersionsAtComputation.IsEmpty)
            {
                var currentOwnVersion = graph.Versions.GetOutputVersion(nodeId);
                return currentOwnVersion == versionInfo.OutputVersion;
            }

            // For derived nodes: check each dependency
            foreach (var kvp in versionInfo.DependencyVersionsAtComputation)
            {
                var depId = kvp.Key;
                var storedVersion = kvp.Value;

                // First, check if dependency version matches what we stored
                var currentVersion = graph.Versions.GetOutputVersion(depId);
                if (currentVersion != storedVersion)
                {
                    // Dependency's output version changed - our cache is stale
                    return false;
                }

                // Second, recursively check if the dependency chain is valid
                if (!IsCachedValueValidRecursive(depId, graph, visited))
                {
                    return false;
                }
            }

            return true;
        }

        // No version info - this node was computed outside the provider layer
        // (e.g., inline by a parent node's Compute method).
        // These nodes may or may not have cached values.
        // Recursively check dependencies to validate the chain.
        var node = GetNodeFromGraph(nodeId, graph);
        if (node == null)
            return false;

        if (node.Dependencies.IsEmpty)
        {
            // Source node without version info: valid if version is 0 (pristine)
            var currentVersion = graph.Versions.GetOutputVersion(nodeId);
            return currentVersion == 0;
        }

        // Intermediate node without version info: recursively check dependencies.
        // If all dependencies are valid, this node would compute the same value.
        foreach (var depId in node.Dependencies)
        {
            if (!IsCachedValueValidRecursive(depId, graph, visited))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Gets a node from the graph (either static or runtime nodes).
    /// </summary>
    private static IComputationNode? GetNodeFromGraph(string nodeId, ComputationGraph graph)
    {
        if (graph.Nodes.TryGetValue(nodeId, out var node))
            return node;
        if (graph.TryGetRuntimeNode(nodeId, out var runtimeNode))
            return runtimeNode;
        return null;
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
            long version = valueChanged
                ? Graph.Versions.IncrementOutputVersion(Node.Id)
                : Graph.Versions.GetOutputVersion(Node.Id);

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
    /// Tries to get valid cached values by checking if the entire dependency chain
    /// is still valid up to source nodes. Uses recursive validation to enable
    /// equality-based cache skipping.
    /// </summary>
    private bool TryGetValidCachedValues(out IEnumerable<T> values)
    {
        values = Enumerable.Empty<T>();

        // Use recursive validation to check the entire dependency chain
        if (!IsCachedValueValid(Node.Id, Graph))
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
    /// Uses recursive validation to check the entire dependency chain up to sources.
    /// This enables equality-based cache skipping: if an intermediate node's value
    /// hasn't changed, downstream nodes don't need to recompute.
    /// </summary>
    private static bool IsCachedValueValid(string nodeId, ComputationGraph graph)
    {
        return IsCachedValueValidRecursive(nodeId, graph, new HashSet<string>());
    }

    /// <summary>
    /// Recursive implementation of cache validation with cycle detection.
    /// </summary>
    private static bool IsCachedValueValidRecursive(
        string nodeId,
        ComputationGraph graph,
        HashSet<string> visited)
    {
        // Cycle detection (shouldn't happen in DAG but defensive)
        if (!visited.Add(nodeId))
            return true; // Already validated in this chain

        // Check if we have version info (node was computed through provider layer)
        if (graph.SharedState.VersionInfo.TryGetValue(nodeId, out var versionInfo))
        {
            // Must have a cached value if we have version info
            if (!graph.ValueCache.TryGet(nodeId, out _))
                return false;

            // For source nodes (no dependencies), check if own version has changed
            if (versionInfo.DependencyVersionsAtComputation.IsEmpty)
            {
                var currentOwnVersion = graph.Versions.GetOutputVersion(nodeId);
                return currentOwnVersion == versionInfo.OutputVersion;
            }

            // For derived nodes: check each dependency
            foreach (var kvp in versionInfo.DependencyVersionsAtComputation)
            {
                var depId = kvp.Key;
                var storedVersion = kvp.Value;

                // First, check if dependency version matches what we stored
                var currentVersion = graph.Versions.GetOutputVersion(depId);
                if (currentVersion != storedVersion)
                {
                    // Dependency's output version changed - our cache is stale
                    return false;
                }

                // Second, recursively check if the dependency chain is valid
                if (!IsCachedValueValidRecursive(depId, graph, visited))
                {
                    return false;
                }
            }

            return true;
        }

        // No version info - this node was computed outside the provider layer
        // (e.g., inline by a parent node's Compute method).
        // These nodes may or may not have cached values.
        // Recursively check dependencies to validate the chain.
        var node = GetNodeFromGraph(nodeId, graph);
        if (node == null)
            return false;

        if (node.Dependencies.IsEmpty)
        {
            // Source node without version info: valid if version is 0 (pristine)
            var currentVersion = graph.Versions.GetOutputVersion(nodeId);
            return currentVersion == 0;
        }

        // Intermediate node without version info: recursively check dependencies.
        // If all dependencies are valid, this node would compute the same value.
        foreach (var depId in node.Dependencies)
        {
            if (!IsCachedValueValidRecursive(depId, graph, visited))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Gets a node from the graph (either static or runtime nodes).
    /// </summary>
    private static IComputationNode? GetNodeFromGraph(string nodeId, ComputationGraph graph)
    {
        if (graph.Nodes.TryGetValue(nodeId, out var node))
            return node;
        if (graph.TryGetRuntimeNode(nodeId, out var runtimeNode))
            return runtimeNode;
        return null;
    }
}
