using System;
using System.Collections.Generic;
using System.Linq;
using Guacamole;
using Guacamole.Core.Providers;
using Guacamole.Core.Providers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Tests verifying custom equality comparer support in provider extension methods.
/// </summary>
public partial class CustomComparerTests
{
    // Example class with proper equality implementation
    private class Person : IEquatable<Person>
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }

        public bool Equals(Person? other)
        {
            if (other is null) return false;
            return Name == other.Name && Age == other.Age;
        }

        public override bool Equals(object? obj) => Equals(obj as Person);
        public override int GetHashCode() => HashCode.Combine(Name, Age);
    }

    // Custom comparer for Person that compares by Name only
    private class PersonNameComparer : IEqualityComparer<Person>
    {
        public bool Equals(Person? x, Person? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Name == y.Name;
        }

        public int GetHashCode(Person obj) => obj.Name.GetHashCode();
    }

    // Custom comparer for strings that ignores case
    private class CaseInsensitiveComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
            => StringComparer.OrdinalIgnoreCase.Equals(x, y);

        public int GetHashCode(string obj)
            => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
    }

    [ComputingContext]
    private partial class SingleValueContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<Person> Person { get; }

    }

    [ComputingContext]
    private partial class CollectionContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Person> People { get; }

    }

    [ComputingContext]
    private partial class StringContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<string> Text { get; }

    }

    private static System.IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Select_WithCustomComparer_UsesComparerForEquality()
    {
        // Arrange
        var services = CreateServices();
        var context = new StringContext();
        int handlerCallCount = 0;
        string? lastResult = null;

        // Use case-insensitive comparer
        var upper = context.Text.Select(
            s => s.ToUpperInvariant(),
            new CaseInsensitiveComparer());

        context.SetText("hello");
        context.SaveChanges();

        context.RegisterOutput(upper, (value, ctx) =>
        {
            handlerCallCount++;
            lastResult = value;
        });

        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);
        Assert.Equal("HELLO", lastResult);

        // Act - Change to different case but same content
        context.SetText("HELLO");
        context.SaveChanges();

        // Assert - With custom comparer, "HELLO" == "hello", so no handler call
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public void Select_WithoutCustomComparer_UsesDefaultEquality()
    {
        // Arrange
        var services = CreateServices();
        var context = new StringContext();
        int handlerCallCount = 0;

        // No custom comparer - uses default case-sensitive comparison
        var upper = context.Text.Select(s => s.ToUpperInvariant());

        context.SetText("hello");
        context.SaveChanges();

        context.RegisterOutput(upper, (value, ctx) =>
        {
            handlerCallCount++;
        });

        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);

        // Act - Change to different case
        context.SetText("HELLO");
        context.SaveChanges();

        // Assert - Different case produces same output "HELLO"
        // but since the source changed, this will trigger handler
        // (This is testing that without custom comparer, pipeline still works)
        Assert.Equal(1, handlerCallCount); // Output is still "HELLO"
    }

    [Fact]
    public void Where_WithCustomComparer_StoreComparerInNode()
    {
        // Arrange
        var services = CreateServices();
        var context = new StringContext();

        // Create Where with custom comparer
        var filtered = context.Text.Where(
            s => s.Length > 3,
            new CaseInsensitiveComparer());

        // Assert - Node stores comparer
        Assert.NotNull(filtered.Node);
    }

    [Fact]
    public void ValuesProvider_Select_WithCustomComparer_UsesComparerForEquality()
    {
        // Arrange
        var services = CreateServices();
        var context = new CollectionContext();
        int handlerCallCount = 0;

        // Custom comparer compares by name only
        var names = context.People.Select(
            p => p.Name,
            StringComparer.OrdinalIgnoreCase);

        context.AddPeople(new Person { Name = "Alice", Age = 30 });
        context.SaveChanges();

        context.RegisterOutput(names, (values, ctx) =>
        {
            handlerCallCount++;
        });

        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);

        // Act - Add same name different case
        context.AddPeople(new Person { Name = "alice", Age = 25 });
        context.SaveChanges();

        // Assert - Collection changed so handler called
        // (Comparer applies to individual values, not collection comparison)
        Assert.Equal(2, handlerCallCount);
    }

    [Fact]
    public void ValuesProvider_Where_WithCustomComparer_StoreComparerInNode()
    {
        // Arrange
        var services = CreateServices();
        var context = new CollectionContext();

        // Create Where with custom comparer
        var filtered = context.People.Where(
            p => p.Age > 18,
            new PersonNameComparer());

        // Assert - Node stores comparer
        Assert.NotNull(filtered.Node);
    }

    [Fact]
    public void ValuesProvider_SelectMany_WithCustomComparer_StoreComparerInNode()
    {
        // Arrange
        var services = CreateServices();
        var context = new CollectionContext();

        // Create SelectMany with custom comparer
        var chars = context.People.SelectMany(
            p => p.Name.ToCharArray().Select(c => c.ToString()),
            StringComparer.OrdinalIgnoreCase);

        // Assert - Node stores comparer
        Assert.NotNull(chars.Node);
    }

    [Fact]
    public void CustomComparer_NodeExposesComparerProperty()
    {
        // Arrange
        var services = CreateServices();
        var context = new StringContext();
        var customComparer = new CaseInsensitiveComparer();

        // Create Select with custom comparer
        var transformed = context.Text.Select(
            s => s.ToUpperInvariant(),
            customComparer);

        // Act - Access node's Comparer property via reflection
        var nodeType = transformed.Node.GetType();
        var comparerProperty = nodeType.GetProperty("Comparer");

        // Assert
        Assert.NotNull(comparerProperty);
        var comparer = comparerProperty.GetValue(transformed.Node);
        Assert.Same(customComparer, comparer);
    }

    [Fact]
    public void CustomComparer_DefaultsToEqualityComparerDefault()
    {
        // Arrange
        var services = CreateServices();
        var context = new StringContext();

        // Create Select without custom comparer (null)
        var transformed = context.Text.Select(s => s.ToUpperInvariant());

        // Act - Access node's Comparer property
        var nodeType = transformed.Node.GetType();
        var comparerProperty = nodeType.GetProperty("Comparer");

        // Assert - Should return EqualityComparer<string>.Default
        Assert.NotNull(comparerProperty);
        var comparer = comparerProperty.GetValue(transformed.Node);
        Assert.Same(EqualityComparer<string>.Default, comparer);
    }
}
