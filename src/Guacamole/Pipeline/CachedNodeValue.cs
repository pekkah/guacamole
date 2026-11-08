namespace Guacamole.Pipeline;

/// <summary>
/// The version of a dependency as it was read while computing a node.
/// </summary>
/// <param name="State">The dependency's change-tracking state (ID, output version, dependents)</param>
/// <param name="Version">The dependency's output version the computation saw</param>
/// <param name="Handle">
/// Evaluates the dependency when the reading node is verified; <c>null</c> for a root node
/// (a source), whose version is advanced by <see cref="ComputationGraph.MarkSourceChanged"/>
/// and is compared directly. Held weakly, so a cached value does not keep the nodes it was
/// computed from alive (the reading node does, as long as it is alive itself).
/// </param>
internal readonly record struct DependencyVersion(NodeState State, long Version, WeakReference<NodeHandle>? Handle)
{
    /// <summary>
    /// The dependency's node ID.
    /// </summary>
    public string Id => State.Id;
}

/// <summary>
/// What a <see cref="CachedNodeValue"/> holds.
/// </summary>
internal enum CachedResultKind : byte
{
    /// <summary>
    /// A computed value (possibly <c>null</c>), in <see cref="CachedNodeValue.Value"/>.
    /// </summary>
    Value = 0,

    /// <summary>
    /// No value and no error (a <c>Where</c> rejected its input).
    /// </summary>
    None = 1,

    /// <summary>
    /// A failed computation, in <see cref="CachedNodeValue.Error"/>.
    /// </summary>
    Error = 2,
}

/// <summary>
/// A node's cached result together with the version information it was computed with. Stored as a
/// single object in <see cref="ComputationGraph.ValueCache"/>, so a result and its versions are
/// always read and replaced together.
/// </summary>
/// <remarks>
/// Every kind of result is cached: values (including <c>null</c>), "no value" and errors. They take
/// part in change detection the same way, so a failing stage runs once per change of its inputs like
/// any other stage, and an error that recomputes to an equal error (see <see cref="ErrorsEqual"/>)
/// keeps the stages downstream of it cached.
/// </remarks>
internal sealed class CachedNodeValue
{
    /// <summary>
    /// A <see cref="Stamp"/> that matches no dirty counter.
    /// </summary>
    public const long NoStamp = -1;

    private long _verifiedAtRevision;
    private long _stamp;
    private object? _view;

    public CachedNodeValue(
        CachedResultKind kind,
        object? value,
        ComputationError? error,
        long outputVersion,
        DependencyVersion[] dependencies,
        long verifiedAtRevision,
        long stamp = NoStamp)
    {
        Kind = kind;
        Value = value;
        Error = error;
        OutputVersion = outputVersion;
        Dependencies = dependencies;
        _verifiedAtRevision = verifiedAtRevision;
        _stamp = stamp;
    }

    /// <summary>
    /// What this entry holds: a value, no value, or an error. The explicit marker lets a <c>null</c>
    /// value be cached like any other value.
    /// </summary>
    public CachedResultKind Kind { get; }

    /// <summary>
    /// The cached value (a <c>T</c> for value nodes, a materialized <c>T[]</c> for collection
    /// nodes) when <see cref="HasValue"/>, possibly <c>null</c>; otherwise <c>null</c>.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Whether this entry holds a computed value (possibly <c>null</c>).
    /// </summary>
    public bool HasValue => Kind == CachedResultKind.Value;

    /// <summary>
    /// The node produced no value (a <c>Where</c> rejected its input). "No value" is cached like a
    /// value, so it takes part in change detection.
    /// </summary>
    public bool IsNone => Kind == CachedResultKind.None;

    /// <summary>
    /// The error of a failed computation when <see cref="Kind"/> is <see cref="CachedResultKind.Error"/>,
    /// otherwise <c>null</c>. Only the <see cref="ComputationError"/> is kept, which holds messages,
    /// not the exceptions they came from, so a cached error does not keep exception objects (or the
    /// state their stack traces and data reference) alive.
    /// </summary>
    public ComputationError? Error { get; }

    /// <summary>
    /// Whether two errors count as equal for the equality cut-off: by <see cref="ComputationError"/>'s
    /// value equality (its runtime type, <see cref="ComputationError.Message"/>, the chain of
    /// <see cref="ComputationError.InnerError"/>s, <see cref="ComputationError.SourceLocation"/> and
    /// <see cref="ComputationError.DiagnosticId"/>).
    /// </summary>
    /// <remarks>
    /// That is everything a consumer can observe of an error. For an error built from an exception
    /// (<see cref="ComputationError.FromException"/>, which the operators use for exceptions thrown by
    /// lambdas), this compares the messages of the exception and of its inner exceptions. The
    /// exception types are not recorded in the error, so two exceptions of different types with the
    /// same messages give equal errors; no consumer can tell those apart either.
    /// </remarks>
    public static bool ErrorsEqual(ComputationError previous, ComputationError current) =>
        ReferenceEquals(previous, current) || EqualityComparer<ComputationError>.Default.Equals(previous, current);

    /// <summary>
    /// The node's output version for this result. Incremented only when the computed result differs
    /// from the previous one (values by the node's comparer, errors by <see cref="ErrorsEqual"/>).
    /// </summary>
    public long OutputVersion { get; }

    /// <summary>
    /// The dependencies the computation read, in read order, with the versions it saw.
    /// </summary>
    public DependencyVersion[] Dependencies { get; }

    /// <summary>
    /// The latest graph revision (see <see cref="SharedGraphState.Revision"/>) at which this value
    /// was known to be up to date. While no source has changed since, the value is valid without
    /// checking its dependencies.
    /// </summary>
    public long VerifiedAtRevision => Volatile.Read(ref _verifiedAtRevision);

    /// <summary>
    /// The node's dirty counter (<see cref="NodeState.Dirty"/>) this value is known to be up to
    /// date with, or <see cref="NoStamp"/>. While the node's counter still has this value, no
    /// source upstream of the node has changed since the value was verified, so it is valid
    /// without checking its dependencies.
    /// </summary>
    /// <remarks>
    /// A stamp is only recorded by an evaluation pass during which no source change was in
    /// progress (see <see cref="GraphEvaluator.CanStamp"/>). That guarantees that every change the
    /// stamp counts had finished marking the nodes it reaches before the value was verified.
    /// </remarks>
    public long Stamp => Volatile.Read(ref _stamp);

    /// <summary>
    /// A read-only view of <see cref="Value"/> handed to public callers (see
    /// <see cref="IncrementalValuesProvider{T}.GetValues"/>), created on first use and kept with
    /// the value so that reading an unchanged value does not allocate. Racing readers may each
    /// create one; any of them is a valid view.
    /// </summary>
    public object? View
    {
        get => Volatile.Read(ref _view);
        set => Volatile.Write(ref _view, value);
    }

    /// <summary>
    /// Records that the value was verified to be up to date at <paramref name="revision"/>.
    /// </summary>
    public void MarkVerified(long revision)
    {
        // A racing writer may store an older revision; that only causes an extra verification.
        if (revision > Volatile.Read(ref _verifiedAtRevision))
            Volatile.Write(ref _verifiedAtRevision, revision);
    }

    /// <summary>
    /// Records that the value is up to date with the node's dirty counter value
    /// <paramref name="stamp"/>. Only for passes where <see cref="GraphEvaluator.CanStamp"/> held
    /// after the verification.
    /// </summary>
    public void SetStamp(long stamp)
    {
        // A racing writer may store an older stamp (which is still correct for the counter value it
        // names); that only causes an extra verification.
        Volatile.Write(ref _stamp, stamp);
    }
}
