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

    private volatile bool _disposed;

    /// <summary>
    /// Whether the context owning this graph has been disposed (see
    /// <see cref="ComputingContextBase.Dispose"/>). Once set, providers of the graph and the sources
    /// owned by the context throw <see cref="ObjectDisposedException"/>: the context no longer tracks
    /// source changes, so its cached values could be stale. One volatile read.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Sets <see cref="IsDisposed"/>. Called once by <see cref="ComputingContextBase.Dispose"/>.
    /// </summary>
    public void MarkDisposed() => _disposed = true;

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> for a read or a source change after
    /// <see cref="MarkDisposed"/>. Kept out of line so that callers' hot paths stay small.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowDisposed(string? name) =>
        throw new ObjectDisposedException(
            name,
            "The computing context that owns this provider or source has been disposed.");

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

    /// <summary>
    /// What one complete change (<see cref="BeginSourceChange"/> followed by
    /// <see cref="EndSourceChange"/>) adds to <see cref="ChangeState"/>: one change started, none active.
    /// </summary>
    public const long CompletedChange = ChangeStarted - 1;

    private long _changeState;
    private long _propagationIds;

    /// <summary>
    /// The source-change state: equal values (read at two points in time) with no active changes
    /// (<c>(state &amp; <see cref="ActiveChangesMask"/>) == 0</c>) mean that no source change was in
    /// progress at any point in between.
    /// </summary>
    public long ChangeState => Volatile.Read(ref _changeState);

    /// <summary>
    /// Whether no source change is in progress (see <see cref="GraphEvaluator.OverlapsChange"/>).
    /// </summary>
    public bool NoChangeInProgress => (ChangeState & ActiveChangesMask) == 0;

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
    /// M's result was not cached because a read failed, M was evicted, or the node no longer reads M), after
    /// which a change stopping at M would leave it stale. With the epoch, any stamp decision anywhere
    /// ends all skipping, which costs nothing in the case it is for: changes with no read between them.
    /// </para>
    /// <para>
    /// <b>Starting before the source publishes (issue #99).</b> A source starts its change before it
    /// publishes its new contents and passes the notification holding it open as
    /// <paramref name="pending"/>; that change is used instead of starting another one, and is ended
    /// once the revision has advanced. No evaluation pass can then read the new contents while the
    /// change state shows no change in progress and the revision is still the one before the change
    /// (see <see cref="GraphEvaluator.OverlapsChange"/>). The epoch is still read after the change
    /// started, as required above.
    /// </para>
    /// <para>
    /// A pass that has made key changes of its own (see <see cref="MarkKeysChanged"/>) counts, for a
    /// stamp it records afterwards, as a pass that started when the latest of them ended; the remarks
    /// there show that the argument above holds for it.
    /// </para>
    /// </remarks>
    /// <param name="sourceId">The source's node ID</param>
    /// <param name="pending">The source's notification, which may hold the change open (see
    /// <see cref="SourceChangedEventArgs.HasPendingChange"/>); <c>null</c> to start the change here</param>
    public void MarkSourceChanged(string sourceId, SourceChangedEventArgs? pending = null)
    {
        var reuse = pending is not null && pending.HasPendingChange;
        if (!reuse)
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
            if (reuse)
                pending!.EndPendingChange();
            else
                EndSourceChange();
        }
    }

    /// <summary>
    /// Records that the entries of the keys <paramref name="keys"/> (key states of a
    /// <see cref="KeyIndex{TKey, TValue}"/>) changed: marks every node downstream of them dirty, as a
    /// change of its own. Called by a dictionary node's commit, with <see cref="CommitLock"/> held,
    /// after the keys' new versions are recorded and before the dictionary value is stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Per-key readers (#95).</b> A node that looks up a key in a dictionary node
    /// (<see cref="GraphEvaluator.ReadKey"/>) depends on the key's state, not on the dictionary node:
    /// a source change upstream of the dictionary marks the dictionary node and stops there
    /// (<see cref="MarkSourceChanged"/> never walks from a dictionary node to its keys). Which keys
    /// changed is only known when the dictionary node is recomputed; its commit then marks the
    /// readers of those keys here. Such a key change follows the rules of a source change: it
    /// increments <see cref="ChangeState"/> for its duration (so no other pass overlapping it records a
    /// stamp), reads <see cref="StampEpoch"/> after that, walks with the same early stopping, and
    /// advances <see cref="Revision"/> once it has marked everything. It returns that revision, which
    /// the pass that made it uses to go on recording stamps (see the last part of these remarks).
    /// </para>
    /// <para>
    /// Between a source change and the dictionary's recomputation, the nodes downstream of a key
    /// (the <b>gated</b> nodes, see <see cref="NodeHandle.Gates"/>) have not been marked although their
    /// key may have changed, so their stamps cannot be trusted on their own. A pass therefore trusts
    /// the stamp of a gated node only after bringing its gates (the dictionary nodes it reads keys of)
    /// up to date, and compares the stamp with the node's counter read after that.
    /// </para>
    /// <para>
    /// <b>Why this is sound.</b> Take a pass P that, after refreshing the gates of a node R, finds R's
    /// stamp s equal to R's counter, and the pass Ps that wrote s. Ps verified or computed R, which
    /// read every key R depends on through its key handle, so it brought the dictionary node D up to
    /// date and checked each key's version against the dictionary result it found (call that commit
    /// D1). A stamp is written only by a pass during which no change ran (<see cref="GraphEvaluator.CanStamp"/>;
    /// for a pass that made key changes of its own before, read "started" as "ended the latest of
    /// them", see below), so a commit of D that changed one of R's keys after D1 started its key
    /// change after Ps ended (one that ran before Ps started would have been D1 or an earlier commit).
    /// Its walk reached R:
    /// R's dependency path to the key state was registered before the values on it were stored, so
    /// before Ps saw them, and an early stop at a node covered in the same epoch is the case of the
    /// #71 argument above (pseudo source: the key state; Ps is a stamping pass that started before the
    /// change ended), so R's counter moved past s. Every such commit marked before storing its value
    /// (commits are serialized by <see cref="CommitLock"/>), and P's refresh of D either found the
    /// latest stored value valid or committed one itself, so every key change before that point has
    /// reached R's counter. Hence a counter still equal to s means none of R's keys changed after D1.
    /// Everything else R depends on reaches it through ordinary dependency edges, which source changes
    /// mark as before. So R's value is what verifying it would give.
    /// </para>
    /// <para>
    /// One case remains: P finds an older value of D valid at its revision while another pass commits
    /// a different one. D's inputs then differ from those its valid value was computed from, which
    /// can only be because a source change had incremented a version but not advanced the revision
    /// yet when P started, or started after P: P overlaps that change, and may return values from
    /// before it, as any pass overlapping a change may. The same holds for a value verified at P's
    /// revision before a key change at that revision, so for other passes the key change's revision
    /// advance is merely conservative: a value verified at a revision was verified after D was brought
    /// up to date at that revision (a gated value cannot be verified without refreshing D), and D's
    /// value at a revision only changes while a source change is in progress (sources publish their
    /// new contents only inside their change, see <see cref="GraphEvaluator.OverlapsChange"/>). A
    /// dictionary recomputed after its value was evicted (or concurrently by two passes) compares
    /// equal to the committed dictionary kept by the index, so it changes no key, unless such a source
    /// change is in progress.
    /// </para>
    /// <para>
    /// Per-key readers also record versions safely: a key's version as of a dictionary result is
    /// either the version at which the key last changed (when that is not later than the result, the
    /// key reads the same in both) or a version that matches nothing (see
    /// <see cref="KeyHandle{TKey, TValue}.VersionAt"/>), so a reader never records a version newer than
    /// the entry it read.
    /// </para>
    /// <para>
    /// <b>The pass that makes the key change (#104).</b> The key change C runs on the thread of the
    /// pass P whose commit of D made it, and ends before P goes on. P does not give up stamping
    /// because of C (<see cref="GraphEvaluator.AccountForOwnKeyChange"/>):
    /// </para>
    /// <list type="number">
    /// <item>P expects the change state to have moved by exactly one complete change per key change
    /// of its own: <see cref="GraphEvaluator.CanStamp"/> holds only if no change was in progress when
    /// P started and every change started since is one of P's own key changes, all ended.</item>
    /// <item>P increments <see cref="StampEpoch"/> again before its next stamp decision.</item>
    /// <item>P takes the revision r_C that C advanced to as its revision.</item>
    /// <item>A node evaluation of P that began before C (it encloses C) records no stamp, and marks its
    /// value verified at the revision it began with. The one exception is the stamp of D's own
    /// commit, whose evaluation ends with C. A gated node's evaluation begins afresh once its gates are
    /// up to date, so the node whose read made C is not such an evaluation.</item>
    /// <item>If a change other than P's own is in progress when C ends, P counts from then on as
    /// overlapping a change (<see cref="GraphEvaluator.OverlapsChange"/>): it no longer takes values by
    /// their revision nor trusts root versions, as a pass starting then would not (it cannot stamp
    /// anymore either, by 1). Its own key change alone never makes it overlap one.</item>
    /// </list>
    /// <para>
    /// <b>Why this is sound.</b> Take a stamp decision d of P for which CanStamp held, after P's own
    /// key changes C_1 ... C_n (n ≥ 1, all ended before d by 1), and let t be the end of C_n and r_C
    /// the revision it advanced to. Compare P with a pass P' that would start at t:
    /// </para>
    /// <list type="bullet">
    /// <item>Changes: by 1, no change was in progress when P started and none other than C_1 ... C_n
    /// started until d, so from t to d none started or was in progress: P' would see none either. A
    /// change that ended before P started advanced the revision before P read its own, so the only
    /// advances after that are those of C_1 ... C_n, and P' would read the revision r_C at t: the
    /// revision P now has by 3. P' overlaps a change (issue #99, <see cref="GraphEvaluator.OverlapsChange"/>)
    /// exactly if one is in progress at t; P then treats itself as overlapping one too (5), and it
    /// already did if a change was in progress when it started. P's own key changes never count:
    /// they publish no source contents, and they have ended at t. (Without a change in progress at t,
    /// a stamp decision is moot anyway once another change has started: 1 fails.)</item>
    /// <item>Epoch: by 2, P incremented the epoch after t and before d, as P' would before its first
    /// stamp decision.</item>
    /// <item>The value: by 4, the evaluation that d belongs to began after t, so it read the node's
    /// dirty counter after t, and everything it relied on was verified or computed after t, or was
    /// verified at r_C or later (P no longer accepts values verified at its earlier revision). A value
    /// verified at r_C or later was verified by a pass whose revision is at least r_C, read after C_n
    /// had marked everything, which accepts only values verified at its revision or later, inductively
    /// with the same property. So the evaluation accepts exactly the values P' would. Results that
    /// were not cached, kept by P from before t (<see cref="GraphEvaluator.FailedReads"/>), make the
    /// reading node's result uncached too, so they are never stamped.</item>
    /// </list>
    /// <para>
    /// The arguments above (#71 and the gates) use only that a stamping pass saw no change in progress
    /// from its start to its decision, incremented the epoch in between, and read the node's counter
    /// and verified its value after its start; so they hold for P' and hence for d. The stamp of D's
    /// own commit (the exception in 4) is decided after C, but its evaluation encloses no other key
    /// change of P (otherwise it records no stamp), so it is the decision of the pass that starts
    /// at the end of P's previous key change (or at P's start), made later: C marks only nodes
    /// downstream of D's keys, which are neither D nor anything D read (the edges form a DAG), so C
    /// changes nothing that decision depends on, and by 1 no other change started meanwhile.
    /// </para>
    /// <para>
    /// Each rule is needed (see LookupStampTests). Without the guard of C (or with an expected change
    /// state that did not count C), a source change that runs, complete, during C would look to P like
    /// its own key change; such a change can stop early at D (covered in the epoch C read), so P would
    /// stamp D from inputs read before it. Without 2, a later change of the epoch C read, which C
    /// recorded on the nodes it marked, could stop at nodes P stamps after C. Without 3, or with the
    /// enclosing evaluations of 4 marking their values at r_C, P would accept values verified before C
    /// (by itself or by another pass) that C made stale: D may be committed twice at one revision
    /// with different values when it was evicted while a source's new contents were visible without
    /// a change in progress. Since sources publish only inside their change (#99), a pass that can
    /// still stamp no longer meets that case, so 3 and the revision rule of 4 are defence in depth,
    /// kept so that the comparison with P' does not depend on how sources publish (the tests apply a
    /// source by hand to reach it). Without 5, P would take, by their revision or root versions,
    /// values that a pass starting after C would have to verify while a source publishes. Without
    /// the stamp rule of 4, an enclosing evaluation would stamp with a counter read before C, which C
    /// may have skipped (covered in its epoch). Other passes overlapping C still record no stamp.
    /// </para>
    /// <para>
    /// Runs under <see cref="CommitLock"/>, so <see cref="OnNodeMarked"/> must not wait for anything
    /// that commits a value while a key change is marking.
    /// </para>
    /// </remarks>
    /// <returns>The revision this key change advanced <see cref="Revision"/> to</returns>
    internal long MarkKeysChanged(List<NodeState> keys)
    {
        BeginSourceChange();
        try
        {
            // After BeginSourceChange (see MarkSourceChanged)
            var epoch = StampEpoch;
            foreach (var key in keys)
                MarkDependentsDirty(key, epoch);

            // After marking, like a source change (see the remarks)
            return Interlocked.Increment(ref _revision);
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
            if (!existing.TryGetTarget(out _))
                RuntimeNodes.TryUpdate(node.Id, new WeakReference<IComputationNode>(node), existing);

            return;
        }

        if (!RuntimeNodes.TryAdd(node.Id, new WeakReference<IComputationNode>(node)))
            return; // registered concurrently

        if (_autoTrimInterval > 0)
            OnRuntimeNodeAdded();
    }

    // Automatic trimming (see EnableAutoTrim); _autoTrimInterval is 0 when it is off
    private int _autoTrimInterval;
    private LRUCache<string, object>? _autoTrimValueCache;
    private int _nodesAddedSinceTrim;
    private int _autoTrimThreshold;

    // GC.CollectionCount(GC.MaxGeneration) when the previous trim started
    private int _fullCollectionsAtTrim;

    /// <summary>
    /// Makes this state trim unused nodes automatically, after every <paramref name="interval"/>
    /// runtime nodes added to <see cref="RuntimeNodes"/>, or after as many nodes as half the number
    /// of nodes that survived the previous trim if that is more (so that the cost of a trim, which
    /// scans the registry, stays proportional to the number of nodes added or removed). After a
    /// full garbage collection, the next trim runs after <paramref name="interval"/> nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check costs one interlocked increment per added node (and, between
    /// <paramref name="interval"/> nodes and the threshold, a read of the collection count); nothing
    /// is done on reads or for nodes that are already registered. A trim is never started inside a
    /// node computation (it is started by the next node added outside one instead), and never waits:
    /// when another trim or a node creation holds the lock, the next added node tries again. Because
    /// a trim only removes nodes that have already been collected and releases the others, nodes
    /// released by one automatic trim are removed by a later one, once the garbage collector has
    /// collected them.
    /// </para>
    /// <para>
    /// The threshold (issue #86). A trim costs one step per registry entry. The entries it removes
    /// pay for themselves (each node is removed once), and so do the nodes added since the previous
    /// trim (each is added once); only the entries that survived the previous trim as well must be
    /// paid for by new nodes, so the next trim waits for half as many new nodes as there are such
    /// survivors. The nodes added since the previous trim are not counted, as right after a trim they
    /// are mostly nodes that only the released reuse cache kept alive and that are about to be
    /// collected. That is still too many after a burst of nodes that were all alive at once: the trims
    /// during the burst found them alive and spaced the next ones by half of them. A full collection
    /// therefore lets the next trim run after <paramref name="interval"/> new nodes: what the collection
    /// found dead pays for its removal, and the live entries scanned are fewer than the objects the
    /// collection itself traced, so at most one such early trim per full collection costs no more
    /// than the collections do.
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
        var added = Interlocked.Increment(ref _nodesAddedSinceTrim);
        if (added < _autoTrimInterval || (added < Volatile.Read(ref _autoTrimThreshold) && !FullCollectionSinceTrim()))
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
            var addedSinceTrim = Volatile.Read(ref _nodesAddedSinceTrim);
            if (addedSinceTrim >= _autoTrimInterval
                && (addedSinceTrim >= Volatile.Read(ref _autoTrimThreshold) || FullCollectionSinceTrim()))
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

    // Whether a full (highest-generation) garbage collection has run since the previous trim started
    private bool FullCollectionSinceTrim() =>
        GC.CollectionCount(GC.MaxGeneration) != Volatile.Read(ref _fullCollectionsAtTrim);

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
    /// <item>Runtime nodes that have been garbage-collected are removed from <see cref="RuntimeNodes"/>,
    /// together with their cached values and version records.
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
        var addedSinceTrim = Interlocked.Exchange(ref _nodesAddedSinceTrim, 0);
        Volatile.Write(ref _fullCollectionsAtTrim, GC.CollectionCount(GC.MaxGeneration));

        var removed = 0;
        foreach (var (id, weakNode) in RuntimeNodes)
        {
            if (weakNode.TryGetTarget(out _))
                continue;

            if (!RuntimeNodes.TryRemove(new KeyValuePair<string, WeakReference<IComputationNode>>(id, weakNode)))
                continue;

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
            // Wait for at least half as many new nodes as there are nodes that survived the previous
            // trim too, so that trimming a large registry stays amortized (see EnableAutoTrim)
            var survivors = Math.Max(0, RuntimeNodes.Count - addedSinceTrim);
            Volatile.Write(ref _autoTrimThreshold, Math.Max(_autoTrimInterval, survivors / 2));
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
