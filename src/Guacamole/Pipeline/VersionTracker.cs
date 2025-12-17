using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Guacamole.Core.Pipeline;

/// <summary>
/// Thread-safe tracker for node output versions.
/// Shared across all graph versions to maintain version continuity.
///
/// This enables equality-based cache skipping: when a node computes the same
/// value as before, its version doesn't increment, so downstream nodes can
/// detect their dependencies haven't changed and skip recomputation.
/// </summary>
public sealed class VersionTracker
{
    private readonly ConcurrentDictionary<string, long> _outputVersions = new();

    /// <summary>
    /// Gets the current output version for a node.
    /// Returns 0 if the node has never been computed.
    /// </summary>
    /// <param name="nodeId">The node's unique identifier</param>
    /// <returns>Current version number (0 if never computed)</returns>
    public long GetOutputVersion(string nodeId) =>
        _outputVersions.GetOrAdd(nodeId, _ => 0);

    /// <summary>
    /// Increments the output version for a node and returns the new version.
    /// Called when a node's computed value has actually changed.
    /// </summary>
    /// <param name="nodeId">The node's unique identifier</param>
    /// <returns>The new version number after incrementing</returns>
    public long IncrementOutputVersion(string nodeId) =>
        _outputVersions.AddOrUpdate(nodeId, 1, (_, v) => v + 1);

    /// <summary>
    /// Creates a snapshot of current output versions for a set of dependencies.
    /// Used when caching a computed value to track which dependency versions
    /// were used at computation time.
    /// </summary>
    /// <param name="dependencies">Set of dependency node IDs</param>
    /// <returns>Immutable dictionary mapping node IDs to their current versions</returns>
    public ImmutableDictionary<string, long> SnapshotDependencyVersions(
        ImmutableHashSet<string> dependencies)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, long>();
        foreach (var depId in dependencies)
        {
            builder[depId] = GetOutputVersion(depId);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Resets version tracking (primarily for testing).
    /// </summary>
    public void Clear() => _outputVersions.Clear();

    /// <summary>
    /// Gets the number of nodes being tracked (primarily for diagnostics).
    /// </summary>
    public int TrackedNodeCount => _outputVersions.Count;
}
