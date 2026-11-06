namespace Guacamole.Caching;

/// <summary>
/// Statistics provider for cache operations.
/// </summary>
public interface ICacheStatisticsProvider
{
    /// <summary>
    /// Gets current cache statistics.
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// Generic cache interface for key-value storage with thread-safe operations.
/// Provides basic caching operations with performance monitoring capabilities.
/// </summary>
/// <typeparam name="TKey">Type of cache keys (must be non-null)</typeparam>
/// <typeparam name="TValue">Type of cached values</typeparam>
public interface ICache<TKey, TValue>
    where TKey : notnull
{
    /// <summary>
    /// Gets the maximum capacity of the cache.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Attempts to retrieve a value from the cache.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="value">The retrieved value, if found</param>
    /// <returns>True if the key was found, false otherwise</returns>
    bool TryGet(TKey key, out TValue? value);

    /// <summary>
    /// Stores or updates a value in the cache.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to store</param>
    void Set(TKey key, TValue value);

    /// <summary>
    /// Removes a specific key from the cache.
    /// </summary>
    /// <param name="key">The cache key to remove</param>
    /// <returns>True if the key was found and removed, false otherwise</returns>
    bool Remove(TKey key);

    /// <summary>
    /// Removes all items from the cache.
    /// </summary>
    void Clear();

    /// <summary>
    /// Checks if the cache contains the specified key.
    /// </summary>
    /// <param name="key">The cache key to check</param>
    /// <returns>True if the key exists, false otherwise</returns>
    bool Contains(TKey key);
}
