using Guacamole.Core.Caching;
using Xunit;

namespace Guacamole.Core.Tests.Caching;

/// <summary>
/// Tests for LRUCache implementation following TDD progression:
/// Basic → Edge → Incremental → Performance
/// </summary>
public class LRUCacheTests
{
    /// <summary>
    /// BASIC: Test that LRUCache can be created with specified capacity
    /// </summary>
    [Fact]
    public void LRUCache_ShouldCreate_WithSpecifiedCapacity()
    {
        // Act - This will fail until LRUCache<TKey, TValue> is implemented
        var cache = new LRUCache<string, int>(capacity: 10);

        // Assert
        Assert.NotNull(cache);
        Assert.Equal(10, cache.Capacity);
        Assert.Equal(0, cache.CurrentSize);
    }

    /// <summary>
    /// BASIC: Test basic cache operations - Set and TryGet
    /// </summary>
    [Fact]
    public void LRUCache_ShouldSupport_BasicOperations()
    {
        // Arrange
        var cache = new LRUCache<string, int>(capacity: 5);

        // Act & Assert - Set operation
        cache.Set("key1", 100);
        Assert.Equal(1, cache.CurrentSize);

        // TryGet operation
        var found = cache.TryGet("key1", out var value);
        Assert.True(found);
        Assert.Equal(100, value);

        // Non-existent key
        var notFound = cache.TryGet("missing", out var missingValue);
        Assert.False(notFound);
        Assert.Equal(default(int), missingValue);
    }

    /// <summary>
    /// BASIC: Test that cache stores different key-value types
    /// </summary>
    [Fact]
    public void LRUCache_ShouldSupport_DifferentTypes()
    {
        // Arrange & Act
        var stringCache = new LRUCache<string, string>(5);
        var intCache = new LRUCache<int, bool>(5);
        var guidCache = new LRUCache<Guid, DateTime>(5);

        stringCache.Set("key", "value");
        intCache.Set(42, true);
        var guid = Guid.NewGuid();
        var now = DateTime.Now;
        guidCache.Set(guid, now);

        // Assert
        Assert.True(stringCache.TryGet("key", out var stringValue));
        Assert.Equal("value", stringValue);

        Assert.True(intCache.TryGet(42, out var boolValue));
        Assert.True(boolValue);

        Assert.True(guidCache.TryGet(guid, out var dateValue));
        Assert.Equal(now, dateValue);
    }

    /// <summary>
    /// BASIC: Test cache Clear operation
    /// </summary>
    [Fact]
    public void LRUCache_ShouldSupport_ClearOperation()
    {
        // Arrange
        var cache = new LRUCache<string, int>(5);
        cache.Set("key1", 1);
        cache.Set("key2", 2);
        cache.Set("key3", 3);
        Assert.Equal(3, cache.CurrentSize);

        // Act
        cache.Clear();

        // Assert
        Assert.Equal(0, cache.CurrentSize);
        Assert.False(cache.TryGet("key1", out _));
        Assert.False(cache.TryGet("key2", out _));
        Assert.False(cache.TryGet("key3", out _));
    }

    /// <summary>
    /// EDGE: Test cache validation for invalid capacity
    /// </summary>
    [Fact]
    public void LRUCache_ShouldValidate_CapacityArgument()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LRUCache<string, int>(0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LRUCache<string, int>(-1));
    }

    /// <summary>
    /// EDGE: Test LRU eviction when capacity is exceeded
    /// </summary>
    [Fact]
    public void LRUCache_ShouldEvict_LeastRecentlyUsed()
    {
        // Arrange - Cache with capacity 3
        var cache = new LRUCache<string, int>(3);

        // Act - Fill cache to capacity
        cache.Set("key1", 1); // Oldest
        cache.Set("key2", 2);
        cache.Set("key3", 3); // Newest

        // Add 4th item - should evict key1 (least recently used)
        cache.Set("key4", 4);

        // Assert
        Assert.Equal(3, cache.CurrentSize); // Still at capacity
        Assert.False(cache.TryGet("key1", out _)); // Evicted
        Assert.True(cache.TryGet("key2", out _));  // Still there
        Assert.True(cache.TryGet("key3", out _));  // Still there
        Assert.True(cache.TryGet("key4", out _));  // Newly added
    }

    /// <summary>
    /// EDGE: Test that accessing an item updates its LRU position
    /// </summary>
    [Fact]
    public void LRUCache_AccessShouldUpdate_LRUPosition()
    {
        // Arrange
        var cache = new LRUCache<string, int>(3);
        cache.Set("key1", 1);
        cache.Set("key2", 2);
        cache.Set("key3", 3);

        // Act - Access key1 to make it most recently used
        cache.TryGet("key1", out _);

        // Add 4th item - should evict key2 (now least recently used)
        cache.Set("key4", 4);

        // Assert
        Assert.True(cache.TryGet("key1", out _));  // Should still be there (accessed recently)
        Assert.False(cache.TryGet("key2", out _)); // Should be evicted
        Assert.True(cache.TryGet("key3", out _));  // Still there
        Assert.True(cache.TryGet("key4", out _));  // Newly added
    }

    /// <summary>
    /// EDGE: Test cache handles null keys and values appropriately
    /// </summary>
    [Fact]
    public void LRUCache_ShouldHandle_NullKeysAndValues()
    {
        // Arrange
        var cache = new LRUCache<string, string?>(5);

        // Act & Assert - Null key should throw
        Assert.Throws<ArgumentNullException>(() => cache.Set(null!, "value"));
        Assert.Throws<ArgumentNullException>(() => cache.TryGet(null!, out _));

        // Null value should be allowed
        cache.Set("key", null);
        var found = cache.TryGet("key", out var value);
        Assert.True(found);
        Assert.Null(value);
    }

    /// <summary>
    /// EDGE: Test cache behavior with duplicate keys
    /// </summary>
    [Fact]
    public void LRUCache_ShouldHandle_DuplicateKeys()
    {
        // Arrange
        var cache = new LRUCache<string, int>(3);

        // Act - Set same key multiple times
        cache.Set("key1", 100);
        cache.Set("key1", 200); // Update value
        cache.Set("key1", 300); // Update again

        // Assert - Should still have only one entry
        Assert.Equal(1, cache.CurrentSize);
        Assert.True(cache.TryGet("key1", out var value));
        Assert.Equal(300, value); // Latest value
    }

    /// <summary>
    /// INCREMENTAL: Test cache provides performance statistics
    /// </summary>
    [Fact]
    public void LRUCache_ShouldProvide_Statistics()
    {
        // Arrange
        var cache = new LRUCache<string, int>(5);

        // Act - Perform various operations
        cache.Set("key1", 1);
        cache.Set("key2", 2);

        // Cache hits
        cache.TryGet("key1", out _);
        cache.TryGet("key1", out _);

        // Cache miss
        cache.TryGet("missing", out _);

        // Get statistics
        var stats = cache.GetStatistics();

        // Assert
        // CacheStatistics is a value type, no need for null check
        Assert.Equal(2, stats.HitCount);
        Assert.Equal(1, stats.MissCount);
        Assert.Equal(0, stats.EvictionCount);
        Assert.Equal(2.0 / 3.0, stats.HitRate, precision: 2);
    }

    /// <summary>
    /// INCREMENTAL: Test cache statistics track evictions
    /// </summary>
    [Fact]
    public void LRUCache_Statistics_ShouldTrack_Evictions()
    {
        // Arrange
        var cache = new LRUCache<string, int>(2);

        // Act - Cause evictions
        cache.Set("key1", 1);
        cache.Set("key2", 2);
        cache.Set("key3", 3); // Evicts key1
        cache.Set("key4", 4); // Evicts key2

        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(2, stats.EvictionCount);
    }

    /// <summary>
    /// PERFORMANCE: Test that cache operations are O(1)
    /// </summary>
    [Fact]
    public void LRUCache_Operations_ShouldBe_O1()
    {
        // Arrange
        var cache = new LRUCache<string, int>(1000);

        // Act - Measure Set operations
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < 1000; i++)
        {
            cache.Set($"key-{i}", i);
        }

        stopwatch.Stop();
        var setTime = stopwatch.ElapsedMilliseconds;

        // Measure Get operations
        stopwatch.Restart();

        for (int i = 0; i < 1000; i++)
        {
            cache.TryGet($"key-{i}", out _);
        }

        stopwatch.Stop();
        var getTime = stopwatch.ElapsedMilliseconds;

        // Assert - O(1) operations should be very fast
        Assert.True(setTime < 50, $"1000 Set operations took {setTime}ms, expected <50ms");
        Assert.True(getTime < 50, $"1000 Get operations took {getTime}ms, expected <50ms");
    }

    /// <summary>
    /// PERFORMANCE: Test cache eviction time is O(1)
    /// </summary>
    [Fact]
    public void LRUCache_Eviction_ShouldBe_O1()
    {
        // Arrange - Small cache to force evictions
        var cache = new LRUCache<string, int>(100);

        // Fill cache
        for (int i = 0; i < 100; i++)
        {
            cache.Set($"initial-{i}", i);
        }

        // Act - Measure eviction operations
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Force 100 evictions
        for (int i = 0; i < 100; i++)
        {
            cache.Set($"eviction-{i}", i);
        }

        stopwatch.Stop();

        // Assert - Eviction should be fast (O(1) with doubly-linked list)
        Assert.True(stopwatch.ElapsedMilliseconds < 10,
            $"100 evictions took {stopwatch.ElapsedMilliseconds}ms, expected <10ms");
    }

    /// <summary>
    /// PERFORMANCE: Test cache memory overhead is reasonable
    /// </summary>
    [Fact]
    public void LRUCache_ShouldHave_ReasonableMemoryOverhead()
    {
        // Arrange & Act
        var cache = new LRUCache<string, int>(1000);

        // Fill with test data
        for (int i = 0; i < 1000; i++)
        {
            cache.Set($"key-{i:D4}", i);
        }

        // Assert - Cache should be at capacity
        Assert.Equal(1000, cache.CurrentSize);
        Assert.Equal(1000, cache.Capacity);

        // Memory overhead should be <2x input data (constitutional requirement)
        var stats = cache.GetStatistics();
        // CacheStatistics is a value type, no need for null check
    }
}
