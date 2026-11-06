using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Guacamole.Diagnostics;

/// <summary>
/// Central metrics collection for Guacamole incremental computation framework.
/// Provides enterprise-grade observability with OpenTelemetry integration.
/// Zero-allocation when no listeners are attached.
/// </summary>
public static class GuacamoleMetrics
{
    /// <summary>
    /// Framework version for metrics metadata.
    /// </summary>
    private const string Version = "1.0.0";

    /// <summary>
    /// Main meter for all Guacamole metrics.
    /// </summary>
    private static readonly Meter s_meter = new("Guacamole.Core", Version);

    /// <summary>
    /// Activity source for distributed tracing integration.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Guacamole.Core", Version);

    #region Core Computation Metrics

    /// <summary>
    /// Total number of computations performed across all providers (cache hits are not counted).
    /// Dimensions: provider.type, node.type, tracking.name, pipeline.stage
    /// </summary>
    public static readonly Counter<long> ComputationsTotal =
        s_meter.CreateCounter<long>(
            "guacamole.computations.total",
            description: "Total number of computations performed");

    /// <summary>
    /// Distribution of computation durations in milliseconds.
    /// Dimensions: provider.type, node.type, tracking.name, pipeline.stage
    /// </summary>
    public static readonly Histogram<double> ComputationDuration =
        s_meter.CreateHistogram<double>(
            "guacamole.computation.duration",
            unit: "ms",
            description: "Computation execution time distribution");

    /// <summary>
    /// Number of computations that produced an error.
    /// Dimensions: provider.type, node.type, tracking.name, pipeline.stage
    /// </summary>
    public static readonly Counter<long> ComputationErrors =
        s_meter.CreateCounter<long>(
            "guacamole.computation.errors.total",
            description: "Total number of computation errors");

    #endregion

    #region Cache Performance Metrics

    /// <summary>
    /// Total cache operations (get, set, invalidate).
    /// Dimensions: operation.type, cache.result
    /// </summary>
    public static readonly Counter<long> CacheOperationsTotal =
        s_meter.CreateCounter<long>(
            "guacamole.cache.operations.total",
            description: "Total cache operations performed");

    /// <summary>
    /// Real-time cache hit ratio as percentage (0-100).
    /// Updated automatically by cache implementation.
    /// </summary>
    public static readonly Gauge<double> CacheHitRatio =
        s_meter.CreateGauge<double>(
            "guacamole.cache.hit_ratio",
            unit: "%",
            description: "Current cache hit ratio percentage");

    /// <summary>
    /// Current number of entries in cache.
    /// Dimensions: cache.type
    /// </summary>
    public static readonly Gauge<int> CacheSize =
        s_meter.CreateGauge<int>(
            "guacamole.cache.size.current",
            description: "Current number of cached entries");

    #endregion

    #region Graph Structure Metrics

    /// <summary>
    /// Current number of active computation nodes in graph.
    /// </summary>
    public static readonly Gauge<int> GraphNodesActive =
        s_meter.CreateGauge<int>(
            "guacamole.graph.nodes.active",
            description: "Active computation nodes in graph");

    /// <summary>
    /// Total number of dependency edges in graph.
    /// </summary>
    public static readonly Gauge<int> GraphDependenciesTotal =
        s_meter.CreateGauge<int>(
            "guacamole.graph.dependencies.total",
            description: "Total dependency edges in computation graph");

    /// <summary>
    /// Graph modification operations (add node, invalidate).
    /// Dimensions: operation.type
    /// </summary>
    public static readonly Counter<long> GraphOperationsTotal =
        s_meter.CreateCounter<long>(
            "guacamole.graph.operations.total",
            description: "Total graph modification operations");

    #endregion

    /// <summary>
    /// Creates a new activity for tracing computation operations.
    /// Use in using statements for automatic start/stop and timing.
    /// </summary>
    /// <param name="operationName">Name of the operation being traced</param>
    /// <param name="tags">Additional tags for the activity</param>
    /// <returns>Activity instance for tracing</returns>
    internal static Activity? StartActivity(string operationName, params (string Key, object? Value)[] tags)
    {
        var activity = ActivitySource.StartActivity(operationName);

        if (activity != null)
        {
            foreach (var (key, value) in tags)
            {
                activity.SetTag(key, value?.ToString());
            }
        }

        return activity;
    }

    /// <summary>
    /// Records a computation performed by a provider. The <c>tracking.name</c> and
    /// <c>pipeline.stage</c> dimensions are included as configured in <paramref name="configuration"/>.
    /// </summary>
    /// <param name="configuration">Metrics configuration in effect</param>
    /// <param name="providerType">Type of provider (value/values)</param>
    /// <param name="nodeType">Type of computation node</param>
    /// <param name="metadata">Metadata of the provider that computed</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    /// <param name="isError">Whether the computation resulted in an error</param>
    internal static void RecordComputation(
        MetricsConfiguration configuration,
        string providerType,
        string nodeType,
        ProviderMetadata metadata,
        double durationMs,
        bool isError)
    {
        var tags = new TagList
        {
            { "provider.type", providerType },
            { "node.type", nodeType }
        };

        if (configuration.CollectTrackingNameDimensions)
            tags.Add("tracking.name", metadata.TrackingName ?? "unknown");

        if (configuration.CollectPipelineStageDimensions && metadata.PipelineStage != null)
            tags.Add("pipeline.stage", metadata.PipelineStage);

        ComputationsTotal.Add(1, tags);
        ComputationDuration.Record(durationMs, tags);

        if (isError)
        {
            ComputationErrors.Add(1, tags);
        }
    }

    /// <summary>
    /// Records a cache operation with result.
    /// </summary>
    /// <param name="operationType">Type of cache operation (get/set/invalidate)</param>
    /// <param name="result">Result of operation (hit/miss/success)</param>
    internal static void RecordCacheOperation(string operationType, string result)
    {
        CacheOperationsTotal.Add(1,
            new KeyValuePair<string, object?>("operation.type", operationType),
            new KeyValuePair<string, object?>("cache.result", result));
    }

    /// <summary>
    /// Updates graph structure metrics.
    /// </summary>
    /// <param name="activeNodes">Current number of active nodes</param>
    /// <param name="totalDependencies">Total number of dependency edges</param>
    internal static void UpdateGraphMetrics(int activeNodes, int totalDependencies)
    {
        GraphNodesActive.Record(activeNodes);
        GraphDependenciesTotal.Record(totalDependencies);
    }

    /// <summary>
    /// Updates cache performance metrics.
    /// </summary>
    /// <param name="hitRatio">Current hit ratio as percentage (0-100)</param>
    /// <param name="cacheSize">Current number of cached entries</param>
    internal static void UpdateCacheMetrics(double hitRatio, int cacheSize)
    {
        CacheHitRatio.Record(hitRatio);
        CacheSize.Record(cacheSize);
    }

    /// <summary>
    /// Disposes metrics resources.
    /// Call on application shutdown.
    /// </summary>
    public static void Dispose()
    {
        s_meter.Dispose();
        ActivitySource.Dispose();
    }
}
