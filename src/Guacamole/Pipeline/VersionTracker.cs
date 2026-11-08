using System.Collections.Concurrent;

namespace Guacamole.Pipeline;

/// <summary>
/// Thread-safe tracker for node output versions.
/// Shared across all graph versions to maintain version continuity.
///
/// This enables equality-based cache skipping: when a node computes the same
/// value as before, its version doesn't increment, so downstream nodes can
/// detect their dependencies haven't changed and skip recomputation.
/// </summary>
/// <remarks>
/// Each node's version lives in its <see cref="NodeState"/>, which evaluation handles and cached
/// values reference directly, so the hot paths read versions without a dictionary lookup.
/// </remarks>
internal sealed class VersionTracker
{
    private readonly ConcurrentDictionary<string, NodeState> _states = new();

    /// <summary>
    /// Gets the change-tracking state of a node, creating it (at version 0) on first use.
    /// </summary>
    /// <param name="nodeId">The node's unique identifier</param>
    public NodeState GetState(string nodeId) =>
        _states.TryGetValue(nodeId, out var state) ? state : _states.GetOrAdd(nodeId, static id => new NodeState(id));

    /// <summary>
    /// Gets the current output version for a node.
    /// Returns 0 if the node has never been computed.
    /// </summary>
    /// <param name="nodeId">The node's unique identifier</param>
    /// <returns>Current version number (0 if never computed)</returns>
    public long GetOutputVersion(string nodeId) => GetState(nodeId).Version;

    /// <summary>
    /// Increments the output version for a node and returns the new version.
    /// Called when a node's computed value has actually changed.
    /// </summary>
    /// <param name="nodeId">The node's unique identifier</param>
    /// <returns>The new version number after incrementing</returns>
    public long IncrementOutputVersion(string nodeId) => GetState(nodeId).IncrementVersion();

    /// <summary>
    /// Forgets the version of a node that no longer exists (see
    /// <see cref="SharedGraphState.TrimUnusedNodes"/>). Must not be called for a node that can
    /// still be evaluated: its version would restart at 0.
    /// </summary>
    /// <param name="nodeId">The removed node's unique identifier</param>
    public void Remove(string nodeId) => _states.TryRemove(nodeId, out _);

    /// <summary>
    /// The number of nodes with a version record (for diagnostics and tests).
    /// </summary>
    public int Count => _states.Count;
}
