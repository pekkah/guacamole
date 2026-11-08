using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Guacamole.Caching;

namespace Guacamole.Pipeline;

/// <summary>
/// Shared mutable state for computation graph that persists across graph versions.
/// This allows runtime-discovered nodes (from extension methods) to be tracked
/// for cache invalidation even when the graph structure changes.
/// </summary>
/// <remarks>
/// <para>
/// None of the per-node state keeps a node alive by itself: <see cref="RuntimeNodes"/> holds nodes
/// weakly, evaluation handles are attached to their nodes with a
/// <see cref="ConditionalWeakTable{TKey, TValue}"/>, and cached values reference the handles of
/// their dependencies weakly. A node is kept alive by the providers, output registrations and
/// downstream nodes that reference it, and (until the next <see cref="TrimUnusedNodes"/>) by the
/// node-reuse cache. <see cref="TrimUnusedNodes"/> removes the registry entries, cached values and
/// version records of nodes that have been collected; with <see cref="EnableAutoTrim"/> it also
/// runs automatically as nodes are added.
/// </para>
/// </remarks>
internal sealed class SharedGraphState
{
    /// <summary>
    /// Runtime-discovered nodes (e.g., from extension methods like Combine, Select), held weakly.
    /// Thread-safe collection for nodes that are discovered during execution.
    /// Shared across all versions of the graph.
    /// </summary>
    public ConcurrentDictionary<string, WeakReference<IComputationNode>> RuntimeNodes { get; } = new();

    /// <summary>
    /// Runtime dependency relationships for dynamically discovered nodes.
    /// Shared across all versions of the graph.
    /// </summary>
    public ConcurrentDictionary<string, ImmutableHashSet<string>> RuntimeDependencies { get; } = new();

    /// <summary>
    /// Version tracker for equality-based cache skipping.
    /// Tracks output versions for each node - versions only increment when
    /// the computed value actually changes (based on equality comparison).
    /// </summary>
    public VersionTracker Versions { get; } = new();

    /// <summary>
    /// Evaluation handle of each node that has been evaluated. A handle lives exactly as long as its
    /// node (the table does not keep the node alive, even though the handle references it).
    /// </summary>
    public ConditionalWeakTable<IComputationNode, NodeHandle> Handles { get; } = new();

    private long _revision;

    /// <summary>
    /// The graph revision: advanced whenever a source changes. A cached value verified at the
    /// current revision is valid without checking its dependencies.
    /// </summary>
    public long Revision => Volatile.Read(ref _revision);

    /// <summary>
    /// Advances <see cref="Revision"/>. Called after a source's version has been incremented.
    /// </summary>
    public void AdvanceRevision() => Interlocked.Increment(ref _revision);

    /// <summary>
    /// Lock that makes comparing a computed value with the cached one, assigning its output
    /// version and storing it a single atomic step.
    /// </summary>
    public object CommitLock { get; } = new();

    /// <summary>
    /// Nodes created by pipeline operators, keyed by the structural identity of the
    /// operator call, so that repeated identical calls reuse one node. Scoped to this
    /// graph state, so nothing is shared across contexts and nothing outlives the graph.
    /// </summary>
    private readonly ConcurrentDictionary<NodeReuseKey, ReusableNode> _reusableNodes = new();

    /// <summary>
    /// Serializes node creation for the reuse cache (so equal keys get a single node) and trimming.
    /// </summary>
    private readonly object _reuseLock = new();

    /// <summary>
    /// Returns the node previously created for <paramref name="key"/>, or creates one with
    /// <paramref name="factory"/>. Thread-safe: concurrent callers with equal keys all
    /// receive the same node instance, and the factory runs at most once per key while the node
    /// is alive. A node that has been released by <see cref="TrimUnusedNodes"/> and collected is
    /// recreated (as a new node, with a new ID and an empty cache).
    /// </summary>
    public IComputationNode GetOrCreateReusableNode(NodeReuseKey key, Func<IComputationNode> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_reusableNodes.TryGetValue(key, out var entry) && entry.TryUse(out var node))
            return node;

        lock (_reuseLock)
        {
            if (_reusableNodes.TryGetValue(key, out entry) && entry.TryUse(out node))
                return node;

            node = factory();
            _reusableNodes[key] = new ReusableNode(node);
            return node;
        }
    }

    /// <summary>
    /// Registers a node for runtime tracking if not already registered.
    /// This is used for dynamically discovered nodes (e.g., from extension methods).
    /// Thread-safe and idempotent.
    /// </summary>
    /// <param name="node">The node to register</param>
    /// <param name="staticNodes">The static nodes dictionary to check against</param>
    public void RegisterNodeIfNeeded(IComputationNode node, ImmutableDictionary<string, IComputationNode> staticNodes)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Check if node is already in static or runtime collections
        if (staticNodes.ContainsKey(node.Id))
        {
            return; // Already registered
        }

        if (RuntimeNodes.TryGetValue(node.Id, out var existing))
        {
            // Already registered, unless the registered node with this ID has been collected
            // (IDs are unique per node, so only nodes created with explicit IDs can get here)
            if (existing.TryGetTarget(out _)
                || !RuntimeNodes.TryUpdate(node.Id, new WeakReference<IComputationNode>(node), existing))
            {
                return;
            }

            RuntimeDependencies[node.Id] = node.Dependencies;
            return;
        }

        // Add to runtime collections
        if (!RuntimeNodes.TryAdd(node.Id, new WeakReference<IComputationNode>(node)))
            return; // registered concurrently

        RuntimeDependencies.TryAdd(node.Id, node.Dependencies);

        if (_autoTrimInterval > 0)
            OnRuntimeNodeAdded();
    }

    // Automatic trimming (see EnableAutoTrim); _autoTrimInterval is 0 when it is off
    private int _autoTrimInterval;
    private LRUCache<string, object>? _autoTrimValueCache;
    private int _nodesAddedSinceTrim;
    private int _autoTrimThreshold;

    /// <summary>
    /// Makes this state trim unused nodes automatically, after every <paramref name="interval"/>
    /// runtime nodes added to <see cref="RuntimeNodes"/>, or after as many nodes as half the size of
    /// the registry after the previous trim if that is more (so that the cost of a trim, which
    /// scans the registry, stays proportional to the number of nodes added since the previous one).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check costs one interlocked increment per added node; nothing is done on reads or for
    /// nodes that are already registered. A trim is never started inside a node computation (it is
    /// started by the next node added outside one instead), and never waits: when another trim or
    /// a node creation holds the lock, the next added node tries again. Because a trim only removes
    /// nodes that have already been collected and releases the others, nodes released by one
    /// automatic trim are removed by a later one, once the garbage collector has collected them.
    /// </para>
    /// <para>
    /// Must be called before the state is used (by the <see cref="ComputationGraph"/> constructor).
    /// </para>
    /// </remarks>
    /// <param name="interval">The minimum number of added nodes between automatic trims (at least 1)</param>
    /// <param name="valueCache">The value cache of the graphs sharing this state</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is less than 1.</exception>
    public void EnableAutoTrim(int interval, LRUCache<string, object> valueCache)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, 1);
        ArgumentNullException.ThrowIfNull(valueCache);

        _autoTrimValueCache = valueCache;
        _autoTrimThreshold = interval;
        _autoTrimInterval = interval;
    }

    private void OnRuntimeNodeAdded()
    {
        if (Interlocked.Increment(ref _nodesAddedSinceTrim) < Volatile.Read(ref _autoTrimThreshold))
            return;

        // Not from inside a node computation (a node may be created while one is running, for
        // example by a lambda building a pipeline): the next node added outside one trims instead.
        if (GraphEvaluator.IsComputing)
            return;

        // Non-blocking: if another trim or a node creation holds the lock (possibly on this thread),
        // skip; the counter stays above the threshold, so the next added node tries again.
        if (Monitor.IsEntered(_reuseLock) || !Monitor.TryEnter(_reuseLock))
            return;

        try
        {
            // Another thread may have trimmed between the increment and the lock
            if (Volatile.Read(ref _nodesAddedSinceTrim) >= _autoTrimThreshold)
            {
                TrimUnusedNodesCore(_autoTrimValueCache!);
                Interlocked.Increment(ref _autoTrimCount);
            }
        }
        finally
        {
            Monitor.Exit(_reuseLock);
        }
    }

    /// <summary>
    /// The number of automatic trims run so far (for diagnostics and tests).
    /// </summary>
    internal int AutoTrimCount => Volatile.Read(ref _autoTrimCount);

    private int _autoTrimCount;

    /// <summary>
    /// Removes the state of collected nodes and releases the reuse cache's references to the others.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>Runtime nodes that have been garbage-collected are removed from <see cref="RuntimeNodes"/>
    /// and <see cref="RuntimeDependencies"/>, together with their cached values and version records.
    /// A collected node can no longer be evaluated, and no live node depends on it (nodes reference
    /// their inputs), so this never affects a live pipeline.</item>
    /// <item>The node-reuse cache forgets collected nodes and stops keeping the others alive: a node
    /// that nothing else references (no provider, output registration or downstream node) can then be
    /// collected, and is removed by the next call. Requesting a node through an identical operator
    /// call keeps it alive again.</item>
    /// </list>
    /// </remarks>
    /// <param name="valueCache">The value cache of the graphs sharing this state</param>
    /// <returns>The number of nodes removed</returns>
    public int TrimUnusedNodes(LRUCache<string, object> valueCache)
    {
        lock (_reuseLock)
        {
            return TrimUnusedNodesCore(valueCache);
        }
    }

    /// <summary>
    /// <see cref="TrimUnusedNodes"/> with <see cref="_reuseLock"/> held.
    /// </summary>
    private int TrimUnusedNodesCore(LRUCache<string, object> valueCache)
    {
        // Nodes added from here on count towards the next automatic trim
        Interlocked.Exchange(ref _nodesAddedSinceTrim, 0);

        var removed = 0;
        foreach (var (id, weakNode) in RuntimeNodes)
        {
            if (weakNode.TryGetTarget(out _))
                continue;

            if (!RuntimeNodes.TryRemove(new KeyValuePair<string, WeakReference<IComputationNode>>(id, weakNode)))
                continue;

            RuntimeDependencies.TryRemove(id, out _);
            valueCache.Remove(id);
            Versions.Remove(id);
            removed++;
        }

        foreach (var (key, entry) in _reusableNodes)
        {
            if (!entry.Release())
                _reusableNodes.TryRemove(new KeyValuePair<NodeReuseKey, ReusableNode>(key, entry));
        }

        if (_autoTrimInterval > 0)
        {
            // Wait for at least half as many new nodes as remain registered, so that trimming a
            // large registry stays amortized over the nodes added in between
            Volatile.Write(ref _autoTrimThreshold, Math.Max(_autoTrimInterval, RuntimeNodes.Count / 2));
        }

        return removed;
    }

    /// <summary>
    /// The number of entries in the node-reuse cache (for diagnostics and tests).
    /// </summary>
    internal int ReusableNodeCount => _reusableNodes.Count;

    /// <summary>
    /// A node in the reuse cache: always referenced weakly, and also strongly from creation (or the
    /// last reuse) until the next trim.
    /// </summary>
    private sealed class ReusableNode
    {
        private readonly WeakReference<IComputationNode> _weak;
        private IComputationNode? _strong;

        public ReusableNode(IComputationNode node)
        {
            _weak = new WeakReference<IComputationNode>(node);
            _strong = node;
        }

        /// <summary>
        /// Returns the node if it is alive, and keeps it alive until the next trim.
        /// </summary>
        public bool TryUse(out IComputationNode node)
        {
            if (Volatile.Read(ref _strong) is { } strong)
            {
                node = strong;
                return true;
            }

            if (_weak.TryGetTarget(out node!))
            {
                Volatile.Write(ref _strong, node);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Drops the strong reference; returns <see langword="false"/> if the node has been collected.
        /// </summary>
        public bool Release()
        {
            Volatile.Write(ref _strong, null);
            return _weak.TryGetTarget(out _);
        }
    }
}
