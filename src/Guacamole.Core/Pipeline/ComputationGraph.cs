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
    /// Creates an empty computation graph.
    /// </summary>
    public ComputationGraph()
        : this(
            ImmutableDictionary<string, IComputationNode>.Empty,
            ImmutableDictionary<string, ImmutableHashSet<string>>.Empty,
            new LRUCache<string, object>(10000))
    {
    }

    /// <summary>
    /// Creates a computation graph with specified components.
    /// </summary>
    private ComputationGraph(
        ImmutableDictionary<string, IComputationNode> nodes,
        ImmutableDictionary<string, ImmutableHashSet<string>> dependencies,
        LRUCache<string, object> valueCache)
    {
        Nodes = nodes;
        Dependencies = dependencies;
        ValueCache = valueCache;
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
        var newGraph = new ComputationGraph(newNodes, newDependencies, ValueCache);

        // Record metrics if enabled
        if (MetricsConfiguration.Current.EnableGraphMetrics)
        {
            GuacamoleMetrics.GraphOperationsTotal.Add(1, new KeyValuePair<string, object?>("operation.type", "add_node"));
            GuacamoleMetrics.UpdateGraphMetrics(newNodes.Count, newDependencies.Values.Sum(deps => deps.Count));
        }

        return newGraph;
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

        return new ComputationGraph(newNodes, Dependencies, ValueCache);
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

    private void InvalidateCacheForNode(string nodeId)
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
    /// </summary>
    private HashSet<string> FindAllDependents(string nodeId)
    {
        var dependents = new HashSet<string>();
        var toCheck = new Queue<string>();
        toCheck.Enqueue(nodeId);

        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();

            // Find all nodes that directly depend on current
            foreach (var kvp in Dependencies)
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