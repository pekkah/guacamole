namespace Guacamole.Pipeline;

/// <summary>
/// The version of a dependency as it was read while computing a node.
/// </summary>
/// <param name="Id">The dependency's node ID</param>
/// <param name="Version">The dependency's output version the computation saw</param>
/// <param name="Handle">
/// Evaluates the dependency when the reading node is verified; <c>null</c> for a root node
/// (a source), whose version is advanced by <see cref="ComputationGraph.MarkSourceChanged"/>
/// and is compared directly.
/// </param>
internal readonly record struct DependencyVersion(string Id, long Version, NodeHandle? Handle);

/// <summary>
/// A node's cached result together with the version information it was computed with. Stored as a
/// single object in <see cref="ComputationGraph.ValueCache"/>, so a value and its versions are
/// always read and replaced together.
/// </summary>
internal sealed class CachedNodeValue
{
    private long _verifiedAtRevision;

    public CachedNodeValue(
        object? value,
        bool isNone,
        long outputVersion,
        DependencyVersion[] dependencies,
        long verifiedAtRevision)
    {
        Value = value;
        IsNone = isNone;
        OutputVersion = outputVersion;
        Dependencies = dependencies;
        _verifiedAtRevision = verifiedAtRevision;
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
    /// Records that the value was verified to be up to date at <paramref name="revision"/>.
    /// </summary>
    public void MarkVerified(long revision)
    {
        // A racing writer may store an older revision; that only causes an extra verification.
        if (revision > Volatile.Read(ref _verifiedAtRevision))
            Volatile.Write(ref _verifiedAtRevision, revision);
    }
}
