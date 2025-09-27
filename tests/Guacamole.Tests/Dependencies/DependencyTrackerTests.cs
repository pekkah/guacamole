using Guacamole.Dependencies;
using Microsoft.Extensions.DependencyInjection;
using Guacamole.Extensions;
using Xunit;

namespace Guacamole.Tests.Dependencies;

/// <summary>
/// Unit tests for the DependencyTracker implementation.
/// Following TDD with incremental complexity progression.
/// </summary>
public sealed class DependencyTrackerTests
{
    [Fact]
    public void DependencyTracker_CanBeCreated()
    {
        // Arrange & Act
        var tracker = new DependencyTracker();

        // Assert
        Assert.NotNull(tracker);
    }

    [Fact]
    public void DependencyTracker_CanBeRegisteredWithDI()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGuacamole();
        using var provider = services.BuildServiceProvider();
        var tracker = provider.GetService<IDependencyTracker>();

        // Assert
        Assert.NotNull(tracker);
        Assert.IsType<DependencyTracker>(tracker);
    }

    // TODO: Add more tests following incremental complexity progression:
    // - Basic dependency tracking tests
    // - Dependency graph validation tests
    // - Cycle detection tests
    // - Dependency invalidation tests
    // - Performance tests for large dependency graphs
}
