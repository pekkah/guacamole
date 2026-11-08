namespace Guacamole.Diagnostics;

/// <summary>
/// Configuration for controlling which metrics are collected.
/// Allows fine-tuning observability vs. performance tradeoffs for different deployment scenarios.
/// </summary>
/// <remarks>
/// <para>
/// Measurements are only taken for instruments that a <c>MeterListener</c> (for example
/// OpenTelemetry) has enabled: with nothing listening, an enabled setting costs a field read per
/// operation, so the defaults are cheap. The settings choose what is recorded while a listener is
/// attached.
/// </para>
/// Distributed tracing needs no switch: activities from <see cref="GuacamoleMetrics.ActivitySource"/>
/// are only created while an <c>ActivityListener</c> (for example OpenTelemetry) listens to it.
/// </remarks>
public sealed record MetricsConfiguration
{
    /// <summary>
    /// Default configuration for development environments.
    /// Collects all metrics for maximum observability.
    /// </summary>
    public static readonly MetricsConfiguration Development = new()
    {
        EnableComputationMetrics = true,
        EnableCacheMetrics = true,
        EnableGraphMetrics = true,
        CollectTrackingNameDimensions = true,
        CollectPipelineStageDimensions = true,
    };

    /// <summary>
    /// Default configuration for production environments.
    /// Balanced observability with minimal performance impact.
    /// </summary>
    public static readonly MetricsConfiguration Production = new()
    {
        EnableComputationMetrics = true,
        EnableCacheMetrics = true,
        EnableGraphMetrics = false, // Skip frequent graph updates
        CollectTrackingNameDimensions = true,
        CollectPipelineStageDimensions = false, // Reduce cardinality
    };

    /// <summary>
    /// Minimal configuration for high-performance scenarios.
    /// Only essential metrics with lowest overhead.
    /// </summary>
    public static readonly MetricsConfiguration Minimal = new()
    {
        EnableComputationMetrics = false,
        EnableCacheMetrics = true, // Cache metrics are usually most valuable
        EnableGraphMetrics = false,
        CollectTrackingNameDimensions = false,
        CollectPipelineStageDimensions = false,
    };

    /// <summary>
    /// Disabled configuration - no metrics collection.
    /// Zero overhead for scenarios where observability is not needed.
    /// </summary>
    public static readonly MetricsConfiguration Disabled = new()
    {
        EnableComputationMetrics = false,
        EnableCacheMetrics = false,
        EnableGraphMetrics = false,
        CollectTrackingNameDimensions = false,
        CollectPipelineStageDimensions = false,
    };

    /// <summary>
    /// Enable collection of computation metrics: <see cref="GuacamoleMetrics.ComputationsTotal"/>,
    /// <see cref="GuacamoleMetrics.ComputationDuration"/> and <see cref="GuacamoleMetrics.ComputationErrors"/>,
    /// recorded each time a provider computes a value (cache hits are not computations).
    /// </summary>
    public bool EnableComputationMetrics { get; init; } = true;

    /// <summary>
    /// Enable collection of cache performance metrics:
    /// <see cref="GuacamoleMetrics.CacheOperationsTotal"/>, <see cref="GuacamoleMetrics.CacheHitRatio"/>
    /// and <see cref="GuacamoleMetrics.CacheSize"/>.
    /// </summary>
    public bool EnableCacheMetrics { get; init; } = true;

    /// <summary>
    /// Enable collection of computation graph structure metrics:
    /// <see cref="GuacamoleMetrics.GraphNodesActive"/>, <see cref="GuacamoleMetrics.GraphDependenciesTotal"/>
    /// and <see cref="GuacamoleMetrics.GraphOperationsTotal"/>.
    /// </summary>
    public bool EnableGraphMetrics { get; init; } = true;

    /// <summary>
    /// Include the provider's tracking name (<c>tracking.name</c>) as a dimension of computation metrics.
    /// Provides rich filtering but increases cardinality.
    /// </summary>
    public bool CollectTrackingNameDimensions { get; init; } = true;

    /// <summary>
    /// Include the provider's pipeline stage (<c>pipeline.stage</c>) as a dimension of computation metrics.
    /// Useful for debugging but can create high cardinality.
    /// </summary>
    public bool CollectPipelineStageDimensions { get; init; } = true;

    private static MetricsConfiguration _current = Production;

    /// <summary>
    /// Gets the current global metrics configuration.
    /// Defaults to Production configuration.
    /// Thread-safe for reads, and lock-free: it is read on every cache access.
    /// </summary>
    public static MetricsConfiguration Current => Volatile.Read(ref _current);

    /// <summary>
    /// Sets the global metrics configuration.
    /// Should be called early in application startup before any computation operations.
    /// Thread-safe.
    /// </summary>
    /// <param name="configuration">New configuration to apply globally</param>
    public static void SetGlobal(MetricsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // The configuration is immutable, so publishing the reference is enough
        Volatile.Write(ref _current, configuration);
    }

    /// <summary>
    /// Creates a new configuration based on this one with specified overrides.
    /// </summary>
    /// <param name="enableComputationMetrics">Override computation metrics setting</param>
    /// <param name="enableCacheMetrics">Override cache metrics setting</param>
    /// <param name="enableGraphMetrics">Override graph metrics setting</param>
    /// <returns>New configuration with overrides applied</returns>
    public MetricsConfiguration With(
        bool? enableComputationMetrics = null,
        bool? enableCacheMetrics = null,
        bool? enableGraphMetrics = null)
    {
        return this with
        {
            EnableComputationMetrics = enableComputationMetrics ?? EnableComputationMetrics,
            EnableCacheMetrics = enableCacheMetrics ?? EnableCacheMetrics,
            EnableGraphMetrics = enableGraphMetrics ?? EnableGraphMetrics
        };
    }
}
