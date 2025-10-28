using System.Collections.Concurrent;
using System.Collections.Immutable;
using Guacamole.Core.Diagnostics;

namespace Guacamole.Core.Pipeline;

/// <summary>
/// Shared mutable state for computation graph that persists across graph versions.
/// This allows runtime-discovered nodes (from extension methods) to be tracked
/// for cache invalidation even when the graph structure changes.
/// </summary>
internal sealed class SharedGraphState
{
    /// <summary>
    /// Runtime-discovered nodes (e.g., from extension methods like Combine, Select).
    /// Thread-safe collection for nodes that are discovered during execution.
    /// Shared across all versions of the graph.
    /// </summary>
    public ConcurrentDictionary<string, IComputationNode> RuntimeNodes { get; } = new();

    /// <summary>
    /// Runtime dependency relationships for dynamically discovered nodes.
    /// Shared across all versions of the graph.
    /// </summary>
    public ConcurrentDictionary<string, ImmutableHashSet<string>> RuntimeDependencies { get; } = new();

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
        if (staticNodes.ContainsKey(node.Id) || RuntimeNodes.ContainsKey(node.Id))
        {
            return; // Already registered
        }

        // Add to runtime collections
        RuntimeNodes.TryAdd(node.Id, node);
        RuntimeDependencies.TryAdd(node.Id, node.Dependencies);
    }
}
