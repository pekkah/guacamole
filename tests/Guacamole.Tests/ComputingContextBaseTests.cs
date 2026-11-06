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

    [ComputingContext]
    private partial class LifecycleTestContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<string> Name { get; }
    }

    [Fact]
    public void Dispose_ThenSourceChanges_DoesNotThrowOrTriggerOutputs()
    {
        var context = new LifecycleTestContext();
        var source = context.NameSource;
        var handlerCalls = 0;
        context.RegisterOutput(context.Name, (_, _) => handlerCalls++);
        source.Set("before");
        Assert.Equal(1, handlerCalls);

        context.Dispose();

        // The disposed context must have unsubscribed from its sources
        source.Set("after");
        Assert.Equal(1, handlerCalls);
    }

    [Fact]
    public async Task RegisterOutput_ConcurrentWithSourceChanges_DoesNotThrow()
    {
        using var context = new LifecycleTestContext();
        context.NameSource.Set("initial");
        var provider = context.Name;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(
            Xunit.TestContext.Current.CancellationToken);

        var writer = Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                context.NameSource.Set("value" + i++);
            }
        }, Xunit.TestContext.Current.CancellationToken);

        Parallel.For(0, 2_000, _ => context.RegisterOutput(provider, (_, _) => { }));
        await stop.CancelAsync();
        await writer;
    }

    [Fact]
    public void TriggerOutputs_WhenHandlerThrows_RunsRemainingHandlersAndThrowsAggregate()
    {
        using var context = new LifecycleTestContext();
        var secondHandlerValues = new List<string>();
        context.RegisterOutput(context.Name, (_, _) => throw new InvalidOperationException("handler failed"));
        context.RegisterOutput(context.Name, (value, _) => secondHandlerValues.Add(value));

        var ex = Assert.Throws<AggregateException>(() => context.NameSource.Set("Alice"));

        var inner = Assert.Single(ex.InnerExceptions);
        Assert.IsType<InvalidOperationException>(inner);
        Assert.Equal(new[] { "Alice" }, secondHandlerValues);
    }
}
