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
/// Tests verifying equality-based caching works correctly with complex objects.
/// This tests the critical scenario: does object equality work properly?
/// </summary>
public partial class EqualityBasedComplexObjectCachingTests
{
    // Test with class (reference type) - BAD for equality
    private class PersonClass
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    // Test with record (proper structural equality) - GOOD for equality
    private record PersonRecord(string Name, int Age);

    // Test with class that implements IEquatable - GOOD for equality
    private class PersonEquatable : IEquatable<PersonEquatable>
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }

        public bool Equals(PersonEquatable? other)
        {
            if (other is null) return false;
            return Name == other.Name && Age == other.Age;
        }

        public override bool Equals(object? obj) => Equals(obj as PersonEquatable);

        public override int GetHashCode() => HashCode.Combine(Name, Age);
    }

    [ComputingContext]
    private partial class ComplexObjectContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<PersonRecord> Person { get; }

        public ComplexObjectContext(System.IServiceProvider services) : base()
        {
            Initialize();
        }

        partial void InitializeComputationNodes();
    }

    [ComputingContext]
    private partial class ComplexCollectionContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<PersonRecord> People { get; }

        public ComplexCollectionContext(System.IServiceProvider services) : base()
        {
            Initialize();
        }

        partial void InitializeComputationNodes();
    }

    private static System.IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ComplexObject_WithRecord_EqualityWorksCorrectly()
    {
        // Arrange
        var services = CreateServices();
        var context = new ComplexObjectContext(services);
        int handlerCallCount = 0;

        context.RegisterOutput(context.Person, (person, ctx) =>
        {
            handlerCallCount++;
        });

        context.SetPerson(new PersonRecord("Alice", 30));
        context.SaveChanges();
        Assert.Equal(1, handlerCallCount); // Baseline

        // Act - Set to equivalent record (different instance, same values)
        context.SetPerson(new PersonRecord("Alice", 30));
        context.SaveChanges();

        // Assert - Records have structural equality, so no cache invalidation
        Assert.Equal(1, handlerCallCount); // Should NOT increase!
    }

    [Fact]
    public void ComplexObject_SelectProperty_CachingWorks()
    {
        // Arrange
        var services = CreateServices();
        var context = new ComplexObjectContext(services);
        int handlerCallCount = 0;
        string? lastName = null;

        // Select just the Name property
        var personName = context.Person.Select(p => p.Name);

        context.SetPerson(new PersonRecord("Alice", 30));
        context.SaveChanges();

        context.RegisterOutput(personName, (name, ctx) =>
        {
            handlerCallCount++;
            lastName = name;
        });

        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);
        Assert.Equal("Alice", lastName);

        // Act - Change only age (name stays same)
        context.SetPerson(new PersonRecord("Alice", 31));
        context.SaveChanges();

        // Assert - Person object changed, so Select recomputes
        // BUT output is still "Alice", so handler not called
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public void ComplexCollection_SelectProperty_CachingWorks()
    {
        // Arrange
        var services = CreateServices();
        var context = new ComplexCollectionContext(services);
        int handlerCallCount = 0;
        string[]? lastNames = null;

        // Select just names from collection
        var names = context.People.Select(p => p.Name);

        context.AddPeople(new PersonRecord("Alice", 30));
        context.AddPeople(new PersonRecord("Bob", 25));
        context.SaveChanges();

        context.RegisterOutput(names, (values, ctx) =>
        {
            handlerCallCount++;
            lastNames = values.ToArray();
        });

        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);
        Assert.Equal(new[] { "Alice", "Bob" }, lastNames);

        // Act - Add person with same name (but different age)
        context.AddPeople(new PersonRecord("Alice", 99));
        context.SaveChanges();

        // Assert - Collection changed, Select recomputes
        // Output is now ["Alice", "Bob", "Alice"]
        Assert.Equal(2, handlerCallCount);
        Assert.Equal(new[] { "Alice", "Bob", "Alice" }, lastNames);
    }

    [Fact]
    public void ComplexCollection_FilterByProperty_CachingWorks()
    {
        // Arrange
        var services = CreateServices();
        var context = new ComplexCollectionContext(services);
        int handlerCallCount = 0;

        // Filter people over 30
        var over30 = context.People
            .Where(p => p.Age > 30)
            .Select(p => p.Name);

        context.AddPeople(new PersonRecord("Alice", 25));
        context.AddPeople(new PersonRecord("Bob", 35));
        context.SaveChanges();

        context.RegisterOutput(over30, (names, ctx) =>
        {
            handlerCallCount++;
        });

        context.SaveChanges();
        Assert.Equal(1, handlerCallCount);

        // Act - Add person under 30 (doesn't affect filter output)
        context.AddPeople(new PersonRecord("Charlie", 20));
        context.SaveChanges();

        // Assert - Collection changed, filter recomputes
        // But output is still just ["Bob"], so handler not called
        Assert.Equal(1, handlerCallCount); // Should NOT increase
    }

    [Fact]
    public void ComplexObject_RecordVsClass_ShowsEqualityDifference()
    {
        // This test demonstrates WHY records are important

        // Records: structural equality ✅
        var record1 = new PersonRecord("Alice", 30);
        var record2 = new PersonRecord("Alice", 30);
        Assert.True(EqualityComparer<PersonRecord>.Default.Equals(record1, record2));

        // Classes: reference equality ❌
        var class1 = new PersonClass { Name = "Alice", Age = 30 };
        var class2 = new PersonClass { Name = "Alice", Age = 30 };
        Assert.False(EqualityComparer<PersonClass>.Default.Equals(class1, class2));

        // Classes with IEquatable: structural equality ✅
        var eq1 = new PersonEquatable { Name = "Alice", Age = 30 };
        var eq2 = new PersonEquatable { Name = "Alice", Age = 30 };
        Assert.True(EqualityComparer<PersonEquatable>.Default.Equals(eq1, eq2));
    }
}
