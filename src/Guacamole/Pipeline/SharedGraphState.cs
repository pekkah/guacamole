using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    // Source changes in progress: the low bits count active changes, the high bits count the
    // changes started so far. One atomic read tells whether a change was active or has started
    // since an earlier read (see ChangeState and GraphEvaluator.CanStamp).
    private const long ChangeStarted = (1L << ActiveChangeBits) + 1;
    private const int ActiveChangeBits = 24;

    /// <summary>
    /// The bits of <see cref="ChangeState"/> that count the source changes in progress.
    /// </summary>
    public const long ActiveChangesMask = (1L << ActiveChangeBits) - 1;

    private long _changeState;
    private long _propagationIds;

    /// <summary>
    /// The source-change state: equal values (read at two points in time) with no active changes
    /// (<c>(state &amp; <see cref="ActiveChangesMask"/>) == 0</c>) mean that no source change was in
    /// progress at any point in between.
    /// </summary>
    public long ChangeState => Volatile.Read(ref _changeState);

    // Starts at 1, so that a node that has never been marked (NodeState.MarkedEpoch 0) is never covered
    private long _stampEpoch = 1;

    /// <summary>
    /// The stamp epoch: incremented by every evaluation pass before it first decides whether it may
    /// record stamps (see <see cref="GraphEvaluator.CanStamp"/>), so it moves on whenever a value may
    /// have been stamped. Changes use it to stop marking early (see <see cref="MarkSourceChanged"/>).
    /// </summary>
    public long StampEpoch => Volatile.Read(ref _stampEpoch);

    /// <summary>
    /// Increments <see cref="StampEpoch"/>. Called by an evaluation pass before its first stamp decision.
    /// </summary>
    public void AdvanceStampEpoch() => Interlocked.Increment(ref _stampEpoch);

    /// <summary>
    /// Called by every evaluation pass between reading <see cref="ChangeState"/> and
    /// <see cref="Revision"/> (see <see cref="GraphEvaluator"/>); <c>null</c> except in tests, which
    /// use it to complete a change between the two reads.
    /// </summary>
    internal Action? OnEvaluationSnapshot { get; set; }

    /// <summary>
    /// Called by a change after it has marked a node downstream of its source (before it walks the
    /// node's dependents); <c>null</c> except in tests, which use it to interleave other changes and
    /// reads with a change in progress.
    /// </summary>
    internal Action<NodeState>? OnNodeMarked { get; set; }

    // Per-thread scratch space for propagations (a propagation runs no user code, so it never nests)
    [ThreadStatic]
    private static List<MarkingFrame>? t_frames;

    [ThreadStatic]
    private static List<NodeHandle>? t_dependents;

    /// <summary>
    /// A node being marked: its dependents are <c>t_dependents[Next..]</c> up to the start of the
    /// next frame's dependents (the end of the list while it is the innermost frame).
    /// </summary>
    private struct MarkingFrame(NodeState state, int start)
    {
        public readonly NodeState State = state;
        public readonly int Start = start;
        public int Next = start;
    }

    /// <summary>
    /// Records that the source <paramref name="sourceId"/> changed: increments its version, marks
    /// it and every node that (transitively) read it dirty (see <see cref="NodeState"/>), then
    /// advances <see cref="Revision"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marking walks only the nodes downstream of the source, so its cost is proportional to them,
    /// and verifying the graph afterwards only has to look into the marked nodes. Thread-safe;
    /// changes may run concurrently with each other and with evaluation passes.
    /// </para>
    /// <para>
    /// <b>Stopping early (#71).</b> Several changes between two reads would otherwise each walk
    /// everything downstream of their sources. A change skips the nodes that an earlier change has
    /// already marked, with everything downstream of them, since a value could last have been
    /// stamped:
    /// </para>
    /// <list type="number">
    /// <item>Every evaluation pass increments <see cref="StampEpoch"/> before its first stamp decision
    /// (<see cref="GraphEvaluator.CanStamp"/>). Passes that only return clean values make no stamp
    /// decision and leave it alone.</item>
    /// <item>A change reads the epoch E once, right after <see cref="BeginSourceChange"/>. It walks the
    /// dependents depth first, incrementing the dirty counter of each node it enters, and once it has
    /// handled all the dependents of a node, records E as that node's
    /// <see cref="NodeState.MarkedEpoch"/>.</item>
    /// <item>A change of epoch E does not enter a node whose marked epoch is E (it always marks its
    /// source, but skips the source's dependents if the source's marked epoch is E).</item>
    /// </list>
    /// <para>
    /// <b>Why this is sound.</b> Only a stamp can make a stale value outlive a change: a value
    /// verified at a revision was verified after every change that ended before that revision was
    /// read, and verification itself compares versions. So it suffices that, once a change C has
    /// ended, no node downstream of its source has a stamp matching its dirty counter that was
    /// written by a pass that started before C ended. Two facts give that:
    /// </para>
    /// <list type="number">
    /// <item><b>Coverage.</b> A marked epoch E is recorded on a node only after every node reachable
    /// from it (through the dependents its walk read) has had its counter incremented by a change of
    /// epoch E: by this walk, or, for a node it skipped, by the change whose walk recorded E there
    /// (inductively, after the same held for that node; the edges form a DAG, so a node found visited
    /// in the same walk has been handled completely). So when C skips a node M, every node X
    /// reachable from M has already been incremented by some change C_x of epoch E.</item>
    /// <item><b>Lemma.</b> If a pass P records a stamp and started after some change of epoch E had
    /// ended, every change of epoch E had ended before P started. P incremented the epoch after it
    /// started, so after that change read E: the increment moved the epoch past E. A change that read
    /// E therefore read it before P's increment. P increments the epoch and then reads the change
    /// state, a change increments the change state and then reads the epoch, all with full fences, so
    /// at least one of them sees the other's write: P saw the change start, and since P's
    /// <see cref="GraphEvaluator.CanStamp"/> held, the change had started and ended before P started.</item>
    /// </list>
    /// <para>
    /// Now take C (epoch E) skipping M, and X reachable from M. A stamp matching X's counter counts
    /// C_x's increment, so the pass P that wrote it read the counter after C_x had started; as its
    /// CanStamp held, C_x had ended before P started, and by the lemma so had C. That holds whenever
    /// P writes the stamp, also while C is still walking: the reader that is re-verifying X and stamps
    /// it concurrently with C either started after C ended or records no stamp (C's start changes the
    /// change state it checks), and a stamp it decided on before C started carries a counter read
    /// before C_x's increment, which no longer matches. For a node reached through an edge registered
    /// after the covering walk read the dependents: a value that read through that edge is stamped
    /// after the edge was registered, while the covering change was in progress or later, so by a pass
    /// that started after it ended, and by the lemma after C ended. Finally, a pass that started after
    /// C ended saw C's new source version, and the stamps it relied on were written by passes that
    /// started after C ended too (induction over passes), so what it stamps reflects C.
    /// </para>
    /// <para>
    /// The epoch is global on purpose. A per-node "verified since marked" flag would not do: a node
    /// downstream of M can be verified and stamped while M is not (M is served through its revision,
    /// M's result is an error and not cached, M was evicted, or the node no longer reads M), after
    /// which a change stopping at M would leave it stale. With the epoch, any stamp decision anywhere
    /// ends all skipping, which costs nothing in the case it is for: changes with no read between them.
    /// </para>
    /// </remarks>
    public void MarkSourceChanged(string sourceId)
    {
        BeginSourceChange();
        try
        {
            // After BeginSourceChange (see the remarks)
            var epoch = StampEpoch;
            var source = Versions.GetState(sourceId);

            // The version first: a pass that sees a dirty mark also sees the new version.
            source.IncrementVersion();
            MarkDependentsDirty(source, epoch);

            // Advance the revision after the version, so that a value verified at the new revision
            // has seen the new version.
            AdvanceRevision();
        }
        finally
        {
            EndSourceChange();
        }
    }

    /// <summary>
    /// Records that a source change has started (see <see cref="ChangeState"/>). Every call must be
    /// followed by <see cref="EndSourceChange"/>; internal so tests can interleave reads with a change.
    /// </summary>
    internal void BeginSourceChange() => Interlocked.Add(ref _changeState, ChangeStarted);

    /// <summary>
    /// Records that a source change started by <see cref="BeginSourceChange"/> has finished.
    /// </summary>
    internal void EndSourceChange() => Interlocked.Decrement(ref _changeState);

    /// <summary>
    /// Marks <paramref name="source"/> and the nodes downstream of it dirty, depth first, skipping
    /// nodes covered in <paramref name="epoch"/> and recording <paramref name="epoch"/> on each node
    /// once everything downstream of it is marked (see <see cref="MarkSourceChanged"/>).
    /// </summary>
    private void MarkDependentsDirty(NodeState source, long epoch)
    {
        source.MarkDirty();
        if (source.MarkedEpoch == epoch)
            return; // everything downstream is marked already

        var frames = t_frames ??= new List<MarkingFrame>();
        var dependents = t_dependents ??= new List<NodeHandle>();
        var propagation = Interlocked.Increment(ref _propagationIds);

        try
        {
            source.TryVisit(propagation);
            Enter(source);

            while (frames.Count > 0)
            {
                ref var frame = ref CollectionsMarshal.AsSpan(frames)[^1];
                if (frame.Next < dependents.Count)
                {
                    var state = dependents[frame.Next++].State;

                    // Covered by a change of this epoch, or handled by this walk already
                    if (state.MarkedEpoch == epoch || !state.TryVisit(propagation))
                        continue;

                    state.MarkDirty();
                    OnNodeMarked?.Invoke(state);
                    Enter(state); // invalidates `frame`
                }
                else
                {
                    // Everything downstream of this node is marked: record it (only now, see the remarks)
                    frame.State.SetMarkedEpoch(epoch);
                    dependents.RemoveRange(frame.Start, dependents.Count - frame.Start);
                    frames.RemoveAt(frames.Count - 1);
                }
            }
        }
        finally
        {
            frames.Clear();
            dependents.Clear();
        }

        void Enter(NodeState state)
        {
            frames.Add(new MarkingFrame(state, dependents.Count));
            state.CopyDependentsTo(dependents);
        }
    }

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
