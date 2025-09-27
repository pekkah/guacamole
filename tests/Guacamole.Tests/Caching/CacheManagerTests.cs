using Guacamole.Caching;
using Microsoft.Extensions.DependencyInjection;
using Guacamole.Extensions;
using Xunit;

namespace Guacamole.Tests.Caching;

/// <summary>
/// Unit tests for the CacheManager implementation.
/// Following TDD with incremental complexity progression.
/// </summary>
public sealed class CacheManagerTests
{
    [Fact]
    public void CacheManager_CanBeCreated()
    {
        // Arrange & Act
        var cacheManager = new CacheManager();

        // Assert
        Assert.NotNull(cacheManager);
    }

    [Fact]
    public void CacheManager_CanBeRegisteredWithDI()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGuacamole();
        using var provider = services.BuildServiceProvider();
        var cacheManager = provider.GetService<ICacheManager>();

        // Assert
        Assert.NotNull(cacheManager);
        Assert.IsType<CacheManager>(cacheManager);
    }

    // TODO: Add more tests following incremental complexity progression:
    // - Basic caching functionality tests
    // - Cache invalidation tests
    // - Memory pressure tests
    // - Cache performance tests
    // - Concurrent access tests
}
