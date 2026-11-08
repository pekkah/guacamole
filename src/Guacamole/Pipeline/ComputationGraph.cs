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
    /// The reverse of <see cref="Dependencies"/>: for each node ID, the IDs of the nodes added to
    /// this graph that declare it as a dependency (whether or not it has been added itself).
    /// </summary>
    public ImmutableDictionary<string, ImmutableHashSet<string>> Dependents { get; }

    /// <summary>
    /// The number of dependency edges in <see cref="Dependencies"/>.
    /// </summary>
    public int DependencyCount { get; }

    /// <summary>
    /// Shared cache for computed values with LRU eviction.
    /// </summary>
    internal LRUCache<string, object> ValueCache { get; }

    /// <summary>
    /// Shared state for runtime-discovered nodes, version records and evaluation handles.
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
        : this(cacheCapacity, autoTrimInterval: 0)
    {
    }

    /// <summary>
    /// Creates an empty computation graph whose value cache holds at most
    /// <paramref name="cacheCapacity"/> values, and that trims unused nodes automatically after
    /// every <paramref name="autoTrimInterval"/> newly registered runtime nodes (0 disables
    /// automatic trimming). See <see cref="SharedGraphState.EnableAutoTrim"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cacheCapacity"/> is less than 1,
    /// or <paramref name="autoTrimInterval"/> is negative.</exception>
    public ComputationGraph(int cacheCapacity, int autoTrimInterval)
        : this(
            ImmutableDictionary<string, IComputationNode>.Empty,
            ImmutableDictionary<string, ImmutableHashSet<string>>.Empty,
            ImmutableDictionary<string, ImmutableHashSet<string>>.Empty,
            0,
            new LRUCache<string, object>(cacheCapacity),
            new SharedGraphState())
    {
        if (autoTrimInterval != 0)
            _sharedState.EnableAutoTrim(autoTrimInterval, ValueCache);
    }

    /// <summary>
    /// Creates a computation graph with specified components.
    /// </summary>
    private ComputationGraph(
        ImmutableDictionary<string, IComputationNode> nodes,
        ImmutableDictionary<string, ImmutableHashSet<string>> dependencies,
        ImmutableDictionary<string, ImmutableHashSet<string>> dependents,
        int dependencyCount,
        LRUCache<string, object> valueCache,
        SharedGraphState sharedState)
    {
        Nodes = nodes;
        Dependencies = dependencies;
        Dependents = dependents;
        DependencyCount = dependencyCount;
        ValueCache = valueCache;
        _sharedState = sharedState ?? throw new ArgumentNullException(nameof(sharedState));
    }

    /// <summary>
    /// Adds a node to the graph with validation.
    /// </summary>
    /// <remarks>
    /// Costs O(log n) for a node whose ID no node in the graph declares as a dependency yet (the
    /// usual case: nodes are added after their dependencies), so building a graph of n nodes this way
    /// is O(n log n); the cycle check used to walk everything upstream of every new node, which made
    /// building a chain quadratic. Otherwise the cycle check walks the nodes upstream of the new one
    /// (see <see cref="WouldCreateCycle"/>). Runtime nodes (<see cref="RegisterNodeIfNeeded"/>) are not
    /// checked: they are created from existing nodes (a node references its inputs), so they cannot close a cycle,
    /// and registering one is O(1).
    /// </remarks>
    /// <exception cref="InvalidOperationException">A node with the same ID exists, or the node would
    /// close a cycle.</exception>
    public ComputationGraph AddNode(IComputationNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        using var activity = GuacamoleMetrics.StartActivity("graph.add_node", ("node.id", node.Id), ("node.type", node.GetType().Name));

        // Validate NodeId uniqueness
        if (Nodes.ContainsKey(node.Id))
        {
            throw new InvalidOperationException($"Node with ID '{node.Id}' already exists in the graph");
        }

        // Check for circular dependencies
        if (WouldCreateCycle(node))
        {
            throw new InvalidOperationException($"Adding node '{node.Id}' would create a circular dependency");
        }

        var newDependencies = Dependencies.SetItem(node.Id, node.Dependencies);
        var newDependents = Dependents;
        foreach (var dependency in node.Dependencies)
        {
            newDependents = newDependents.SetItem(
                dependency,
                newDependents.TryGetValue(dependency, out var existing) ? existing.Add(node.Id) : [node.Id]);
        }

        var newNodes = Nodes.SetItem(node.Id, node);
        var newGraph = new ComputationGraph(newNodes, newDependencies, newDependents, DependencyCount + node.Dependencies.Count, ValueCache, _sharedState);

        // Record metrics if enabled and listened to
        if (MetricsConfiguration.Current.EnableGraphMetrics)
        {
            if (GuacamoleMetrics.GraphOperationsTotal.Enabled)
                GuacamoleMetrics.GraphOperationsTotal.Add(1, new KeyValuePair<string, object?>("operation.type", "add_node"));

            if (GuacamoleMetrics.GraphNodesActive.Enabled || GuacamoleMetrics.GraphDependenciesTotal.Enabled)
                GuacamoleMetrics.UpdateGraphMetrics(newNodes.Count, newGraph.DependencyCount);
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
    /// Replaces the node with <paramref name="node"/>'s ID (or adds it if there is none) and
    /// invalidates the cached values of that node and everything downstream of it (used by tests).
    /// </summary>
    /// <remarks>
    /// <see cref="Dependencies"/>, <see cref="Dependents"/> and <see cref="DependencyCount"/> follow
    /// the new node's dependencies: the replaced node's edges are removed (a dependency that no node
    /// declares any more loses its <see cref="Dependents"/> entry) and the new node's are added.
    /// Nodes that depend on the replaced ID keep depending on it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The new node's dependencies would close a cycle.</exception>
    internal ComputationGraph ReplaceNode(IComputationNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // The nodes that depend on this ID are the same before and after the replacement, so the
        // cycle check of AddNode applies as it is
        if (WouldCreateCycle(node))
        {
            throw new InvalidOperationException($"Replacing node '{node.Id}' would create a circular dependency");
        }

        var oldDependencies = Dependencies.TryGetValue(node.Id, out var replaced) ? replaced : [];
        var newDependents = Dependents;
        foreach (var dependency in oldDependencies.Except(node.Dependencies))
        {
            var remaining = newDependents[dependency].Remove(node.Id);
            newDependents = remaining.IsEmpty ? newDependents.Remove(dependency) : newDependents.SetItem(dependency, remaining);
        }

        foreach (var dependency in node.Dependencies.Except(oldDependencies))
        {
            newDependents = newDependents.SetItem(
                dependency,
                newDependents.TryGetValue(dependency, out var existing) ? existing.Add(node.Id) : [node.Id]);
        }

        var newNodes = Nodes.SetItem(node.Id, node);
        var newDependencies = Dependencies.SetItem(node.Id, node.Dependencies);
        var dependencyCount = DependencyCount - oldDependencies.Count + node.Dependencies.Count;

        // Invalidate cache for the updated node and dependents
        InvalidateCacheForNode(node.Id);

        return new ComputationGraph(newNodes, newDependencies, newDependents, dependencyCount, ValueCache, _sharedState);
    }

    /// <summary>
    /// Whether adding <paramref name="node"/> would close a cycle.
    /// </summary>
    /// <remarks>
    /// The graph is acyclic, so a new cycle has to pass through the new node: one of its dependencies
    /// must (transitively) depend on its ID. That needs a node in the graph that declares the ID as a
    /// dependency (<see cref="Dependents"/>); when there is none, there is no walk at all. That is the
    /// usual case, as nodes are added after their dependencies. Otherwise the nodes upstream of the new
    /// node are walked, iteratively (long chains cannot overflow the stack); when nodes are added
    /// before their dependencies, those are not in the graph yet and the walk ends right away.
    /// </remarks>
    private bool WouldCreateCycle(IComputationNode node)
    {
        var dependencies = node.Dependencies;
        if (dependencies.Contains(node.Id))
            return true;

        if (dependencies.IsEmpty || !Dependents.ContainsKey(node.Id))
            return false;

        var visited = new HashSet<string>();
        var pending = new Stack<string>(dependencies);
        while (pending.TryPop(out var id))
        {
            if (id == node.Id)
                return true;

            if (!visited.Add(id) || !Dependencies.TryGetValue(id, out var upstream))
                continue;

            foreach (var dependency in upstream)
                pending.Push(dependency);
        }

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
        node = null;
        return _sharedState.RuntimeNodes.TryGetValue(nodeId, out var weakNode) && weakNode.TryGetTarget(out node);
    }

    /// <summary>
    /// Removes the registry entries, cached values and version records of runtime nodes that have
    /// been garbage-collected, and releases the node-reuse cache's references to the remaining
    /// nodes. See <see cref="SharedGraphState.TrimUnusedNodes"/>.
    /// </summary>
    /// <returns>The number of nodes removed</returns>
    public int TrimUnusedNodes() => _sharedState.TrimUnusedNodes(ValueCache);

    /// <summary>
    /// Marks a source node as changed by incrementing its version.
    /// Does NOT clear any caches and does NOT bump versions of dependents; it marks the
    /// dependents dirty (see <see cref="NodeState"/>), and lazy validation then re-verifies only
    /// the dirty nodes, recursively checking dependency version matches.
    /// This is the preferred method for source change notification when using
    /// version-based equality caching.
    /// </summary>
    /// <param name="nodeId">The source node's unique identifier</param>
    /// <param name="change">The source's notification, which may hold the change open since before
    /// the source published its new contents (see <see cref="SharedGraphState.MarkSourceChanged"/>)</param>
    public void MarkSourceChanged(string nodeId, SourceChangedEventArgs? change = null)
    {
        // Downstream nodes observe the new version via their dependency version checks and
        // recompute as needed. Their own versions are only incremented if their computed values
        // actually change, enabling equality-based cache skipping.
        _sharedState.MarkSourceChanged(nodeId, change);
    }

    /// <summary>
    /// Invalidates cache for a node and all its transitive dependents.
    /// Handles both static and runtime dependencies correctly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOTE: With version-based caching, prefer using <see cref="MarkSourceChanged"/>
    /// instead of this method. This method is kept for backward compatibility
    /// and for scenarios where forced cache clearing is needed.
    /// </para>
    /// <para>
    /// Follows reverse edges only, so its cost is proportional to the nodes downstream of
    /// <paramref name="nodeId"/>, not to the graph: the edges recorded as nodes read their inputs
    /// (<see cref="NodeState"/>; every node with a cached value has recorded the edges to what it read),
    /// and <see cref="Dependents"/> for nodes added to the graph that have not been computed.
    /// </para>
    /// </remarks>
    public void InvalidateCacheForNode(string nodeId)
    {
        var visited = new HashSet<string> { nodeId };
        var pending = new Stack<string>();
        var dependents = new List<NodeHandle>();
        pending.Push(nodeId);

        while (pending.TryPop(out var id))
        {
            ValueCache.Remove(id);

            if (Versions.TryGetState(id, out var state))
            {
                state.CopyDependentsTo(dependents);
                foreach (var dependent in dependents)
                {
                    if (visited.Add(dependent.State.Id))
                        pending.Push(dependent.State.Id);
                }

                dependents.Clear();
            }

            if (Dependents.TryGetValue(id, out var declared))
            {
                foreach (var dependent in declared)
                {
                    if (visited.Add(dependent))
                        pending.Push(dependent);
                }
            }
        }
    }
}
