using System.Collections.Immutable;

namespace Guacamole.Core.Pipeline;

/// <summary>
/// Tracks version information for a computation node's cached value.
/// Used for lazy cache validation - instead of clearing caches on source change,
/// we validate cache freshness by checking if dependency versions have changed.
/// </summary>
public readonly record struct NodeVersionInfo(
    /// <summary>
    /// The version of this node's output value. Incremented only when
    /// the computed value changes (based on equality comparison).
    /// </summary>
    long OutputVersion,

    /// <summary>
    /// Snapshot of dependency versions at the time this node's value was computed.
    /// Used to determine if the cached value is still valid.
    /// </summary>
    ImmutableDictionary<string, long> DependencyVersionsAtComputation);
