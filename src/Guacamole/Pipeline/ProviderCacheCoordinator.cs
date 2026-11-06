using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Version-based cache validation and storage shared by
/// <see cref="Guacamole.IncrementalValueProvider{T}"/> and
/// <see cref="Guacamole.IncrementalValuesProvider{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// When a provider computes a node it records, in <see cref="NodeVersionInfo"/>, the output
/// versions of the inputs it read. Nodes computed inline (without their own up-to-date cache
/// entry, e.g. sources or intermediate nodes of a chain) are looked through: their own
/// dependencies are recorded too, so changes to a source reached through such a node are
/// still detected.
/// </para>
/// <para>
/// A cached value is valid while every recorded input still has the recorded version and,
/// for inputs whose dependencies were not looked through, the input's own cache is valid.
/// </para>
/// </remarks>
internal static class ProviderCacheCoordinator
{
    /// <summary>
    /// Checks whether the cached value of a node is still valid.
    /// </summary>
    public static bool IsCachedValueValid(string nodeId, ComputationGraph graph)
    {
        return IsCachedValueValidRecursive(nodeId, graph, new HashSet<string>());
    }

    private static bool IsCachedValueValidRecursive(
        string nodeId,
        ComputationGraph graph,
        HashSet<string> visited)
    {
        // Cycle detection (shouldn't happen in DAG but defensive)
        if (!visited.Add(nodeId))
            return true; // Already validated in this chain

        if (graph.SharedState.VersionInfo.TryGetValue(nodeId, out var versionInfo))
        {
            // Must have a cached value if we have version info
            if (!graph.ValueCache.TryGet(nodeId, out _))
                return false;

            var recorded = versionInfo.DependencyVersionsAtComputation;

            // Root nodes (sources): valid while their own version is unchanged
            if (recorded.IsEmpty)
            {
                return graph.Versions.GetOutputVersion(nodeId) == versionInfo.OutputVersion;
            }

            foreach (var (depId, storedVersion) in recorded)
            {
                // Any recorded input whose output changed makes this value stale
                if (graph.Versions.GetOutputVersion(depId) != storedVersion)
                    return false;

                // If the input's own dependencies were recorded too, the version checks
                // above already cover its whole chain. Otherwise this value was built from
                // the input's cached value, which must itself still be valid.
                var depNode = GetNode(depId, graph);
                if (depNode != null && depNode.Dependencies.All(recorded.ContainsKey))
                    continue;

                if (!IsCachedValueValidRecursive(depId, graph, visited))
                    return false;
            }

            return true;
        }

        // No version info - this node was never computed through the provider layer.
        // Recursively check dependencies to validate the chain.
        var node = GetNode(nodeId, graph);
        if (node == null)
            return false;

        if (node.Dependencies.IsEmpty)
        {
            // Source node without version info: valid if version is 0 (pristine)
            return graph.Versions.GetOutputVersion(nodeId) == 0;
        }

        foreach (var depId in node.Dependencies)
        {
            if (!IsCachedValueValidRecursive(depId, graph, visited))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Records the current output versions of the inputs a computation of
    /// <paramref name="node"/> will read. Must be called <b>before</b> computing, so that
    /// a change that happens during the computation is not recorded as already seen.
    /// </summary>
    public static ImmutableDictionary<string, long> SnapshotDependencyVersions(
        IComputationNode node,
        ComputationGraph graph)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, long>();
        var toVisit = new Queue<string>(node.Dependencies);

        while (toVisit.Count > 0)
        {
            var depId = toVisit.Dequeue();
            if (builder.ContainsKey(depId))
                continue;

            builder[depId] = graph.Versions.GetOutputVersion(depId);

            // A dependency with a valid cached value is used as-is; its own validity is
            // tracked through its version info. Any other dependency is computed inline,
            // so the inputs it reads are recorded as well.
            if (graph.SharedState.VersionInfo.ContainsKey(depId) && IsCachedValueValid(depId, graph))
                continue;

            var depNode = GetNode(depId, graph);
            if (depNode == null)
                continue;

            foreach (var transitive in depNode.Dependencies)
            {
                toVisit.Enqueue(transitive);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Atomically stores a computed value together with its version information.
    /// The node's output version is incremented only if the value changed.
    /// </summary>
    /// <param name="graph">The computation graph</param>
    /// <param name="nodeId">The computed node</param>
    /// <param name="dependencyVersions">Snapshot taken before computing</param>
    /// <param name="value">The computed value to cache</param>
    /// <param name="isUnchanged">Returns true if the previously cached value equals the new one</param>
    public static void Commit(
        ComputationGraph graph,
        string nodeId,
        ImmutableDictionary<string, long> dependencyVersions,
        object value,
        Func<object, bool> isUnchanged)
    {
        var state = graph.SharedState;

        lock (state.CommitLock)
        {
            bool unchanged = graph.ValueCache.TryGet(nodeId, out var previous)
                && previous != null
                && isUnchanged(previous);

            // Source versions are advanced by MarkSourceChanged; if the version already
            // moved since the last commit, that change has been accounted for.
            var currentVersion = graph.Versions.GetOutputVersion(nodeId);
            bool alreadyAdvanced = state.VersionInfo.TryGetValue(nodeId, out var previousInfo)
                ? currentVersion != previousInfo.OutputVersion
                : currentVersion != 0;

            long version = unchanged || alreadyAdvanced
                ? currentVersion
                : graph.Versions.IncrementOutputVersion(nodeId);

            graph.ValueCache.Set(nodeId, value);
            state.VersionInfo[nodeId] = new NodeVersionInfo(version, dependencyVersions);
        }
    }

    /// <summary>
    /// Builds a dictionary of cached values relevant to this computation.
    /// Includes the node itself and all transitive dependencies.
    /// Only includes version-valid cached values to prevent stale data propagation.
    /// </summary>
    public static IReadOnlyDictionary<string, object> BuildCacheContext(
        IComputationNode node,
        ComputationGraph graph)
    {
        var dict = new Dictionary<string, object>();

        // Check if the node itself is cached and version-valid
        if (IsCachedValueValid(node.Id, graph) &&
            graph.ValueCache.TryGet(node.Id, out var nodeValue) && nodeValue != null)
        {
            dict[node.Id] = nodeValue;
        }

        // Add all dependencies and their transitive dependencies
        var toCheck = new Queue<string>(node.Dependencies);
        var checked_ = new HashSet<string>();

        while (toCheck.Count > 0)
        {
            var depId = toCheck.Dequeue();
            if (!checked_.Add(depId))
                continue;

            // Try to get cached value for this dependency (only if version-valid)
            if (IsCachedValueValid(depId, graph) &&
                graph.ValueCache.TryGet(depId, out var depValue) && depValue != null)
            {
                dict[depId] = depValue;
            }

            var depNode = GetNode(depId, graph);
            if (depNode != null)
            {
                // Register this node for cache invalidation tracking
                // This ensures all nodes in the dependency chain are tracked
                graph.RegisterNodeIfNeeded(depNode);

                foreach (var transDep in depNode.Dependencies)
                {
                    toCheck.Enqueue(transDep);
                }
            }
        }

        return dict;
    }

    /// <summary>
    /// Gets a node from the graph (either static or runtime nodes).
    /// </summary>
    private static IComputationNode? GetNode(string nodeId, ComputationGraph graph)
    {
        if (graph.Nodes.TryGetValue(nodeId, out var node))
            return node;
        if (graph.TryGetRuntimeNode(nodeId, out var runtimeNode))
            return runtimeNode;
        return null;
    }
}
