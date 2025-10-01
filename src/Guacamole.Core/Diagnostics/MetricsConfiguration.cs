namespace Guacamole.Core.Diagnostics;

/// <summary>
/// Configuration for controlling metrics collection granularity and performance impact.
/// Allows fine-tuning observability vs. performance tradeoffs for different deployment scenarios.
/// </summary>
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
        EnableProviderMetrics = true,
        EnableDetailedTracing = true,
        EnableMemoryMetrics = true,
        CollectTrackingNameDimensions = true,
        CollectPipelineStageDimensions = true,
        MaxTrackingNamesPerProcess = 1000
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
        EnableProviderMetrics = false, // Skip provider lifecycle tracking
        EnableDetailedTracing = false, // Skip detailed tracing
        EnableMemoryMetrics = true,
        CollectTrackingNameDimensions = true,
        CollectPipelineStageDimensions = false, // Reduce cardinality
        MaxTrackingNamesPerProcess = 100
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
        EnableProviderMetrics = false,
        EnableDetailedTracing = false,
        EnableMemoryMetrics = false,
        CollectTrackingNameDimensions = false,
        CollectPipelineStageDimensions = false,
        MaxTrackingNamesPerProcess = 0
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
        EnableProviderMetrics = false,
        EnableDetailedTracing = false,
        EnableMemoryMetrics = false,
        CollectTrackingNameDimensions = false,
        CollectPipelineStageDimensions = false,
        MaxTrackingNamesPerProcess = 0
    };

    /// <summary>
    /// Enable collection of computation timing and count metrics.
    /// Includes computation duration histograms and operation counters.
    /// </summary>
    public bool EnableComputationMetrics { get; init; } = true;

    /// <summary>
    /// Enable collection of cache performance metrics.
    /// Includes hit ratios, cache size, and invalidation events.
    /// </summary>
    public bool EnableCacheMetrics { get; init; } = true;

    /// <summary>
    /// Enable collection of computation graph structure metrics.
    /// Includes node counts and dependency tracking.
    /// May have performance impact due to frequent updates.
    /// </summary>
    public bool EnableGraphMetrics { get; init; } = true;

    /// <summary>
    /// Enable collection of provider lifecycle metrics.
    /// Includes provider creation and transformation events.
    /// </summary>
    public bool EnableProviderMetrics { get; init; } = true;

    /// <summary>
    /// Enable detailed distributed tracing with ActivitySource.
    /// Provides fine-grained operation tracing but may impact performance.
    /// </summary>
    public bool EnableDetailedTracing { get; init; } = true;

    /// <summary>
    /// Enable memory usage estimation metrics.
    /// May require additional computation to estimate memory usage.
    /// </summary>
    public bool EnableMemoryMetrics { get; init; } = true;

    /// <summary>
    /// Include tracking names as metric dimensions.
    /// Provides rich filtering but increases cardinality.
    /// Disable in high-scale scenarios to reduce memory usage.
    /// </summary>
    public bool CollectTrackingNameDimensions { get; init; } = true;

    /// <summary>
    /// Include pipeline stage names as metric dimensions.
    /// Useful for debugging but can create high cardinality.
    /// </summary>
    public bool CollectPipelineStageDimensions { get; init; } = true;

    /// <summary>
    /// Maximum number of unique tracking names to track per process.
    /// Prevents unbounded cardinality growth from dynamic tracking names.
    /// Set to 0 to disable tracking name collection entirely.
    /// </summary>
    public int MaxTrackingNamesPerProcess { get; init; } = 1000;

    /// <summary>
    /// Minimum duration in milliseconds for recording computation timings.
    /// Operations faster than this threshold are not recorded in histograms.
    /// Helps reduce noise from very fast operations.
    /// </summary>
    public double MinimumDurationThresholdMs { get; init; } = 0.1;

    /// <summary>
    /// Whether to record individual computation node performance.
    /// Disable for high-throughput scenarios to reduce overhead.
    /// </summary>
    public bool RecordIndividualNodePerformance { get; init; } = true;

    /// <summary>
    /// Whether to track cache invalidation cascade depth.
    /// Useful for debugging but adds computational overhead.
    /// </summary>
    public bool TrackInvalidationCascades { get; init; } = true;

    /// <summary>
    /// Sample rate for detailed tracing (0.0 to 1.0).
    /// 1.0 = trace all operations, 0.1 = trace 10% of operations.
    /// Helps control overhead in high-throughput scenarios.
    /// </summary>
    public double TracingSampleRate { get; init; } = 1.0;

    /// <summary>
    /// Gets the current global metrics configuration.
    /// Defaults to Production configuration.
    /// </summary>
    public static MetricsConfiguration Current { get; private set; } = Production;

    /// <summary>
    /// Sets the global metrics configuration.
    /// Should be called early in application startup before any computation operations.
    /// </summary>
    /// <param name="configuration">New configuration to apply globally</param>
    public static void SetGlobal(MetricsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Current = configuration;
    }

    /// <summary>
    /// Creates a new configuration based on this one with specified overrides.
    /// </summary>
    /// <param name="enableComputationMetrics">Override computation metrics setting</param>
    /// <param name="enableCacheMetrics">Override cache metrics setting</param>
    /// <param name="enableDetailedTracing">Override tracing setting</param>
    /// <returns>New configuration with overrides applied</returns>
    public MetricsConfiguration With(
        bool? enableComputationMetrics = null,
        bool? enableCacheMetrics = null,
        bool? enableDetailedTracing = null)
    {
        return this with
        {
            EnableComputationMetrics = enableComputationMetrics ?? EnableComputationMetrics,
            EnableCacheMetrics = enableCacheMetrics ?? EnableCacheMetrics,
            EnableDetailedTracing = enableDetailedTracing ?? EnableDetailedTracing
        };
    }

    /// <summary>
    /// Validates configuration settings for consistency.
    /// </summary>
    /// <returns>True if configuration is valid</returns>
    public bool IsValid()
    {
        return MaxTrackingNamesPerProcess >= 0
               && MinimumDurationThresholdMs >= 0
               && TracingSampleRate is >= 0.0 and <= 1.0;
    }
}
