using System.Collections.Concurrent;
using System.Collections.Immutable;
using Guacamole.Caching;
using Guacamole.Diagnostics;

namespace Guacamole.Pipeline;

/// <summary>
/// Manages the computation graph, dependency tracking, and cache coordination across all providers.
/// Immutable with copy-on-write semantics, enforces DAG structure, provides centralized cache management with LRU eviction.
/// </summary>
internal sealed class ComputationGraph
{
    /// <summary>
    /// All computation nodes in the graph.
    /// </summary>
    public ImmutableDictionary<string, IComputationNode> Nodes { get; }

    /// <summary>
    /// Dependency relationships between nodes.
    /// </summary>
    public ImmutableDictionary<string, ImmutableHashSet<string>> Dependencies { get; }

    /// <summary>
    /// Shared cache for computed values with LRU eviction.
    /// </summary>
    internal LRUCache<string, object> ValueCache { get; }

    /// <summary>
    /// Shared state for runtime-discovered nodes and dependencies.
    /// This state is shared across all versions of the graph to ensure
    /// cache invalidation works correctly even as the graph evolves.
    /// </summary>
    private readonly SharedGraphState _sharedState;

    /// <summary>
    /// Gets the version tracker for equality-based cache skipping.
    /// Tracks output versions for each node - versions only increment when
    /// the computed value actually changes.
    /// </summary>
    public VersionTracker Versions => _sharedState.Versions;

    /// <summary>
    /// Gets the shared state (internal access for IncrementalValueProvider).
    /// </summary>
    internal SharedGraphState SharedState => _sharedState;

    /// <summary>
    /// Creates an empty computation graph with the default value cache capacity.
    /// </summary>
    public ComputationGraph()
        : this(ComputingContextBase.DefaultCacheCapacity)
    {
    }

    /// <summary>
    /// Creates an empty computation graph whose value cache holds at most
    /// <paramref name="cacheCapacity"/> values.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cacheCapacity"/> is less than 1.</exception>
    public ComputationGraph(int cacheCapacity)
        : this(
            ImmutableDictionary<string, IComputationNode>.Empty,
            ImmutableDictionary<string, ImmutableHashSet<string>>.Empty,
            new LRUCache<string, object>(cacheCapacity),
            new SharedGraphState())
    {
    }

    /// <summary>
    /// Creates a computation graph with specified components.
    /// </summary>
    private ComputationGraph(
        ImmutableDictionary<string, IComputationNode> nodes,
        ImmutableDictionary<string, ImmutableHashSet<string>> dependencies,
        LRUCache<string, object> valueCache,
        SharedGraphState sharedState)
    {
        Nodes = nodes;
        Dependencies = dependencies;
        ValueCache = valueCache;
        _sharedState = sharedState ?? throw new ArgumentNullException(nameof(sharedState));
    }

    /// <summary>
    /// Adds a node to the graph with validation.
    /// </summary>
    public ComputationGraph AddNode(IComputationNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        using var activity = GuacamoleMetrics.StartActivity("graph.add_node",
            ("node.id", node.Id),
            ("node.type", node.GetType().Name));

        // Validate NodeId uniqueness
        if (Nodes.ContainsKey(node.Id))
        {
            throw new InvalidOperationException($"Node with ID '{node.Id}' already exists in the graph");
        }

        // Check for circular dependencies
        var newDependencies = Dependencies.SetItem(node.Id, node.Dependencies);
        if (WouldCreateCycle(newDependencies, node.Id))
        {
            throw new InvalidOperationException($"Adding node '{node.Id}' would create a circular dependency");
        }

        var newNodes = Nodes.SetItem(node.Id, node);
        var newGraph = new ComputationGraph(newNodes, newDependencies, ValueCache, _sharedState);

        // Record metrics if enabled
        if (MetricsConfiguration.Current.EnableGraphMetrics)
        {
            GuacamoleMetrics.GraphOperationsTotal.Add(1, new KeyValuePair<string, object?>("operation.type", "add_node"));
            GuacamoleMetrics.UpdateGraphMetrics(newNodes.Count, newDependencies.Values.Sum(deps => deps.Count));
        }

        return newGraph;
    }

    /// <summary>
    /// Registers a node for runtime tracking if not already registered.
    /// This is used for dynamically discovered nodes (e.g., from extension methods).
    /// Thread-safe and idempotent.
    /// </summary>
    /// <param name="node">The node to register</param>
    public void RegisterNodeIfNeeded(IComputationNode node)
    {
        _sharedState.RegisterNodeIfNeeded(node, Nodes);
    }

    /// <summary>
    /// Returns the node created by an earlier, identical operator call (same
    /// <paramref name="key"/>), or creates and registers a new one. This makes repeated
    /// pipeline construction (e.g. in property getters) share one node and its cache.
    /// </summary>
    /// <typeparam name="TNode">The node type created by <paramref name="factory"/></typeparam>
    /// <param name="key">Structural identity of the operator call</param>
    /// <param name="factory">Creates the node when no identical call was made before</param>
    /// <returns>The shared node for <paramref name="key"/></returns>
    internal TNode GetOrCreateNode<TNode>(NodeReuseKey key, Func<TNode> factory)
        where TNode : class, IComputationNode
    {
        var node = (TNode)_sharedState.GetOrCreateReusableNode(key, factory);
        RegisterNodeIfNeeded(node);
        return node;
    }

    /// <summary>
    /// Extension method for caching values (used by tests).
    /// </summary>
    internal ComputationGraph WithCachedValue(string nodeId, object value)
    {
        ValueCache.Set(nodeId, value);
        return this; // Simplified for tests
    }

    /// <summary>
    /// Extension method for replacing nodes (used by tests).
    /// </summary>
    internal ComputationGraph ReplaceNode(IComputationNode node)
    {
        var newNodes = Nodes.SetItem(node.Id, node);

        // Invalidate cache for the updated node and dependents
        InvalidateCacheForNode(node.Id);

        return new ComputationGraph(newNodes, Dependencies, ValueCache, _sharedState);
    }

    /// <summary>
    /// Checks for circular dependencies.
    /// </summary>
    private bool WouldCreateCycle(
        ImmutableDictionary<string, ImmutableHashSet<string>> dependencies,
        string nodeId)
    {
        return HasCycleInDependencies(dependencies, nodeId, new HashSet<string>(), new HashSet<string>());
    }

    private bool HasCycleInDependencies(
        ImmutableDictionary<string, ImmutableHashSet<string>> dependencies,
        string nodeId,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visiting.Contains(nodeId))
            return true;
        if (visited.Contains(nodeId))
            return false;

        visiting.Add(nodeId);

        if (dependencies.TryGetValue(nodeId, out var deps))
        {
            foreach (var dep in deps)
            {
                if (HasCycleInDependencies(dependencies, dep, visiting, visited))
                    return true;
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
        return false;
    }

    /// <summary>
    /// Gets the count of runtime-discovered nodes.
    /// </summary>
    public int GetRuntimeNodeCount() => _sharedState.RuntimeNodes.Count;

    /// <summary>
    /// Gets all runtime node IDs (for debugging).
    /// </summary>
    public IEnumerable<string> GetRuntimeNodeIds() => _sharedState.RuntimeNodes.Keys;

    /// <summary>
    /// Tries to get a runtime node by ID.
    /// </summary>
    public bool TryGetRuntimeNode(string nodeId, out IComputationNode? node)
    {
        return _sharedState.RuntimeNodes.TryGetValue(nodeId, out node);
    }

    /// <summary>
    /// Marks a source node as changed by incrementing its version.
    /// Does NOT clear any caches and does NOT eagerly bump versions of dependents;
    /// lazy validation will handle this by recursively checking dependency version matches.
    /// This is the preferred method for source change notification when using
    /// version-based equality caching.
    /// </summary>
    /// <param name="nodeId">The source node's unique identifier</param>
    public void MarkSourceChanged(string nodeId)
    {
        // Increment the source node's version. Downstream nodes will observe this
        // via their recursive dependency version checks and recompute as needed.
        // Their own versions should only be incremented if their computed values
        // actually change, enabling equality-based cache skipping.
        _sharedState.Versions.IncrementOutputVersion(nodeId);

        // Advance the revision after the version, so that a value verified at the new revision
        // has seen the new version.
        _sharedState.AdvanceRevision();
    }

    /// <summary>
    /// Invalidates cache for a node and all its transitive dependents.
    /// Handles both static and runtime dependencies correctly.
    /// Public to allow ComputingContextBase to properly invalidate caches.
    /// </summary>
    /// <remarks>
    /// NOTE: With version-based caching, prefer using <see cref="MarkSourceChanged"/>
    /// instead of this method. This method is kept for backward compatibility
    /// and for scenarios where forced cache clearing is needed.
    /// </remarks>
    public void InvalidateCacheForNode(string nodeId)
    {
        // Remove the node itself from cache
        ValueCache.Remove(nodeId);

        // Find and invalidate all dependent nodes recursively
        var dependentsToInvalidate = FindAllDependents(nodeId);
        foreach (var dependent in dependentsToInvalidate)
        {
            ValueCache.Remove(dependent);
        }
    }

    /// <summary>
    /// Finds all nodes that depend on the given node (transitively).
    /// Searches both static and runtime dependency collections.
    /// </summary>
    private HashSet<string> FindAllDependents(string nodeId)
    {
        var dependents = new HashSet<string>();
        var toCheck = new Queue<string>();
        toCheck.Enqueue(nodeId);

        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();

            // Find all nodes that directly depend on current in static dependencies
            foreach (var kvp in Dependencies)
            {
                if (kvp.Value.Contains(current) && !dependents.Contains(kvp.Key))
                {
                    dependents.Add(kvp.Key);
                    toCheck.Enqueue(kvp.Key);
                }
            }

            // Also search runtime dependencies (shared across graph versions)
            foreach (var kvp in _sharedState.RuntimeDependencies)
            {
                if (kvp.Value.Contains(current) && !dependents.Contains(kvp.Key))
                {
                    dependents.Add(kvp.Key);
                    toCheck.Enqueue(kvp.Key);
                }
            }
        }

        return dependents;
    }
}
