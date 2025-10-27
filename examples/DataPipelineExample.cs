#!/usr/bin/env dotnet script
// Example 3: Data Pipeline with Caching
// Demonstrates incremental computation pipelines with multiple transformation stages

using System;
using System.Collections.Generic;
using System.Linq;
using Guacamole;
using Guacamole.Sources;

Console.WriteLine("=== Guacamole Data Pipeline Example ===\n");

// Define data models
public record Order(int Id, string Customer, decimal Amount, string Status);

// Define computing context with multi-stage pipeline
[ComputingContext]
public partial class OrderContext : ComputingContextBase
{
    // Source: All orders - generates OrdersSource accessor
    public partial IncrementalValuesProvider<Order> Orders { get; }

    // Stage 1: Filter completed orders
    public IncrementalValuesProvider<Order> CompletedOrders =>
        Orders.Where(o => o.Status == "Completed");

    // Stage 2: Group by customer
    public IncrementalValueProvider<Dictionary<string, List<Order>>> OrdersByCustomer =>
        CompletedOrders.Select(orders =>
            orders.GroupBy(o => o.Customer)
                  .ToDictionary(g => g.Key, g => g.ToList()));

    // Stage 3: Calculate customer totals
    public IncrementalValueProvider<Dictionary<string, decimal>> CustomerTotals =>
        OrdersByCustomer.Select(grouped =>
            grouped.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Sum(o => o.Amount)));

    // Stage 4: Find top customers
    public IncrementalValueProvider<List<(string Customer, decimal Total)>> TopCustomers =>
        CustomerTotals.Select(totals =>
            totals.OrderByDescending(kvp => kvp.Value)
                  .Take(3)
                  .Select(kvp => (kvp.Key, kvp.Value))
                  .ToList());
}

// Create context
var context = new OrderContext();
int recomputeCount = 0;

// Register output handlers
Console.WriteLine("Registering output handlers...\n");

context.RegisterOutput(context.TopCustomers, (top, ctx) =>
{
    recomputeCount++;
    Console.WriteLine($"[OUTPUT #{recomputeCount}] Top Customers:");
    foreach (var (customer, total) in top)
    {
        Console.WriteLine($"  {customer}: ${total:F2}");
    }
    Console.WriteLine();
});

// Add initial orders
Console.WriteLine("Adding initial orders...");
context.OrdersSource.Push(new Order(1, "Alice", 100m, "Completed"));
context.OrdersSource.Push(new Order(2, "Bob", 200m, "Completed"));
context.OrdersSource.Push(new Order(3, "Alice", 150m, "Completed"));
context.OrdersSource.Push(new Order(4, "Charlie", 300m, "Pending"));  // Not completed - filtered out

Console.WriteLine("\nAdding more completed orders...");
context.OrdersSource.Push(new Order(5, "Bob", 250m, "Completed"));
context.OrdersSource.Push(new Order(6, "Diana", 400m, "Completed"));

Console.WriteLine("\nCompleting a pending order...");
// Remove pending order and add as completed
context.OrdersSource.Remove(new Order(4, "Charlie", 300m, "Pending"));
context.OrdersSource.Push(new Order(4, "Charlie", 300m, "Completed"));

Console.WriteLine("\nAdding another order for Alice...");
context.OrdersSource.Push(new Order(7, "Alice", 75m, "Completed"));

Console.WriteLine($"\nDone! Pipeline recomputed {recomputeCount} times.");
Console.WriteLine("Each change triggers incremental recomputation of only affected stages.");
Console.WriteLine("\nKey benefit: Unchanged pipeline stages use cached values.");
