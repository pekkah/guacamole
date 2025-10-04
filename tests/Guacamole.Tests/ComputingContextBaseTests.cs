using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Tests for ComputingContextBase basic instantiation and lifecycle.
/// Progression: basic → edge → incremental → performance → chaos
/// </summary>
public partial class ComputingContextBaseTests
{
    // Test context for testing base class
    [ComputingContext]
    private partial class TestContext : ComputingContextBase
    {
    }

    [Fact]
    public void Constructor_WithValidServices_CreatesInstance()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();

        // Act
        using var context = new TestContext();

        // Assert
        Assert.NotNull(context);
        Assert.False(context.IsDisposed);
    }


    [Fact]
    public void Dispose_AfterCreation_SetsIsDisposed()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContext();

        // Act
        context.Dispose();

        // Assert
        Assert.True(context.IsDisposed);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContext();

        // Act & Assert - should not throw
        context.Dispose();
        context.Dispose();
    }
}
