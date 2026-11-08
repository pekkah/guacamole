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

    // Results that are not cached (errors, null values), so that a node is computed at most once
    // per pass even when it is both verified and read.
    private Dictionary<string, (object Result, long Version)>? _uncachedResults;

    public GraphEvaluator(ComputationGraph graph)
    {
        Graph = graph;
        State = graph.SharedState;
        Revision = State.Revision;
        _changeState = State.ChangeState;
        Context = new ComputationContext(NoCachedValues) { Evaluator = this };
    }

    // The source-change state when this pass started (read after Revision)
    private readonly long _changeState;

    /// <summary>
    /// Whether this pass may record stamps (<see cref="CachedNodeValue.Stamp"/>): no source change
    /// was in progress when it started, and none has started since. Read after the verification or
    /// computation whose result would be stamped.
    /// </summary>
    /// <remarks>
    /// A stamp says that no change has reached the node since its value was verified. That holds only
    /// if every change already counted by the node's dirty counter had finished marking all the nodes
    /// it reaches (after incrementing its source's version) before the verification looked at them:
    /// a change still in progress may have marked this node but not yet the nodes between it and the
    /// source. Passes overlapping a change still verify correctly; they just do not record stamps.
    /// </remarks>
    public bool CanStamp =>
        (_changeState & SharedGraphState.ActiveChangesMask) == 0 && State.ChangeState == _changeState;

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

    /// <summary>
    /// Reads a collection dependency of the node being computed.
    /// </summary>
    public Result<IEnumerable<T>, ComputationError> Read<T>(ICollectionComputationNode<T> node)
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
            return uncached;

        // Read before verifying: a change that marks the node after this point makes the stamp
        // recorded below stale, so the value is verified again.
        var dirty = State.Dirty;

        if (TryGetEntry(evaluator.Graph, out var entry))
        {
            // Verified since the last source change, or no source upstream has changed since it was
            if (entry.VerifiedAtRevision >= evaluator.Revision || entry.Stamp == dirty)
            {
                entry.MarkVerified(evaluator.Revision);
                version = entry.OutputVersion;
                return ToResult(entry);
            }

            if (IsUpToDate(entry, evaluator))
            {
                entry.MarkVerified(evaluator.Revision);
                if (evaluator.CanStamp)
                    entry.SetStamp(dirty);

                version = entry.OutputVersion;
                return ToResult(entry);
            }
        }

        return Recompute(evaluator, dirty, entry, out version, metadata);
    }

    private bool IsUpToDate(CachedNodeValue entry, GraphEvaluator evaluator)
    {
        if (_isRoot)
            return entry.OutputVersion == State.Version;

        foreach (var dependency in entry.Dependencies)
        {
            long current;
            if (dependency.Handle is null)
            {
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

        return Commit(evaluator, result, dependencies, versionBefore, dirty, previous, out version);
    }

    /// <summary>
    /// Stores a computed result with its version information. A computed node's version is
    /// incremented only if the result differs from the cached one; errors and <c>null</c> values
    /// are not cached and always count as a change. A root keeps the version it had before it was
    /// read (root versions are advanced by <see cref="ComputationGraph.MarkSourceChanged"/>).
    /// </summary>
    /// <remarks>
    /// The node is added to the dependents of what it read before the stamp is decided: a change
    /// whose marking missed the new edge started before that point, so
    /// <see cref="GraphEvaluator.CanStamp"/> is false and the value is stored without a stamp.
    /// </remarks>
    private Result<TResult, ComputationError> Commit(
        GraphEvaluator evaluator,
        Result<TResult, ComputationError> result,
        DependencyVersion[] dependencies,
        long versionBefore,
        long dirty,
        CachedNodeValue? previous,
        out long version)
    {
        var graph = evaluator.Graph;
        var state = evaluator.State;
        var hasValue = result.IsSuccess && result.Value is not null;
        var cacheable = hasValue || result.IsNone;

        // Materialize outside the lock
        var stored = hasValue ? Store(result.Value!) : null;

        lock (state.CommitLock)
        {
            // Also for results that are not cached: changes must reach the nodes downstream of this one
            RegisterWithDependencies(dependencies);

            if (!cacheable)
            {
                graph.ValueCache.Remove(Id);
                version = _isRoot ? versionBefore : State.IncrementVersion();
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
                        && previous.IsNone == result.IsNone
                        && (result.IsNone || StoredEquals(previous.Value!, stored!));

                    version = unchanged ? current : State.IncrementVersion();
                }

                var stamp = evaluator.CanStamp ? dirty : CachedNodeValue.NoStamp;
                graph.ValueCache.Set(Id, new CachedNodeValue(stored, result.IsNone, version, dependencies, evaluator.Revision, stamp));
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
        }
    }

    private bool TryGetEntry(ComputationGraph graph, out CachedNodeValue entry)
    {
        if (graph.ValueCache.TryGet(Id, out var cached)
            && cached is CachedNodeValue typed
            && (typed.IsNone || IsStoredValue(typed.Value)))
        {
            entry = typed;
            return true;
        }

        entry = null!;
        return false;
    }

    private Result<TResult, ComputationError> ToResult(CachedNodeValue entry) => entry.IsNone
        ? Result<TResult, ComputationError>.None
        : Result<TResult, ComputationError>.Success(Load(entry.Value!));
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

    protected override bool StoredEquals(object previous, object current) =>
        previous is T previousValue && _comparer.Equals(previousValue, (T)current);
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

        for (var i = 0; i < a.Length; i++)
        {
            if (!_comparer.Equals(a[i], b[i]))
                return false;
        }

        return true;
    }
}
