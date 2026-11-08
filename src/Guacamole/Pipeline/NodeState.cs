namespace Guacamole.Pipeline;

/// <summary>
/// The change-tracking state of one node, shared by everything that refers to the node by ID
/// (see <see cref="VersionTracker"/>): its output version, its dirty counter and the nodes that
/// have read it.
/// </summary>
/// <remarks>
/// <para>
/// The <b>output version</b> increments only when the node's value changes (for a source, on every
/// change notification).
/// </para>
/// <para>
/// The <b>dirty counter</b> increments whenever a source upstream of the node changes: a source
/// change walks the reverse dependency edges (<see cref="AddDependent"/>) from the source and marks
/// every node it reaches. A cached value records the counter it was verified against (its stamp,
/// see <see cref="CachedNodeValue.Stamp"/>); while the counter still has that value, no source the
/// value depends on has changed, and the value is valid without checking its dependencies. This
/// makes verification after a change proportional to the changed paths instead of the graph.
/// </para>
/// <para>
/// Holds no reference to the node or its handle (dependents are referenced weakly), so it never
/// keeps a node alive.
/// </para>
/// </remarks>
internal sealed class NodeState
{
    private const int MinPruneThreshold = 16;

    private long _version;
    private long _dirty;
    private long _lastPropagation;

    // Reverse edges: the handles of the nodes whose computations read this node. Only ever grows
    // (except for collected handles), so a change reaches every node that has ever read this one.
    private HashSet<WeakReference<NodeHandle>>? _dependents;
    private int _pruneThreshold = MinPruneThreshold;

    // Snapshot of _dependents for marking without the lock; reset (under the lock) when the set changes
    private WeakReference<NodeHandle>[]? _dependentsSnapshot;

    public NodeState(string id)
    {
        Id = id;
    }

    /// <summary>
    /// The node's ID.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The node's current output version.
    /// </summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>
    /// Increments the output version and returns the new version.
    /// </summary>
    public long IncrementVersion() => Interlocked.Increment(ref _version);

    /// <summary>
    /// The number of source changes that have reached this node (see the remarks).
    /// </summary>
    public long Dirty => Volatile.Read(ref _dirty);

    /// <summary>
    /// Records that a source upstream of this node (or this node, if it is a source) has changed.
    /// </summary>
    public void MarkDirty() => Interlocked.Increment(ref _dirty);

    /// <summary>
    /// Marks this node as visited by the propagation <paramref name="propagation"/>; returns
    /// <see langword="false"/> if that propagation has visited it already. Two concurrent
    /// propagations may visit a node more than once, which only costs an extra increment.
    /// </summary>
    public bool TryVisit(long propagation) => Interlocked.Exchange(ref _lastPropagation, propagation) != propagation;

    /// <summary>
    /// Records that the node of <paramref name="dependent"/> read this node, so that a change of
    /// this node's sources marks it dirty. Idempotent; collected dependents are pruned as the set grows.
    /// </summary>
    public void AddDependent(WeakReference<NodeHandle> dependent)
    {
        lock (this)
        {
            var dependents = _dependents ??= new HashSet<WeakReference<NodeHandle>>();
            if (!dependents.Add(dependent))
                return;

            if (dependents.Count >= _pruneThreshold)
            {
                dependents.RemoveWhere(static d => !d.TryGetTarget(out _));
                _pruneThreshold = Math.Max(MinPruneThreshold, dependents.Count * 2);
            }

            // Published after the set has changed: a marking that reads the snapshot afterwards
            // rebuilds it and sees the new dependent.
            Volatile.Write(ref _dependentsSnapshot, null);

            // The caller reads the source-change state next (GraphEvaluator.CanStamp); that read
            // must not move before this write, or a change could both read the old snapshot and be
            // missed by the caller (a marking reads the snapshot after a full fence).
            Interlocked.MemoryBarrier();
        }
    }

    /// <summary>
    /// Appends the live dependents to <paramref name="target"/>, and prunes the collected ones.
    /// </summary>
    public void CopyDependentsTo(List<NodeHandle> target)
    {
        var snapshot = Volatile.Read(ref _dependentsSnapshot) ?? TakeSnapshot();

        var dead = false;
        foreach (var dependent in snapshot)
        {
            if (dependent.TryGetTarget(out var handle))
                target.Add(handle);
            else
                dead = true;
        }

        if (dead)
        {
            lock (this)
            {
                if (_dependents!.RemoveWhere(static d => !d.TryGetTarget(out _)) > 0)
                    Volatile.Write(ref _dependentsSnapshot, null);
            }
        }
    }

    private WeakReference<NodeHandle>[] TakeSnapshot()
    {
        lock (this)
        {
            // Taking the lock also orders this read after any AddDependent that reset the snapshot
            var snapshot = _dependentsSnapshot;
            if (snapshot is null)
            {
                snapshot = _dependents is null ? [] : [.. _dependents];
                Volatile.Write(ref _dependentsSnapshot, snapshot);
            }

            return snapshot;
        }
    }

    /// <summary>
    /// The number of recorded dependents, including collected ones not yet pruned (for tests).
    /// </summary>
    internal int DependentCount
    {
        get
        {
            lock (this)
            {
                return _dependents?.Count ?? 0;
            }
        }
    }
}
