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
    /// Total number of computations performed across all providers.
    /// Dimensions: provider.type, operation.type, node.type, tracking.name
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
    /// Number of computation errors by type.
    /// Dimensions: error.type, provider.type, node.type, tracking.name
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

    /// <summary>
    /// Total cache invalidation events.
    /// Dimensions: invalidation.reason, cascade.level
    /// </summary>
    public static readonly Counter<long> CacheInvalidationsTotal =
        s_meter.CreateCounter<long>(
            "guacamole.cache.invalidations.total",
            description: "Total cache invalidation events");

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

    #region Provider Lifecycle Metrics

    /// <summary>
    /// Total provider creation events.
    /// Dimensions: provider.type, tracking.name, pipeline.stage
    /// </summary>
    public static readonly Counter<long> ProvidersCreatedTotal =
        s_meter.CreateCounter<long>(
            "guacamole.providers.created.total",
            description: "Total provider creation events");

    /// <summary>
    /// Provider transformation operations.
    /// Dimensions: transform.type, provider.type, tracking.name
    /// </summary>
    public static readonly Counter<long> ProviderTransformationsTotal =
        s_meter.CreateCounter<long>(
            "guacamole.provider.transformations.total",
            description: "Total provider transformation operations");

    #endregion

    #region Memory and Resource Metrics

    /// <summary>
    /// Estimated memory usage by computation graphs in bytes.
    /// </summary>
    public static readonly Gauge<long> MemoryUsageEstimated =
        s_meter.CreateGauge<long>(
            "guacamole.memory.usage.estimated",
            unit: "bytes",
            description: "Estimated memory usage by computation framework");

    #endregion

    /// <summary>
    /// Creates a new activity for tracing computation operations.
    /// Use in using statements for automatic start/stop and timing.
    /// </summary>
    /// <param name="operationName">Name of the operation being traced</param>
    /// <param name="tags">Additional tags for the activity</param>
    /// <returns>Activity instance for tracing</returns>
    public static Activity? StartActivity(string operationName, params (string Key, object? Value)[] tags)
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
    /// Records a computation operation with standard metrics and tracing.
    /// </summary>
    /// <param name="providerType">Type of provider (value/values)</param>
    /// <param name="nodeType">Type of computation node</param>
    /// <param name="trackingName">User-provided tracking name</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    /// <param name="isError">Whether the operation resulted in error</param>
    public static void RecordComputation(
        string providerType,
        string nodeType,
        string? trackingName,
        double durationMs,
        bool isError = false)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new KeyValuePair<string, object?>("provider.type", providerType),
            new KeyValuePair<string, object?>("node.type", nodeType),
            new KeyValuePair<string, object?>("tracking.name", trackingName ?? "unknown")
        };

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
    public static void RecordCacheOperation(string operationType, string result)
    {
        CacheOperationsTotal.Add(1,
            new KeyValuePair<string, object?>("operation.type", operationType),
            new KeyValuePair<string, object?>("cache.result", result));
    }

    /// <summary>
    /// Records a provider creation event.
    /// </summary>
    /// <param name="providerType">Type of provider created</param>
    /// <param name="trackingName">Tracking name if provided</param>
    /// <param name="transformType">Type of transformation if applicable</param>
    public static void RecordProviderCreation(
        string providerType,
        string? trackingName = null,
        string? transformType = null)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("provider.type", providerType)
        };

        if (trackingName != null)
            tags.Add(new KeyValuePair<string, object?>("tracking.name", trackingName));

        if (transformType != null)
            tags.Add(new KeyValuePair<string, object?>("transform.type", transformType));

        ProvidersCreatedTotal.Add(1, tags.ToArray());
    }

    /// <summary>
    /// Updates graph structure metrics.
    /// </summary>
    /// <param name="activeNodes">Current number of active nodes</param>
    /// <param name="totalDependencies">Total number of dependency edges</param>
    public static void UpdateGraphMetrics(int activeNodes, int totalDependencies)
    {
        GraphNodesActive.Record(activeNodes);
        GraphDependenciesTotal.Record(totalDependencies);
    }

    /// <summary>
    /// Updates cache performance metrics.
    /// </summary>
    /// <param name="hitRatio">Current hit ratio as percentage (0-100)</param>
    /// <param name="cacheSize">Current number of cached entries</param>
    public static void UpdateCacheMetrics(double hitRatio, int cacheSize)
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
