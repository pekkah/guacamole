#!/usr/bin/env dotnet script
// Example 1: Basic Source Usage
// Demonstrates the fundamentals of push-based incremental computation

using System;
using System.Collections.Generic;
using System.Linq;
using Guacamole;
using Guacamole.Sources;

Console.WriteLine("=== Guacamole Basic Source Usage Example ===\n");

// Define a simple data model
public record Person(string Name, int Age);

// Define a computing context with source properties
[ComputingContext]
public partial class PeopleContext : ComputingContextBase
{
    // Collection property - generates PeopleSource accessor
    public partial IncrementalValuesProvider<Person> People { get; }

    // Computed property - adults only (age >= 18)
    public IncrementalValuesProvider<Person> Adults =>
        People.Where(p => p.Age >= 18);

    // Computed property - count of adults
    public IncrementalValueProvider<int> AdultCount =>
        Adults.Select(adults => adults.Count());
}

// Create context
var context = new PeopleContext();

// Register output handlers - these are called automatically when data changes
Console.WriteLine("Registering output handlers...\n");

context.RegisterOutput(context.AdultCount, (count, ctx) =>
{
    Console.WriteLine($"[OUTPUT] Adult count updated: {count}");
});

context.RegisterOutput(context.Adults, (adults, ctx) =>
{
    Console.WriteLine($"[OUTPUT] Adults list updated:");
    foreach (var adult in adults.OrderBy(a => a.Name))
    {
        Console.WriteLine($"  - {adult.Name} (age {adult.Age})");
    }
    Console.WriteLine();
});

// Push data through sources - outputs trigger automatically
Console.WriteLine("Adding people...");
context.PeopleSource.Push(new Person("Alice", 25));
context.PeopleSource.Push(new Person("Bob", 17));
context.PeopleSource.Push(new Person("Charlie", 30));

Console.WriteLine("\nAdding another adult...");
context.PeopleSource.Push(new Person("Diana", 22));

Console.WriteLine("\nRemoving a person...");
context.PeopleSource.Remove(new Person("Bob", 17));

Console.WriteLine("\nDone! Note: No SaveChanges() needed - outputs triggered automatically.");
