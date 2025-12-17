using System.Collections.Concurrent;
using System.Collections.Immutable;
using Guacamole.Core.Caching;
using Guacamole.Core.Diagnostics;

namespace Guacamole.Core.Pipeline;

/// <summary>
/// Manages the computation graph, dependency tracking, and cache coordination across all providers.
/// Immutable with copy-on-write semantics, enforces DAG structure, provides centralized cache management with LRU eviction.
/// </summary>
public class ComputationGraph
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
    public LRUCache<string, object> ValueCache { get; }

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
    /// Creates an empty computation graph.
    /// </summary>
    public ComputationGraph()
        : this(
            ImmutableDictionary<string, IComputationNode>.Empty,
            ImmutableDictionary<string, ImmutableHashSet<string>>.Empty,
            new LRUCache<string, object>(10000),
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
    /// Checks if the graph maintains DAG invariant.
    /// </summary>
    public bool IsValidDAG()
    {
        foreach (var nodeId in Nodes.Keys)
        {
            if (HasCycle(nodeId, new HashSet<string>(), new HashSet<string>()))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Validates that all node dependencies exist in the graph.
    /// </summary>
    /// <returns>Collection of missing dependency errors</returns>
    public IEnumerable<string> ValidateDependencies()
    {
        var missingDependencies = new List<string>();

        foreach (var node in Nodes.Values)
        {
            foreach (var dep in node.Dependencies)
            {
                if (!Nodes.ContainsKey(dep))
                {
                    missingDependencies.Add($"Node '{node.Id}' depends on missing node '{dep}'");
                }
            }
        }

        return missingDependencies;
    }

    /// <summary>
    /// Extension method for caching values (used by tests).
    /// </summary>
    public ComputationGraph WithCachedValue(string nodeId, object value)
    {
        ValueCache.Set(nodeId, value);
        return this; // Simplified for tests
    }

    /// <summary>
    /// Extension method for replacing nodes (used by tests).
    /// </summary>
    public ComputationGraph ReplaceNode(IComputationNode node)
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

    private bool HasCycle(string nodeId, HashSet<string> visiting, HashSet<string> visited)
    {
        if (visiting.Contains(nodeId))
            return true;
        if (visited.Contains(nodeId))
            return false;

        visiting.Add(nodeId);

        if (Dependencies.TryGetValue(nodeId, out var deps))
        {
            foreach (var dep in deps)
            {
                if (HasCycle(dep, visiting, visited))
                    return true;
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
        return false;
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
    /// Checks if a node is registered (either static or runtime).
    /// </summary>
    public bool IsNodeRegistered(string nodeId) => Nodes.ContainsKey(nodeId) || _sharedState.RuntimeNodes.ContainsKey(nodeId);

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
