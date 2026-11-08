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
/// A node's cached result together with the version information it was computed with. Stored as a
/// single object in <see cref="ComputationGraph.ValueCache"/>, so a value and its versions are
/// always read and replaced together.
/// </summary>
internal sealed class CachedNodeValue
{
    /// <summary>
    /// A <see cref="Stamp"/> that matches no dirty counter.
    /// </summary>
    public const long NoStamp = -1;

    private long _verifiedAtRevision;
    private long _stamp;

    public CachedNodeValue(
        object? value,
        bool isNone,
        long outputVersion,
        DependencyVersion[] dependencies,
        long verifiedAtRevision,
        long stamp = NoStamp)
    {
        Value = value;
        IsNone = isNone;
        OutputVersion = outputVersion;
        Dependencies = dependencies;
        _verifiedAtRevision = verifiedAtRevision;
        _stamp = stamp;
    }

    /// <summary>
    /// The cached value (a <c>T</c> for value nodes, a materialized <c>T[]</c> for collection
    /// nodes); <c>null</c> when <see cref="IsNone"/>.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// The node produced no value (a <c>Where</c> rejected its input). "No value" is cached like a
    /// value, so it takes part in change detection.
    /// </summary>
    public bool IsNone { get; }

    /// <summary>
    /// The node's output version for this value. Incremented only when the computed value differs
    /// from the previous one (using the node's comparer).
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
