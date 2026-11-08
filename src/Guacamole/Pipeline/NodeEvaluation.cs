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
        Context = new ComputationContext(NoCachedValues) { Evaluator = this };
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
        Result<T, ComputationError> result;
        long version;
        WeakReference<NodeHandle>? handle = null;

        if (node.Dependencies.IsEmpty)
        {
            version = State.Versions.GetOutputVersion(node.Id);
            result = node.Compute(Context);
        }
        else
        {
            var typed = ValueNodeHandle<T>.For(node, Graph);
            handle = typed.WeakSelf;
            result = typed.Evaluate(this, out version);
        }

        _reads.Add(new DependencyVersion(node.Id, version, handle));
        return result;
    }

    /// <summary>
    /// Reads a collection dependency of the node being computed.
    /// </summary>
    public Result<IEnumerable<T>, ComputationError> Read<T>(ICollectionComputationNode<T> node)
    {
        Result<IEnumerable<T>, ComputationError> result;
        long version;
        WeakReference<NodeHandle>? handle = null;

        if (node.Dependencies.IsEmpty)
        {
            version = State.Versions.GetOutputVersion(node.Id);
            result = node.Compute(Context);
        }
        else
        {
            var typed = CollectionNodeHandle<T>.For(node, Graph);
            handle = typed.WeakSelf;
            result = typed.Evaluate(this, out version);
        }

        _reads.Add(new DependencyVersion(node.Id, version, handle));
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
    protected NodeHandle()
    {
        WeakSelf = new WeakReference<NodeHandle>(this);
    }

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
}

/// <summary>
/// Verification, computation and commit of a node producing <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The node's result type (<c>IEnumerable&lt;T&gt;</c> for collection nodes)</typeparam>
internal abstract class NodeHandle<TResult> : NodeHandle
{
    private readonly bool _isRoot;

    protected NodeHandle(IComputationNode node)
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

        if (TryGetEntry(evaluator.Graph, out var entry)
            && (entry.VerifiedAtRevision >= evaluator.Revision || IsUpToDate(entry, evaluator)))
        {
            entry.MarkVerified(evaluator.Revision);
            version = entry.OutputVersion;
            return ToResult(entry);
        }

        return Recompute(evaluator, out version, metadata);
    }

    private bool IsUpToDate(CachedNodeValue entry, GraphEvaluator evaluator)
    {
        var versions = evaluator.State.Versions;
        if (_isRoot)
            return entry.OutputVersion == versions.GetOutputVersion(Id);

        foreach (var dependency in entry.Dependencies)
        {
            long current;
            if (dependency.Handle is null)
            {
                current = versions.GetOutputVersion(dependency.Id);
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

    private Result<TResult, ComputationError> Recompute(GraphEvaluator evaluator, out long version, ProviderMetadata? metadata)
    {
        // A root's version is read before its value: a change racing with the read leaves the
        // value marked with the older version, so it is recomputed on the next read.
        var versionBefore = _isRoot ? evaluator.State.Versions.GetOutputVersion(Id) : 0;

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

        return Commit(evaluator, result, dependencies, versionBefore, out version);
    }

    /// <summary>
    /// Stores a computed result with its version information. A computed node's version is
    /// incremented only if the result differs from the cached one; errors and <c>null</c> values
    /// are not cached and always count as a change. A root keeps the version it had before it was
    /// read (root versions are advanced by <see cref="ComputationGraph.MarkSourceChanged"/>).
    /// </summary>
    private Result<TResult, ComputationError> Commit(
        GraphEvaluator evaluator,
        Result<TResult, ComputationError> result,
        DependencyVersion[] dependencies,
        long versionBefore,
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
            if (!cacheable)
            {
                graph.ValueCache.Remove(Id);
                version = _isRoot ? versionBefore : state.Versions.IncrementOutputVersion(Id);
            }
            else
            {
                if (_isRoot)
                {
                    version = versionBefore;
                }
                else
                {
                    var current = state.Versions.GetOutputVersion(Id);
                    var unchanged = TryGetEntry(graph, out var previous)
                        && previous.OutputVersion == current
                        && previous.IsNone == result.IsNone
                        && (result.IsNone || StoredEquals(previous.Value!, stored!));

                    version = unchanged ? current : state.Versions.IncrementOutputVersion(Id);
                }

                graph.ValueCache.Set(Id, new CachedNodeValue(stored, result.IsNone, version, dependencies, evaluator.Revision));
            }
        }

        if (!cacheable)
        {
            evaluator.SetUncachedResult(Id, result, version);
            return result;
        }

        return stored is null ? result : Result<TResult, ComputationError>.Success(Load(stored));
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
/// (<see cref="IHasComparer{T}"/>) or <see cref="EqualityComparer{T}.Default"/>.
/// </summary>
internal sealed class ValueNodeHandle<T> : NodeHandle<T>
{
    private readonly IComputationNode<T> _node;
    private readonly IEqualityComparer<T> _comparer;

    private ValueNodeHandle(IComputationNode<T> node)
        : base(node)
    {
        _node = node;
        _comparer = node is IHasComparer<T> withComparer ? withComparer.Comparer : EqualityComparer<T>.Default;
    }

    public override IComputationNode Node => _node;

    protected override string ProviderType => "value";

    /// <summary>
    /// Returns the handle of <paramref name="node"/>, creating (and registering the node) on first use.
    /// </summary>
    public static ValueNodeHandle<T> For(IComputationNode<T> node, ComputationGraph graph)
    {
        var handles = graph.SharedState.Handles;
        if (handles.TryGetValue(node, out var existing) && existing is ValueNodeHandle<T> typed)
            return typed;

        graph.RegisterNodeIfNeeded(node);
        return handles.GetValue(node, s_create) as ValueNodeHandle<T>
            ?? new ValueNodeHandle<T>(node); // a node implementing several node interfaces: not shared
    }

    private static readonly ConditionalWeakTable<IComputationNode, NodeHandle>.CreateValueCallback s_create =
        static node => new ValueNodeHandle<T>((IComputationNode<T>)node);

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
/// <see cref="EqualityComparer{T}.Default"/>.
/// </summary>
internal sealed class CollectionNodeHandle<T> : NodeHandle<IEnumerable<T>>
{
    private readonly ICollectionComputationNode<T> _node;
    private readonly IEqualityComparer<T> _comparer;

    private CollectionNodeHandle(ICollectionComputationNode<T> node)
        : base(node)
    {
        _node = node;
        _comparer = node is IHasComparer<T> withComparer ? withComparer.Comparer : EqualityComparer<T>.Default;
    }

    public override IComputationNode Node => _node;

    protected override string ProviderType => "values";

    /// <summary>
    /// Returns the handle of <paramref name="node"/>, creating (and registering the node) on first use.
    /// </summary>
    public static CollectionNodeHandle<T> For(ICollectionComputationNode<T> node, ComputationGraph graph)
    {
        var handles = graph.SharedState.Handles;
        if (handles.TryGetValue(node, out var existing) && existing is CollectionNodeHandle<T> typed)
            return typed;

        graph.RegisterNodeIfNeeded(node);
        return handles.GetValue(node, s_create) as CollectionNodeHandle<T>
            ?? new CollectionNodeHandle<T>(node); // a node implementing several node interfaces: not shared
    }

    private static readonly ConditionalWeakTable<IComputationNode, NodeHandle>.CreateValueCallback s_create =
        static node => new CollectionNodeHandle<T>((ICollectionComputationNode<T>)node);

    protected override Result<IEnumerable<T>, ComputationError> ComputeCore(ComputationContext context) => _node.Compute(context);

    protected override bool IsStoredValue(object? value) => value is T[];

    protected override object Store(IEnumerable<T> value) => value as T[] ?? value.ToArray();

    protected override IEnumerable<T> Load(object stored) => (T[])stored;

    protected override bool StoredEquals(object previous, object current)
    {
        if (previous is not T[] a)
            return false;

        var b = (T[])current;
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
