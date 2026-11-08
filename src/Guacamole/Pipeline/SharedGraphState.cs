using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Shared mutable state for computation graph that persists across graph versions.
/// This allows runtime-discovered nodes (from extension methods) to be tracked
/// for cache invalidation even when the graph structure changes.
/// </summary>
internal sealed class SharedGraphState
{
    /// <summary>
    /// Runtime-discovered nodes (e.g., from extension methods like Combine, Select).
    /// Thread-safe collection for nodes that are discovered during execution.
    /// Shared across all versions of the graph.
    /// </summary>
    public ConcurrentDictionary<string, IComputationNode> RuntimeNodes { get; } = new();

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
    /// Evaluation handles of the nodes that have been evaluated, by node ID. Cached values record
    /// the handles of the dependencies they read, so they can be verified without knowing the
    /// dependencies' value types.
    /// </summary>
    public ConcurrentDictionary<string, NodeHandle> Handles { get; } = new();

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
    /// <see cref="Lazy{T}"/> guarantees a single node per key under concurrent access.
    /// </summary>
    private readonly ConcurrentDictionary<NodeReuseKey, Lazy<IComputationNode>> _reusableNodes = new();

    /// <summary>
    /// Returns the node previously created for <paramref name="key"/>, or creates one with
    /// <paramref name="factory"/>. Thread-safe: concurrent callers with equal keys all
    /// receive the same node instance and the factory runs at most once per key.
    /// </summary>
    public IComputationNode GetOrCreateReusableNode(NodeReuseKey key, Func<IComputationNode> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        return _reusableNodes
            .GetOrAdd(key, static (_, f) => new Lazy<IComputationNode>(f, LazyThreadSafetyMode.ExecutionAndPublication), factory)
            .Value;
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
        if (staticNodes.ContainsKey(node.Id) || RuntimeNodes.ContainsKey(node.Id))
        {
            return; // Already registered
        }

        // Add to runtime collections
        RuntimeNodes.TryAdd(node.Id, node);
        RuntimeDependencies.TryAdd(node.Id, node.Dependencies);
    }
}
