using System.Diagnostics.Metrics;
using Guacamole.Caching;
using Guacamole.Diagnostics;
using Guacamole.Tests.Common;
using Xunit;

namespace Guacamole.Tests.Diagnostics;

/// <summary>
/// The value cache records <see cref="GuacamoleMetrics.CacheOperationsTotal"/>,
/// <see cref="GuacamoleMetrics.CacheHitRatio"/> and <see cref="GuacamoleMetrics.CacheSize"/> while
/// something listens to them (issue #76: they are not recorded at all otherwise).
/// </summary>
/// <remarks>
/// Cache measurements carry no tag identifying the cache, and other tests use caches concurrently,
/// so the counts are checked as lower bounds.
/// </remarks>
public class CacheMetricsTests
{
    private const string MeterName = "Guacamole.Core";

    /// <summary>
    /// Counts the cache measurements by operation and result. Instruments are matched by name (see
    /// <see cref="ComputationMetricsTests"/>).
    /// </summary>
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Dictionary<string, long> _counts = new();
        private long _gauges;

        public Recorder()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MeterName
                    && instrument.Name is "guacamole.cache.operations.total" or "guacamole.cache.size.current" or "guacamole.cache.hit_ratio")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? operation = null, result = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "operation.type")
                        operation = tag.Value as string;
                    else if (tag.Key == "cache.result")
                        result = tag.Value as string;
                }

                lock (_counts)
                    _counts[$"{operation}/{result}"] = Count($"{operation}/{result}") + value;
            });
            _listener.SetMeasurementEventCallback<int>((_, _, _, _) => Interlocked.Increment(ref _gauges));
            _listener.SetMeasurementEventCallback<double>((_, _, _, _) => Interlocked.Increment(ref _gauges));
            _listener.Start();
        }

        public long Gauges => Interlocked.Read(ref _gauges);

        public long Count(string key)
        {
            lock (_counts)
                return _counts.TryGetValue(key, out var count) ? count : 0;
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public void CacheOperations_AreRecorded_WhileListenedTo()
    {
        if (!MetricsConfiguration.Current.EnableCacheMetrics)
            Assert.Skip("Cache metrics are disabled in the global configuration.");

        using var recorder = new Recorder();
        var cache = new LRUCache<string, object>(capacity: 10);

        for (var i = 0; i < 20; i++)
            cache.Set($"key-{i}", i);
        cache.Set("key-19", 19);
        for (var i = 0; i < 10; i++)
            Assert.True(cache.TryGet($"key-{i + 10}", out _));
        for (var i = 0; i < 5; i++)
            Assert.False(cache.TryGet($"key-{i}", out _));

        Assert.True(recorder.Count("set/insert") >= 20);
        Assert.True(recorder.Count("evict/lru") >= 10);
        Assert.True(recorder.Count("set/update") >= 1);
        Assert.True(recorder.Count("get/hit") >= 10);
        Assert.True(recorder.Count("get/miss") >= 5);
        Assert.True(recorder.Gauges >= 2 * 20); // hit ratio and size, on every insert
    }

    /// <summary>
    /// With cache metrics enabled (the default) but nothing listening, a cache operation records
    /// nothing, so it allocates nothing.
    /// </summary>
    [Fact]
    public void CacheOperations_AllocateNothing_WithoutAListener()
    {
        var cache = new LRUCache<string, object>(capacity: 4);
        var keys = Enumerable.Range(0, 8).Select(i => $"key-{i}").ToArray();
        var value = new object();
        foreach (var key in keys)
            cache.Set(key, value);

        // Updates and lookups (inserts allocate a list node)
        var allocated = ScalingAssert.AllocatedBytes(() =>
        {
            foreach (var key in keys.AsSpan(4))
            {
                cache.Set(key, value);
                cache.TryGet(key, out _);
                cache.TryGet("missing", out _);
            }
        });

        // Another test may attach a listener meanwhile; only check when none was attached
        if (!GuacamoleMetrics.CacheOperationsTotal.Enabled)
            Assert.Equal(0, allocated);
    }
}
