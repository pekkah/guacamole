using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Guacamole;

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

    }

    /// <summary>
    /// Test context with collection property for testing RegisterOutput.
    /// </summary>
    [ComputingContext]
    private partial class TestContextWithCollection : ComputingContextBase
    {
        public partial IncrementalValuesProvider<string> Items { get; }

    }

    [Fact]
    public void RegisterOutput_WhenSaveChangesCalled_TriggersHandlerWithComputedValue()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue();

        string? capturedValue = null;
        TestContextWithValue? capturedContext = null;

        // Register output handler
        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            capturedValue = value;
            capturedContext = (TestContextWithValue)ctx;
        });

        // Act
        context.NameSource.Set("John");

        // Assert
        Assert.Equal("John", capturedValue);
        Assert.Same(context, capturedContext);
    }
    [Fact]
    public void RegisterOutput_ForCollectionProvider_TriggersWithCollectionValue()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithCollection();

        IEnumerable<string>? capturedItems = null;

        context.RegisterOutput(context.Items, (items, ctx) =>
        {
            capturedItems = items.ToList(); // Materialize to avoid multiple enumeration
        });

        // Act
        context.ItemsSource.Push("Item1");
        context.ItemsSource.Push("Item2");
        context.ItemsSource.Push("Item3");

        // Assert
        Assert.NotNull(capturedItems);
        // ImmutableHashSet doesn't preserve order, so compare as sets
        Assert.Equal(new[] { "Item1", "Item2", "Item3" }.OrderBy(x => x), capturedItems.OrderBy(x => x));
    }

    [Fact]
    public void RegisterOutput_WithMultipleHandlers_TriggersAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue();

        var handler1Called = false;
        var handler2Called = false;
        var handler3Called = false;

        context.RegisterOutput(context.Name, (value, ctx) => handler1Called = true);
        context.RegisterOutput(context.Name, (value, ctx) => handler2Called = true);
        context.RegisterOutput(context.Name, (value, ctx) => handler3Called = true);

        // Act
        context.NameSource.Set("Test");

        // Assert
        Assert.True(handler1Called);
        Assert.True(handler2Called);
        Assert.True(handler3Called);
    }
    [Fact]
    public void RegisterOutput_WithComputationError_DoesNotCallHandler()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue();

        var handlerCalled = false;

        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            handlerCalled = true;
        });

        // Act - SaveChanges without setting value (will fail to compute)

        // Assert - Handler should not be called for failed computation
        Assert.False(handlerCalled);
    }

    [Fact]
    public void RegisterOutput_AfterMultipleSaveChanges_TriggersEachTime()
    {
        // Arrange
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestContextWithValue();

        var callCount = 0;
        var capturedValues = new List<string>();

        context.RegisterOutput(context.Name, (value, ctx) =>
        {
            callCount++;
            capturedValues.Add(value);
        });

        // Act - Multiple SaveChanges cycles
        context.NameSource.Set("First");

        context.NameSource.Set("Second");

        context.NameSource.Set("Third");

        // Assert
        Assert.Equal(3, callCount);
        Assert.Equal(new[] { "First", "Second", "Third" }, capturedValues);
    }
}
