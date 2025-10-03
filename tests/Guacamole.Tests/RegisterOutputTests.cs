using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Guacamole.Core.Providers;

namespace Guacamole.Tests;

/// <summary>
/// Tests for RegisterOutput functionality - verifying Roslyn-like output handler behavior.
/// Tests verify that handlers are called after SaveChanges() with recomputed values.
/// </summary>
public partial class RegisterOutputTests
{
    /// <summary>
    /// Test context with single value property for testing RegisterOutput.
    /// </summary>
    [ComputingContext]
    private partial class TestContextWithValue : ComputingContextBase
    {
        public partial IncrementalValueProvider<string> Name { get; }

        public TestContextWithValue(IServiceProvider services) : base()
        {
            Initialize();
        }

        partial void InitializeComputationNodes();
    }

    /// <summary>
    /// Test context with collection property for testing RegisterOutput.
    /// </summary>
    [ComputingContext]
    private partial class TestContextWithCollection : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Items { get; }

        public TestContextWithCollection(IServiceProvider services) : base()
        {
            Initialize();
        }

        partial void InitializeComputationNodes();
    }

    [Fact]
    public void RegisterOutput_WhenSaveChangesCalled_TriggersHandlerWithComputedValue()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue(services);

        string? capturedValue = null;
        TestContextWithValue? capturedContext = null;

        // Register output handler
        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            capturedValue = value;
            capturedContext = (TestContextWithValue)ctx;
        });

        // Act
        context.SetName("John");
        context.SaveChanges();

        // Assert
        Assert.Equal("John", capturedValue);
        Assert.Same(context, capturedContext);
    }

    [Fact]
    public void RegisterOutput_WithMultipleMutations_OnlyTriggersOnSaveChanges()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue(services);

        var callCount = 0;
        var capturedValues = new List<string>();

        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            callCount++;
            capturedValues.Add(value);
        });

        // Act - Multiple mutations before SaveChanges
        context.SetName("First");
        Assert.Equal(0, callCount); // Not triggered yet

        context.SetName("Second");
        Assert.Equal(0, callCount); // Still not triggered

        context.SaveChanges();

        // Assert - Handler called only once with final value
        Assert.Equal(1, callCount);
        Assert.Single(capturedValues);
        Assert.Equal("Second", capturedValues[0]); // Last value wins
    }

    [Fact]
    public void RegisterOutput_ForCollectionProvider_TriggersWithCollectionValue()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithCollection(services);

        IEnumerable<string>? capturedItems = null;

        context.RegisterOutput(context.Items, (items, ctx) =>
        {
            capturedItems = items.ToList(); // Materialize to avoid multiple enumeration
        });

        // Act
        context.AddItems("Item1");
        context.AddItems("Item2");
        context.AddItems("Item3");
        context.SaveChanges();

        // Assert
        Assert.NotNull(capturedItems);
        Assert.Equal(new[] { "Item1", "Item2", "Item3" }, capturedItems);
    }

    [Fact]
    public void RegisterOutput_WithMultipleHandlers_TriggersAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue(services);

        var handler1Called = false;
        var handler2Called = false;
        var handler3Called = false;

        context.RegisterOutput(context.Name, (value, ctx) => handler1Called = true);
        context.RegisterOutput(context.Name, (value, ctx) => handler2Called = true);
        context.RegisterOutput(context.Name, (value, ctx) => handler3Called = true);

        // Act
        context.SetName("Test");
        context.SaveChanges();

        // Assert
        Assert.True(handler1Called);
        Assert.True(handler2Called);
        Assert.True(handler3Called);
    }

    [Fact]
    public void RegisterOutput_WhenNoChanges_StillTriggersHandlerWithCurrentValue()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue(services);

        string? capturedValue = null;

        context.SetName("Initial");
        context.SaveChanges();

        // Register handler after initial value is set
        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            capturedValue = value;
        });

        // Act - SaveChanges with no new mutations
        context.SaveChanges();

        // Assert - Handler still gets current value
        Assert.Equal("Initial", capturedValue);
    }

    [Fact]
    public void RegisterOutput_WithComputationError_DoesNotCallHandler()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue(services);

        var handlerCalled = false;

        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            handlerCalled = true;
        });

        // Act - SaveChanges without setting value (will fail to compute)
        context.SaveChanges();

        // Assert - Handler should not be called for failed computation
        Assert.False(handlerCalled);
    }

    [Fact]
    public void RegisterOutput_AfterMultipleSaveChanges_TriggersEachTime()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue(services);

        var callCount = 0;
        var capturedValues = new List<string>();

        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            callCount++;
            capturedValues.Add(value);
        });

        // Act - Multiple SaveChanges cycles
        context.SetName("First");
        context.SaveChanges();

        context.SetName("Second");
        context.SaveChanges();

        context.SetName("Third");
        context.SaveChanges();

        // Assert
        Assert.Equal(3, callCount);
        Assert.Equal(new[] { "First", "Second", "Third" }, capturedValues);
    }

    [Fact]
    public void RegisterOutput_WithCollectionMutations_ReflectsAllChanges()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithCollection(services);

        var capturedSnapshots = new List<string[]>();

        context.RegisterOutput(context.Items, (items, ctx) =>
        {
            capturedSnapshots.Add(items.ToArray());
        });

        // Act - Multiple SaveChanges with different operations
        context.AddItems("A");
        context.AddItems("B");
        context.SaveChanges();

        context.AddItems("C");
        context.RemoveItems("A");
        context.SaveChanges();

        // Assert
        Assert.Equal(2, capturedSnapshots.Count);
        Assert.Equal(new[] { "A", "B" }, capturedSnapshots[0]);
        Assert.Equal(new[] { "B", "C" }, capturedSnapshots[1]);
    }
}
