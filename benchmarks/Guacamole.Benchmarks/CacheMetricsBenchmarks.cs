using BenchmarkDotNet.Attributes;
using Guacamole.Caching;
using Guacamole.Diagnostics;

namespace Guacamole.Benchmarks;

/// <summary>
/// The cost of cache metrics on the value cache's hot paths (issue #76), with cache metrics
/// enabled (the default) and nothing listening to them, against cache metrics disabled.
/// </summary>
[MemoryDiagnoser]
public class CacheMetricsBenchmarks
{
    private const int Operations = 1000;

    private LRUCache<string, object> _cache = null!;
    private string[] _keys = null!;
    private object _value = null!;
    private MetricsConfiguration _previous = null!;

    /// <summary>Whether cache metrics are enabled (no listener is attached either way).</summary>
    [Params(true, false)]
    public bool CacheMetrics { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _previous = MetricsConfiguration.Current;
        MetricsConfiguration.SetGlobal(MetricsConfiguration.Production.With(enableCacheMetrics: CacheMetrics));

        _cache = new LRUCache<string, object>(Operations);
        _keys = Enumerable.Range(0, Operations).Select(static i => $"node-{i}").ToArray();
        _value = new object();
        foreach (var key in _keys)
            _cache.Set(key, _value);
    }

    [GlobalCleanup]
    public void Cleanup() => MetricsConfiguration.SetGlobal(_previous);

    /// <summary>Replaces the value of an existing key (time per operation).</summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public void SetExisting()
    {
        foreach (var key in _keys)
            _cache.Set(key, _value);
    }

    /// <summary>Adds a new key to a full cache, evicting the least recently used one (time per operation).</summary>
    [Benchmark(OperationsPerInvoke = 2 * Operations)]
    public void SetNew()
    {
        // Alternates between two key sets, so every Set inserts and evicts
        foreach (var key in _keys)
            _cache.Set(key + "'", _value);
        foreach (var key in _keys)
            _cache.Set(key, _value);
    }

    /// <summary>Looks up an existing key (time per operation).</summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public bool TryGetHit()
    {
        var found = false;
        foreach (var key in _keys)
            found |= _cache.TryGet(key, out _);

        return found;
    }
}
