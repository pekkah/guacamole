using Guacamole.Engine;
using Microsoft.Extensions.DependencyInjection;
using Guacamole.Extensions;
using Xunit;

namespace Guacamole.Tests.Engine;

/// <summary>
/// Unit tests for the QueryEngine implementation.
/// Following TDD with incremental complexity progression.
/// </summary>
public sealed class QueryEngineTests
{
    [Fact]
    public void QueryEngine_CanBeCreated()
    {
        // Arrange & Act
        var engine = new QueryEngine();

        // Assert
        Assert.NotNull(engine);
    }

    [Fact]
    public void QueryEngine_CanBeRegisteredWithDI()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGuacamole();
        using var provider = services.BuildServiceProvider();
        var engine = provider.GetService<IQueryEngine>();

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<QueryEngine>(engine);
    }

    // TODO: Add more tests following incremental complexity progression:
    // - Basic functionality tests
    // - Edge case tests
    // - Incremental computation tests
    // - Performance tests
    // - Chaos tests
}
