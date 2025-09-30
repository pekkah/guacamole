using System.Collections.Concurrent;
using Guacamole.Core.Diagnostics;

namespace Guacamole.Core.Caching;

/// <summary>
/// Least-Recently-Used cache implementation for storing computed values with predictable eviction behavior.
/// Provides O(1) access time and eviction time with thread-safe operations.
/// </summary>
/// <typeparam name="TKey">Type of cache keys</typeparam>
/// <typeparam name="TValue">Type of cached values</typeparam>
public class LRUCache<TKey, TValue> : ICache<TKey, TValue>, ICacheStatisticsProvider where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, LinkedListNode<CacheEntry>> _index;
    private readonly LinkedList<CacheEntry> _accessOrder;
    private readonly object _lock = new();
    private readonly int _capacity;

    /// <summary>
    /// Maximum number of cached entries.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Current number of entries in cache.
    /// </summary>
    public int CurrentSize => _index.Count;

    /// <summary>
    /// Current number of entries in cache.
    /// </summary>
    public int Count => _index.Count;

    // Statistics tracking
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;

    /// <summary>
    /// Creates an LRU cache with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of entries</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when capacity is less than 1</exception>
    public LRUCache(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1");

        _capacity = capacity;
        _index = new ConcurrentDictionary<TKey, LinkedListNode<CacheEntry>>();
        _accessOrder = new LinkedList<CacheEntry>();
    }

    /// <summary>
    /// Tries to get a value from the cache.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="value">The cached value if found</param>
    /// <returns>True if the key was found, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null</exception>
    public bool TryGet(TKey key, out TValue? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_index.TryGetValue(key, out var node))
        {
            lock (_lock)
            {
                // Move to front (most recently used)
                _accessOrder.Remove(node);
                _accessOrder.AddFirst(node);
            }

            Interlocked.Increment(ref _hitCount);

            // Record cache hit metrics
            if (MetricsConfiguration.Current.EnableCacheMetrics)
            {
                GuacamoleMetrics.RecordCacheOperation("get", "hit");
            }

            value = node.Value.Value;
            return true;
        }

        Interlocked.Increment(ref _missCount);

        // Record cache miss metrics
        if (MetricsConfiguration.Current.EnableCacheMetrics)
        {
            GuacamoleMetrics.RecordCacheOperation("get", "miss");
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Sets a value in the cache with LRU eviction if necessary.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <exception cref="ArgumentNullException">Thrown when key is null</exception>
    public void Set(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);

        bool wasEvicted = false;

        lock (_lock)
        {
            // Update existing key
            if (_index.TryGetValue(key, out var existingNode))
            {
                existingNode.Value = new CacheEntry(key, value);
                _accessOrder.Remove(existingNode);
                _accessOrder.AddFirst(existingNode);

                // Record cache update metrics
                if (MetricsConfiguration.Current.EnableCacheMetrics)
                {
                    GuacamoleMetrics.RecordCacheOperation("set", "update");
                }
                return;
            }

            // Add new entry
            var newEntry = new CacheEntry(key, value);
            var newNode = new LinkedListNode<CacheEntry>(newEntry);

            // Evict if at capacity
            if (_index.Count >= _capacity)
            {
                var lastNode = _accessOrder.Last;
                if (lastNode != null)
                {
                    _accessOrder.RemoveLast();
                    _index.TryRemove(lastNode.Value.Key, out _);
                    Interlocked.Increment(ref _evictionCount);
                    wasEvicted = true;
                }
            }

            _index[key] = newNode;
            _accessOrder.AddFirst(newNode);
        }

        // Record cache metrics outside the lock
        if (MetricsConfiguration.Current.EnableCacheMetrics)
        {
            GuacamoleMetrics.RecordCacheOperation("set", "insert");

            if (wasEvicted)
            {
                GuacamoleMetrics.RecordCacheOperation("evict", "lru");
            }

            // Update cache size metrics
            var hitRatio = (_hitCount + _missCount) > 0 ? (double)_hitCount / (_hitCount + _missCount) * 100 : 0;
            GuacamoleMetrics.UpdateCacheMetrics(hitRatio, _index.Count);
        }
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _index.Clear();
            _accessOrder.Clear();
        }
    }

    /// <summary>
    /// Gets cache performance statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        var hitCount = Interlocked.Read(ref _hitCount);
        var missCount = Interlocked.Read(ref _missCount);
        var evictionCount = Interlocked.Read(ref _evictionCount);

        var totalRequests = hitCount + missCount;
        var hitRate = totalRequests > 0 ? (double)hitCount / totalRequests : 0.0;

        return new CacheStatistics(hitCount, missCount, evictionCount, hitRate);
    }

    /// <summary>
    /// Checks if the cache contains the specified key.
    /// </summary>
    public bool ContainsKey(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _index.ContainsKey(key);
    }

    /// <summary>
    /// Checks if the cache contains the specified key.
    /// </summary>
    public bool Contains(TKey key)
    {
        return ContainsKey(key);
    }

    /// <summary>
    /// Removes a specific key from the cache.
    /// </summary>
    /// <param name="key">The cache key to remove</param>
    /// <returns>True if the key was found and removed, false otherwise</returns>
    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_lock)
        {
            if (_index.TryRemove(key, out var node))
            {
                _accessOrder.Remove(node);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Cache entry containing key-value pair.
    /// </summary>
    private struct CacheEntry
    {
        public TKey Key { get; }
        public TValue Value { get; set; }

        public CacheEntry(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }
}

/// <summary>
/// Cache performance metrics.
/// </summary>
/// <param name="HitCount">Total number of cache hits</param>
/// <param name="MissCount">Total number of cache misses</param>
/// <param name="EvictionCount">Total number of evictions</param>
/// <param name="HitRate">Cache hit rate (0.0 to 1.0)</param>
public readonly record struct CacheStatistics(
    long HitCount,
    long MissCount,
    long EvictionCount,
    double HitRate);