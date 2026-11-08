using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Guacamole.Diagnostics;

namespace Guacamole.Pipeline;

/// <summary>
/// One evaluation pass: brings the nodes a provider read needs up to date and records, for every
/// node it computes, the versions of the dependencies that computation read.
/// </summary>
/// <remarks>
/// <para>
/// Validation works by verifying, not by looking through: a cached node is up to date when each
/// dependency it read, once brought up to date itself (recursively, recomputing it if needed),
/// still has the version the node recorded. A dependency that recomputes to an equal value keeps
/// its version, so everything downstream of it stays cached, whether or not that dependency is
/// read through a provider itself (the equality cut-off applies at every stage).
/// </para>
/// <para>
/// Root nodes (sources, which have no dependencies) are read fresh by their dependents, and their
/// versions are advanced by <see cref="ComputationGraph.MarkSourceChanged"/>. A dependent records
/// the root's version read <b>before</b> reading its value, so a change racing with the read is
/// seen as a change on the next read.
/// </para>
/// <para>
/// A source change also marks every node downstream of the source dirty (see <see cref="NodeState"/>).
/// A cached value whose stamp still matches its node's dirty counter is valid without verifying
/// its dependencies, so after a change only the marked nodes are verified dependency by dependency;
/// unchanged subtrees cost one check each.
/// </para>
/// <para>
/// An evaluator is used by one thread for one provider read and is then discarded.
/// </para>
/// </remarks>
internal sealed class GraphEvaluator
{
    private static readonly IReadOnlyDictionary<string, object> NoCachedValues = new Dictionary<string, object>();

    // Dependencies read by the computations in progress; each computation owns the tail that
    // starts where it began (see BeginFrame / EndFrame).
    private readonly List<DependencyVersion> _reads = new();

    // Results that are not cached (see FailedReads), so that a node is computed at most once per pass
    // even when it is both verified and read.
    private Dictionary<string, (object Result, long Version)>? _uncachedResults;

    // Number of dependency reads in this pass that ended with an exception (see FailedReads)
    private int _failedReads;

    /// <summary>
    /// The number of dependency reads in this pass that threw instead of returning a result.
    /// </summary>
    /// <remarks>
    /// A read that throws records no dependency, so a result computed while one was in progress (the
    /// node's operator usually turns the exception into an error) does not list everything it depends
    /// on, and the exception may not be a function of the versions it did record. Such a result is
    /// therefore not cached (see <c>NodeHandle&lt;TResult&gt;.Commit</c>). Reads only throw for
    /// failures of the framework itself; exceptions of user lambdas are caught by the operator of the
    /// node that runs them and become that node's error result.
    /// </remarks>
    internal int FailedReads => _failedReads;

    // An exception filter: counts the failed read without catching the exception
    private bool NoteFailedRead()
    {
        _failedReads++;
        return false;
    }

    /// <summary>
    /// Counts a read of a result kept only for this pass (see <see cref="TryGetUncachedResult{TResult}"/>)
    /// as a failed read, so that the reading node's result is not cached either.
    /// </summary>
    internal void NoteUncachedRead() => _failedReads++;

    public GraphEvaluator(ComputationGraph graph)
    {
        Graph = graph;
        State = graph.SharedState;

        // The change state before the revision: a pass that sees no change in progress then reads a
        // revision that every change ended by then has advanced, so a value verified at an older
        // revision (possibly before such a change had incremented its source's version) is not taken
        // as verified by this pass. Read the other way round, a change could advance the revision and
        // end between the two reads; the pass would accept values verified before the change and
        // still be allowed to stamp what it derives from them.
        _changeState = State.ChangeState;
        State.OnEvaluationSnapshot?.Invoke();
        Revision = State.Revision;
        Context = new ComputationContext(NoCachedValues) { Evaluator = this };
    }

    // The source-change state when this pass started (read before Revision)
    private readonly long _changeState;

    /// <summary>
    /// Whether a source change was in progress when this pass started. Such a pass neither takes
    /// values as verified by their revision nor trusts the versions of roots (issue #99).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A change in progress may have published its source's new contents without having advanced
    /// the source's version or the revision yet (a source starts its change before it publishes, see
    /// <see cref="SourceChangedEventArgs"/>). Passes at the same revision can therefore read
    /// different contents: one that read the old contents and commits last leaves an older value
    /// verified at that revision, under the same root version as the newer one. A pass that started
    /// with no change in progress is not affected: every change that published before it started had
    /// ended, so the revision it read covers them. But a pass that started during the change, for
    /// example a read on a thread that has just returned the newer value, could take the older value
    /// by its revision, or by the root version recorded with it, and go back in time.
    /// </para>
    /// <para>
    /// Such a pass therefore verifies every value through its dependencies and treats each root
    /// dependency as changed, so the nodes reading roots are recomputed from the current contents
    /// (their versions only move on if the result differs). Versions of computed nodes still identify
    /// their values (a version is assigned at commit and shared only by equal values), and stamps
    /// are still trusted: a value stamped by a pass that started before the change read the contents
    /// before it, and any value committed since replaced it. This costs only the passes that start
    /// while another thread is changing a source; the output handlers a change triggers run after it
    /// has ended.
    /// </para>
    /// </remarks>
    public bool OverlapsChange => (_changeState & SharedGraphState.ActiveChangesMask) != 0;

    // Whether this pass may still record stamps: 0 until the first stamp decision, then 1 (yes, so
    // far) or -1 (no, for the rest of the pass)
    private int _stamping;

    /// <summary>
    /// Whether this pass may record stamps (<see cref="CachedNodeValue.Stamp"/>): no source change
    /// was in progress when it started, and none has started since. Called after the verification or
    /// computation whose result would be stamped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stamp says that no change has reached the node since its value was verified. That holds only
    /// if every change already counted by the node's dirty counter had finished marking all the nodes
    /// it reaches (after incrementing its source's version) before the verification looked at them:
    /// a change still in progress may have marked this node but not yet the nodes between it and the
    /// source. Passes overlapping a change still verify correctly; they just do not record stamps.
    /// </para>
    /// <para>
    /// The first call of a pass that may stamp increments <see cref="SharedGraphState.StampEpoch"/>
    /// before it checks the change state, which ends early stopping for the changes that start
    /// afterwards (see <see cref="SharedGraphState.MarkSourceChanged"/>).
    /// </para>
    /// </remarks>
    public bool CanStamp()
    {
        if (_stamping == 0)
        {
            if ((_changeState & SharedGraphState.ActiveChangesMask) != 0)
            {
                _stamping = -1;
                return false;
            }

            // A full fence: the change state read below cannot move before it
            State.AdvanceStampEpoch();
            _stamping = 1;
        }

        if (_stamping < 0)
            return false;

        if (State.ChangeState != _changeState)
        {
            _stamping = -1;
            return false;
        }

        return true;
    }

    // Number of node computations in progress on this thread (nested when a computation reads a
    // provider that has to be computed itself)
    [ThreadStatic]
    private static int t_computationDepth;

    /// <summary>
    /// Whether a node computation is in progress on the current thread.
    /// </summary>
    public static bool IsComputing => t_computationDepth > 0;

    internal static void EnterComputation() => t_computationDepth++;

    internal static void ExitComputation() => t_computationDepth--;

    public ComputationGraph Graph { get; }

    public SharedGraphState State { get; }

    /// <summary>
    /// The graph revision when this pass started. Values verified or computed in this pass are
    /// marked as up to date at this revision.
    /// </summary>
    public long Revision { get; }

    /// <summary>
    /// The context nodes are computed with; reads through it are recorded.
    /// </summary>
    public ComputationContext Context { get; }

    /// <summary>
    /// Reads a single-value dependency of the node being computed.
    /// </summary>
    public Result<T, ComputationError> Read<T>(IComputationNode<T> node)
    {
        try
        {
            return ReadCore(node);
        }
        catch (Exception) when (NoteFailedRead())
        {
            throw; // Not reached: the filter never catches
        }
    }

    /// <summary>
    /// Reads a collection dependency of the node being computed.
    /// </summary>
    public Result<IEnumerable<T>, ComputationError> Read<T>(ICollectionComputationNode<T> node)
    {
        try
        {
            return ReadCore(node);
        }
        catch (Exception) when (NoteFailedRead())
        {
            throw; // Not reached: the filter never catches
        }
    }

    /// <summary>
    /// Reads the entry of <paramref name="key"/> in a dictionary dependency of the node being
    /// computed (see <see cref="KeyIndex{TKey, TValue}"/>): returns the whole dictionary result, but
    /// records a dependency on the key only, so the reading node is invalidated only when that
    /// key's entry (or the dictionary's success or failure) changes. A root dictionary (a source) is
    /// read like any other dependency.
    /// </summary>
    public Result<ImmutableDictionary<TKey, TValue>, ComputationError> ReadKey<TKey, TValue>(IComputationNode<ImmutableDictionary<TKey, TValue>> node, TKey key)
        where TKey : notnull
    {
        if (node.Dependencies.IsEmpty)
            return Read(node);

        try
        {
            var dictionary = ValueNodeHandle<ImmutableDictionary<TKey, TValue>>.For(node, Graph);
            var keyHandle = KeyIndex<TKey, TValue>.GetKeyHandle(dictionary, key, Graph);
            var result = dictionary.Evaluate(this, out var version);

            // The key's version as of the dictionary result just read (see KeyHandle.VersionAt)
            _reads.Add(new DependencyVersion(keyHandle.State, keyHandle.VersionAt(version), keyHandle.WeakSelf));
            return result;
        }
        catch (Exception) when (NoteFailedRead())
        {
            throw; // Not reached: the filter never catches
        }
    }

    private Result<T, ComputationError> ReadCore<T>(IComputationNode<T> node)
    {
        Result<T, ComputationError> result;
        long version;
        NodeState state;
        WeakReference<NodeHandle>? handle = null;

        if (node.Dependencies.IsEmpty)
        {
            // A root's version is read before its value (see the class remarks)
            state = State.Versions.GetState(node.Id);
            version = state.Version;
            result = node.Compute(Context);
        }
        else
        {
            var typed = ValueNodeHandle<T>.For(node, Graph);
            state = typed.State;
            handle = typed.WeakSelf;
            result = typed.Evaluate(this, out version);
        }

        _reads.Add(new DependencyVersion(state, version, handle));
        return result;
    }

    private Result<IEnumerable<T>, ComputationError> ReadCore<T>(ICollectionComputationNode<T> node)
    {
        Result<IEnumerable<T>, ComputationError> result;
        long version;
        NodeState state;
        WeakReference<NodeHandle>? handle = null;

        if (node.Dependencies.IsEmpty)
        {
            // A root's version is read before its value (see the class remarks)
            state = State.Versions.GetState(node.Id);
            version = state.Version;
            result = node.Compute(Context);
        }
        else
        {
            var typed = CollectionNodeHandle<T>.For(node, Graph);
            state = typed.State;
            handle = typed.WeakSelf;
            result = typed.Evaluate(this, out version);
        }

        _reads.Add(new DependencyVersion(state, version, handle));
        return result;
    }

    internal int BeginFrame() => _reads.Count;

    internal DependencyVersion[] EndFrame(int start)
    {
        var count = _reads.Count - start;
        if (count == 0)
            return [];

        var dependencies = CollectionsMarshal.AsSpan(_reads).Slice(start, count).ToArray();
        _reads.RemoveRange(start, count);
        return dependencies;
    }

    internal bool TryGetUncachedResult<TResult>(string nodeId, out Result<TResult, ComputationError> result, out long version)
    {
        if (_uncachedResults != null
            && _uncachedResults.TryGetValue(nodeId, out var entry)
            && entry.Result is Result<TResult, ComputationError> typed)
        {
            result = typed;
            version = entry.Version;
            return true;
        }

        result = default;
        version = 0;
        return false;
    }

    internal void SetUncachedResult<TResult>(string nodeId, Result<TResult, ComputationError> result, long version)
    {
        (_uncachedResults ??= new Dictionary<string, (object, long)>())[nodeId] = (result, version);
    }
}

/// <summary>
/// Evaluates one node of the graph without the caller knowing its value type, so that the
/// dependencies a cached value recorded can be verified.
/// </summary>
internal abstract class NodeHandle
{
    protected NodeHandle(NodeState state)
    {
        State = state;
        WeakSelf = new WeakReference<NodeHandle>(this);
    }

    /// <summary>
    /// The node's change-tracking state (output version, dirty counter, dependents).
    /// </summary>
    public NodeState State { get; }

    /// <summary>
    /// A weak reference to this handle, shared by all cached values that record it as a dependency.
    /// </summary>
    public WeakReference<NodeHandle> WeakSelf { get; }

    // The dictionary handles whose per-key changes reach this node (see Gates); grows only, under
    // SharedGraphState.CommitLock
    private NodeHandle[] _gates = [];

    /// <summary>
    /// The <b>gates</b> of this node: the handles of the dictionary nodes that this node depends on
    /// through a per-key read (<see cref="GraphEvaluator.ReadKey"/>), directly or through other nodes.
    /// Empty for most nodes.
    /// </summary>
    /// <remarks>
    /// A change upstream of a dictionary node marks the dictionary node but not its per-key readers:
    /// which keys changed is only known once the dictionary has been recomputed, and the dictionary
    /// node then marks the readers of the keys whose entries changed (see
    /// <see cref="KeyIndex{TKey, TValue}"/>). Until then, the stamps of the nodes downstream of a
    /// per-key read cannot be trusted on their own, so a pass brings a node's gates up to date before
    /// it trusts the node's stamp (see <c>NodeHandle&lt;TResult&gt;.Evaluate</c>). Read after the
    /// stamp (see there). Grows only: a node that no longer reads through a key keeps the gate, which
    /// costs one cached read of the dictionary per stamp check.
    /// </remarks>
    public NodeHandle[] Gates => Volatile.Read(ref _gates);

    /// <summary>
    /// Sets the gates of a handle that is not shared yet (a key handle's gate is its dictionary).
    /// </summary>
    protected void InitializeGates(NodeHandle[] gates) => _gates = gates;

    /// <summary>
    /// Adds <paramref name="gates"/> to this node's gates and to those of every node downstream of it
    /// (through the recorded dependents). Called with <see cref="SharedGraphState.CommitLock"/> held,
    /// by the commit of a node that newly depends on gated nodes, before its value is stored: a pass
    /// that sees the value and stamps a node downstream on the strength of it has then seen the gates
    /// too, and so has every pass that reads that stamp afterwards (gates are read after the stamp).
    /// Rare: a node usually gets its gates when it is first computed, before anything depends on it.
    /// </summary>
    internal void AddGates(NodeHandle[] gates)
    {
        if (gates.Length == 0 || !TryMergeGates(gates))
            return;

        var pending = new Stack<NodeHandle>();
        var dependents = new List<NodeHandle>();
        pending.Push(this);
        while (pending.TryPop(out var handle))
        {
            dependents.Clear();
            handle.State.CopyDependentsTo(dependents);
            foreach (var dependent in dependents)
            {
                if (dependent.TryMergeGates(gates))
                    pending.Push(dependent);
            }
        }
    }

    private bool TryMergeGates(NodeHandle[] gates)
    {
        var current = _gates;

        // Usually the first gates a node gets: share the array (gate arrays are never modified)
        if (current.Length == 0 && Array.IndexOf(gates, this) < 0)
        {
            Volatile.Write(ref _gates, gates);
            return true;
        }

        NodeHandle[]? merged = null;
        foreach (var gate in gates)
        {
            if (gate == this || Array.IndexOf(merged ?? current, gate) >= 0)
                continue;

            merged = [.. merged ?? current, gate];
        }

        if (merged is null)
            return false;

        Volatile.Write(ref _gates, merged);
        return true;
    }

    /// <summary>
    /// The node this handle evaluates.
    /// </summary>
    public abstract IComputationNode Node { get; }

    /// <summary>
    /// Brings the node up to date in this pass (recomputing it if needed) and returns its output version.
    /// </summary>
    public abstract long Refresh(GraphEvaluator evaluator);

    // Node types known to implement exactly one result interface (see EnsureSingleResultInterface)
    private static readonly ConditionalWeakTable<Type, object> s_checkedNodeTypes = new();

    /// <summary>
    /// Returns the handle of <paramref name="node"/>: the one in <see cref="SharedGraphState.Handles"/>,
    /// or a new one created by <paramref name="create"/> (after registering the node) on first use.
    /// Every caller gets the same handle for a node, so the weak reverse edges that reference it
    /// (<see cref="NodeState.AddDependent"/>) stay alive as long as the node does.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The node implements more than one result interface (see <see cref="EnsureSingleResultInterface"/>).
    /// </exception>
    protected static THandle GetOrCreate<THandle>(
        IComputationNode node,
        ComputationGraph graph,
        ConditionalWeakTable<IComputationNode, NodeHandle>.CreateValueCallback create)
        where THandle : NodeHandle
    {
        var handles = graph.SharedState.Handles;
        if (!handles.TryGetValue(node, out var handle))
        {
            EnsureSingleResultInterface(node);
            graph.RegisterNodeIfNeeded(node);
            handle = handles.GetValue(node, create);
        }

        return handle as THandle ?? throw MultipleResultInterfaces(node);
    }

    /// <summary>
    /// Rejects a node that implements more than one of <see cref="IComputationNode{T}"/> and
    /// <see cref="ICollectionComputationNode{T}"/> (or one of them for several <c>T</c>).
    /// </summary>
    /// <remarks>
    /// A node has one ID, so one cached value, one output version and one handle. Reading such a
    /// node through two of its interfaces would need two handles of different result types for the
    /// same node; the second could not be stored in <see cref="SharedGraphState.Handles"/>, so the
    /// reverse edges referencing it (weakly) would disappear with it, and changes would stop reaching
    /// the nodes that read it (issue #73). Such a node is therefore rejected on first use, whichever
    /// interface it is read through.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The node implements more than one result interface.</exception>
    internal static void EnsureSingleResultInterface(IComputationNode node)
    {
        var type = node.GetType();
        if (s_checkedNodeTypes.TryGetValue(type, out _))
            return;

        var count = 0;
        foreach (var implemented in type.GetInterfaces())
        {
            if (implemented.IsGenericType
                && implemented.GetGenericTypeDefinition() is var definition
                && (definition == typeof(IComputationNode<>) || definition == typeof(ICollectionComputationNode<>)))
            {
                count++;
            }
        }

        if (count > 1)
            throw MultipleResultInterfaces(node);

        s_checkedNodeTypes.TryAdd(type, type);
    }

    private static InvalidOperationException MultipleResultInterfaces(IComputationNode node) => new(
        $"Node '{node.Id}' ({node.GetType().Name}) implements more than one of IComputationNode<T> and " +
        "ICollectionComputationNode<T>. A node must produce exactly one kind of result: implement one " +
        "interface and expose the other result through a separate node.");
}

/// <summary>
/// Verification, computation and commit of a node producing <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The node's result type (<c>IEnumerable&lt;T&gt;</c> for collection nodes)</typeparam>
internal abstract class NodeHandle<TResult> : NodeHandle
{
    private readonly bool _isRoot;

    // The dependencies whose dependents this handle has been added to (see RegisterWithDependencies);
    // replaced under SharedGraphState.CommitLock
    private NodeState[] _registeredWith = [];

    protected NodeHandle(IComputationNode node, NodeState state)
        : base(state)
    {
        Id = node.Id;
        _isRoot = node.Dependencies.IsEmpty;
    }

    /// <summary>
    /// The node's ID.
    /// </summary>
    public string Id { get; }

    private KeyIndex? _keyIndex;

    /// <summary>
    /// The per-key readers of this (dictionary) node, created by the first per-key read (see
    /// <see cref="GraphEvaluator.ReadKey"/>); <c>null</c> for every other node. Set under
    /// <see cref="SharedGraphState.CommitLock"/>.
    /// </summary>
    internal KeyIndex? KeyIndex
    {
        get => Volatile.Read(ref _keyIndex);
        set => Volatile.Write(ref _keyIndex, value);
    }

    /// <summary>
    /// Provider type used to tag computation metrics ("value" or "values").
    /// </summary>
    protected abstract string ProviderType { get; }

    protected abstract Result<TResult, ComputationError> ComputeCore(ComputationContext context);

    /// <summary>
    /// Whether a cached object has the type <see cref="Store"/> produces.
    /// </summary>
    protected abstract bool IsStoredValue(object? value);

    /// <summary>
    /// Converts a computed value into the object kept in the cache (never <c>null</c>).
    /// </summary>
    protected abstract object Store(TResult value);

    protected abstract TResult Load(object stored);

    /// <summary>
    /// Compares two stored values with the node's comparer.
    /// </summary>
    protected abstract bool StoredEquals(object previous, object current);

    public override long Refresh(GraphEvaluator evaluator)
    {
        Evaluate(evaluator, out var version);
        return version;
    }

    /// <summary>
    /// Returns the node's up-to-date result and its output version, from the cache if the cached
    /// value is still valid, otherwise by computing (and committing) it.
    /// </summary>
    /// <param name="evaluator">The evaluation pass</param>
    /// <param name="version">The output version of the returned result</param>
    /// <param name="metadata">Metadata of the provider being read, to record computation metrics; <c>null</c> for dependencies</param>
    public Result<TResult, ComputationError> Evaluate(GraphEvaluator evaluator, out long version, ProviderMetadata? metadata = null)
    {
        if (evaluator.TryGetUncachedResult(Id, out Result<TResult, ComputationError> uncached, out version))
        {
            // This result was not cached because it depends on a failed read. A node reading it
            // depends on that failure too, so it must not be cached either: count the hit as a
            // failed read of the reader.
            evaluator.NoteUncachedRead();
            return uncached;
        }

        // Read before verifying: a change that marks the node after this point makes the stamp
        // recorded below stale, so the value is verified again.
        var dirty = State.Dirty;

        if (TryGetEntry(evaluator.Graph, out var entry))
        {
            // Verified since the last source change, or no source upstream has changed since it was
            // (for a gated node, once its gates are up to date: see Gates and StampHoldsAfterGates)
            if ((entry.VerifiedAtRevision >= evaluator.Revision && !evaluator.OverlapsChange)
                || (entry.Stamp == dirty && (Gates.Length == 0 || StampHoldsAfterGates(entry, evaluator, ref dirty))))
            {
                entry.MarkVerified(evaluator.Revision);
                version = entry.OutputVersion;
                return ToResult(entry);
            }

            if (IsUpToDate(entry, evaluator))
            {
                entry.MarkVerified(evaluator.Revision);
                if (evaluator.CanStamp())
                    entry.SetStamp(dirty);

                version = entry.OutputVersion;
                return ToResult(entry);
            }
        }

        return Recompute(evaluator, dirty, entry, out version, metadata);
    }

    /// <summary>
    /// For a gated node whose stamp matched its dirty counter: brings the gates up to date, then
    /// checks the stamp against the counter again (re-read into <paramref name="dirty"/>).
    /// </summary>
    /// <remarks>
    /// Refreshing a gate recomputes the dictionary node if a change upstream of it has marked it, and
    /// the dictionary's commit marks the readers of the keys whose entries changed before it stores
    /// its value (see <see cref="KeyIndex{TKey, TValue}"/>). So once the gates are up to date, every
    /// key change this node has to see has reached its counter, and a stamp that still matches is as
    /// trustworthy as an ungated node's (see the remarks of
    /// <see cref="SharedGraphState.MarkKeysChanged"/> for the argument).
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool StampHoldsAfterGates(CachedNodeValue entry, GraphEvaluator evaluator, ref long dirty)
    {
        foreach (var gate in Gates)
            gate.Refresh(evaluator);

        dirty = State.Dirty;
        return entry.Stamp == dirty;
    }

    private bool IsUpToDate(CachedNodeValue entry, GraphEvaluator evaluator)
    {
        // A root's version does not tell whether a change in progress has published its new
        // contents (see GraphEvaluator.OverlapsChange)
        if (_isRoot)
            return !evaluator.OverlapsChange && entry.OutputVersion == State.Version;

        foreach (var dependency in entry.Dependencies)
        {
            long current;
            if (dependency.Handle is null)
            {
                if (evaluator.OverlapsChange)
                    return false; // a root (see above): recompute from its current contents

                current = dependency.State.Version;
            }
            else if (dependency.Handle.TryGetTarget(out var handle))
            {
                current = handle.Refresh(evaluator);
            }
            else
            {
                // The dependency no longer exists (only possible for a value whose node is gone too)
                return false;
            }

            if (current != dependency.Version)
                return false;
        }

        return true;
    }

    private Result<TResult, ComputationError> Recompute(GraphEvaluator evaluator, long dirty, CachedNodeValue? previous, out long version, ProviderMetadata? metadata)
    {
        // A root's version is read before its value: a change racing with the read leaves the
        // value marked with the older version, so it is recomputed on the next read.
        var versionBefore = _isRoot ? State.Version : 0;

        var frame = evaluator.BeginFrame();
        var failedReadsBefore = evaluator.FailedReads;
        Result<TResult, ComputationError> result;
        DependencyVersion[] dependencies;
        GraphEvaluator.EnterComputation();
        try
        {
            result = metadata is { } providerMetadata
                ? ComputationMetrics.Measure(
                    Node.GetType().Name,
                    providerMetadata,
                    ProviderType,
                    static (handle, context) => handle.ComputeCore(context),
                    this,
                    evaluator.Context)
                : ComputeCore(evaluator.Context);
        }
        finally
        {
            GraphEvaluator.ExitComputation();
            dependencies = evaluator.EndFrame(frame);
        }

        var cacheable = evaluator.FailedReads == failedReadsBefore;
        return Commit(evaluator, result, dependencies, versionBefore, dirty, previous, cacheable, out version);
    }

    /// <summary>
    /// Stores a computed result with its version information. Values (including <c>null</c>),
    /// "no value" and errors are all cached. A computed node's version is incremented only if the
    /// result differs from the cached one: values by the node's comparer, errors by
    /// <see cref="CachedNodeValue.ErrorsEqual"/>, <c>null</c> equal only to <c>null</c>. A root keeps
    /// the version it had before it was read (root versions are advanced by
    /// <see cref="ComputationGraph.MarkSourceChanged"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node is added to the dependents of what it read before the stamp is decided: a change
    /// whose marking missed the new edge started before that point, so
    /// <see cref="GraphEvaluator.CanStamp"/> is false and the value is stored without a stamp.
    /// </para>
    /// <para>
    /// An error is cached on the same terms as a value: with the versions of the dependencies its
    /// computation read before it failed, and validated by the same dirty marking, stamps and
    /// revisions. That is sound because a computation is a function of what it reads: run again with
    /// the same versions of the same dependencies it reads the same values in the same order and
    /// fails at the same point, and the dependencies it did not get to read cannot affect it. The one
    /// exception is a dependency read that throws (a framework failure, not a lambda's): it records
    /// no dependency, so such a result is not cached (<paramref name="cacheable"/> is
    /// <see langword="false"/>, see <see cref="GraphEvaluator.FailedReads"/>). It then always counts
    /// as a change and is kept for the rest of the pass only.
    /// </para>
    /// </remarks>
    private Result<TResult, ComputationError> Commit(
        GraphEvaluator evaluator,
        Result<TResult, ComputationError> result,
        DependencyVersion[] dependencies,
        long versionBefore,
        long dirty,
        CachedNodeValue? previous,
        bool cacheable,
        out long version)
    {
        var graph = evaluator.Graph;
        var state = evaluator.State;
        var kind = result.IsSuccess ? CachedResultKind.Value : result.IsNone ? CachedResultKind.None : CachedResultKind.Error;
        var error = result.ErrorValue;

        // Materialize outside the lock (a null value is cached as null)
        var stored = result.IsSuccess && result.ValueOrDefault is { } value ? Store(value) : null;

        lock (state.CommitLock)
        {
            // Also for results that are not cached: changes must reach the nodes downstream of this one
            RegisterWithDependencies(dependencies);

            if (!cacheable)
            {
                version = _isRoot ? versionBefore : State.IncrementVersion();

                // Before the value is removed, like a cached value is stored after it (see below)
                KeyIndex?.OnCommit(state, kind, stored, cacheable: false, version, changed: true);
                graph.ValueCache.Remove(Id);
            }
            else
            {
                if (_isRoot)
                {
                    version = versionBefore;
                }
                else
                {
                    // `previous` is the entry this pass found before computing. If another commit
                    // replaced it meanwhile with a different value, the version moved on and the
                    // result counts as changed; at worst that costs downstream an extra recompute.
                    var current = State.Version;
                    var unchanged = previous is not null
                        && previous.OutputVersion == current
                        && previous.Kind == kind
                        && kind switch
                        {
                            CachedResultKind.None => true,
                            CachedResultKind.Error => CachedNodeValue.ErrorsEqual(previous.Error!, error!),
                            _ => previous.Value is null
                                ? stored is null
                                : stored is not null && StoredEquals(previous.Value, stored),
                        };

                    version = unchanged ? current : State.IncrementVersion();

                    // A dictionary node with per-key readers marks the readers of the changed keys
                    // before its value is stored, so a pass that finds the value up to date finds
                    // those readers marked (see KeyIndex)
                    KeyIndex?.OnCommit(state, kind, stored, cacheable: true, version, changed: !unchanged);
                }

                var stamp = evaluator.CanStamp() ? dirty : CachedNodeValue.NoStamp;
                graph.ValueCache.Set(Id, new CachedNodeValue(kind, stored, error, version, dependencies, evaluator.Revision, stamp));
            }
        }

        if (!cacheable)
        {
            evaluator.SetUncachedResult(Id, result, version);
            return result;
        }

        return stored is null ? result : Result<TResult, ComputationError>.Success(Load(stored));
    }

    /// <summary>
    /// Adds this node to the dependents of each dependency it has not been added to yet, so that
    /// source changes mark it dirty. Called with <see cref="SharedGraphState.CommitLock"/> held.
    /// </summary>
    private void RegisterWithDependencies(DependencyVersion[] dependencies)
    {
        foreach (var dependency in dependencies)
        {
            var dependencyState = dependency.State;
            if (Array.IndexOf(_registeredWith, dependencyState) >= 0)
                continue;

            dependencyState.AddDependent(WeakSelf);
            _registeredWith = [.. _registeredWith, dependencyState];

            // A dependency's later gates reach this node through AddGates (it is a dependent now)
            if (dependency.Handle is { } weak && weak.TryGetTarget(out var handle))
                AddGates(handle.Gates);
        }
    }

    private bool TryGetEntry(ComputationGraph graph, out CachedNodeValue entry)
    {
        if (graph.ValueCache.TryGet(Id, out var cached)
            && cached is CachedNodeValue typed
            && (!typed.HasValue || (typed.Value is null ? default(TResult) is null : IsStoredValue(typed.Value))))
        {
            entry = typed;
            return true;
        }

        entry = null!;
        return false;
    }

    private Result<TResult, ComputationError> ToResult(CachedNodeValue entry) => entry.Kind switch
    {
        CachedResultKind.None => Result<TResult, ComputationError>.None,
        CachedResultKind.Error => Result<TResult, ComputationError>.Error(entry.Error!),
        _ => Result<TResult, ComputationError>.Success(entry.Value is null ? default! : Load(entry.Value)),
    };
}

/// <summary>
/// Handle of a single-value node; compares values with the node's comparer
/// (<see cref="IHasComparer{T}"/>) or <see cref="DefaultEqualityComparer{T}.Instance"/>.
/// </summary>
internal sealed class ValueNodeHandle<T> : NodeHandle<T>
{
    private readonly IComputationNode<T> _node;
    private readonly IEqualityComparer<T> _comparer;

    private ValueNodeHandle(IComputationNode<T> node, NodeState state)
        : base(node, state)
    {
        _node = node;
        _comparer = node is IHasComparer<T> withComparer ? withComparer.Comparer : DefaultEqualityComparer<T>.Instance;
    }

    public override IComputationNode Node => _node;

    protected override string ProviderType => "value";

    /// <summary>
    /// Returns the handle of <paramref name="node"/>, creating (and registering the node) on first use.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The node also implements another result interface (see <see cref="NodeHandle.EnsureSingleResultInterface"/>).
    /// </exception>
    public static ValueNodeHandle<T> For(IComputationNode<T> node, ComputationGraph graph)
    {
        if (graph.SharedState.Handles.TryGetValue(node, out var existing) && existing is ValueNodeHandle<T> typed)
            return typed;

        var versions = graph.SharedState.Versions;
        return GetOrCreate<ValueNodeHandle<T>>(node, graph, n => new ValueNodeHandle<T>((IComputationNode<T>)n, versions.GetState(n.Id)));
    }

    protected override Result<T, ComputationError> ComputeCore(ComputationContext context) => _node.Compute(context);

    protected override bool IsStoredValue(object? value) => value is T;

    protected override object Store(T value) => value!;

    protected override T Load(object stored) => (T)stored;

    protected override bool StoredEquals(object previous, object current)
    {
        if (previous is not T previousValue)
            return false;

        // Collect() decides from its input's delta when it can (see Pipeline/ArrayDelta.cs)
        if (_node is IDeltaComparable<T> deltaComparable && deltaComparable.TryEquals(previousValue, (T)current, out var equal))
            return equal;

        return _comparer.Equals(previousValue, (T)current);
    }
}

/// <summary>
/// Handle of a collection node; caches the values as a materialized array and compares arrays
/// element-wise with the node's comparer (<see cref="IHasComparer{T}"/>) or
/// <see cref="DefaultEqualityComparer{T}.Instance"/>.
/// </summary>
internal sealed class CollectionNodeHandle<T> : NodeHandle<IEnumerable<T>>
{
    private readonly ICollectionComputationNode<T> _node;
    private readonly IEqualityComparer<T> _comparer;

    private CollectionNodeHandle(ICollectionComputationNode<T> node, NodeState state)
        : base(node, state)
    {
        _node = node;
        _comparer = node is IHasComparer<T> withComparer ? withComparer.Comparer : DefaultEqualityComparer<T>.Instance;
    }

    public override IComputationNode Node => _node;

    protected override string ProviderType => "values";

    /// <summary>
    /// Returns the handle of <paramref name="node"/>, creating (and registering the node) on first use.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The node also implements another result interface (see <see cref="NodeHandle.EnsureSingleResultInterface"/>).
    /// </exception>
    public static CollectionNodeHandle<T> For(ICollectionComputationNode<T> node, ComputationGraph graph)
    {
        if (graph.SharedState.Handles.TryGetValue(node, out var existing) && existing is CollectionNodeHandle<T> typed)
            return typed;

        var versions = graph.SharedState.Versions;
        return GetOrCreate<CollectionNodeHandle<T>>(node, graph, n => new CollectionNodeHandle<T>((ICollectionComputationNode<T>)n, versions.GetState(n.Id)));
    }

    protected override Result<IEnumerable<T>, ComputationError> ComputeCore(ComputationContext context) => _node.Compute(context);

    protected override bool IsStoredValue(object? value) => value is T[];

    protected override object Store(IEnumerable<T> value) => value as T[] ?? value.ToArray();

    protected override IEnumerable<T> Load(object stored) => (T[])stored;

    protected override bool StoredEquals(object previous, object current)
    {
        if (previous is not T[] a)
            return false;

        var b = (T[])current;
        if (ReferenceEquals(a, b))
            return true;

        if (a.Length != b.Length)
            return false;

        // When the node recorded how `a` became `b`, only the replaced items need comparing
        // (see Pipeline/ArrayDelta.cs)
        if (ArrayDelta.Get(_node, a, b) is { } delta && ArrayDelta.TryEquals(delta, a, b, _comparer, out var equal))
            return equal;

        for (var i = 0; i < a.Length; i++)
        {
            if (!_comparer.Equals(a[i], b[i]))
                return false;
        }

        return true;
    }
}
