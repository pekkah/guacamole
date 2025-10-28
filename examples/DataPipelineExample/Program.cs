using Guacamole;
using DataPipelineExample;

Console.WriteLine("=== Guacamole Data Pipeline Example ===\n");

// Create context
var context = new OrderContext();
int recomputeCount = 0;

// Create multi-stage pipeline using provider extension methods
// Stage 1: Filter completed orders
var completedOrders = context.Orders.Where(o => o.Status == "Completed");

// Stage 2: Collect to get collection, then group by customer
var completedCollection = completedOrders.Collect();
var ordersByCustomer = completedCollection.Select(orders =>
    orders.GroupBy(o => o.Customer)
          .ToDictionary(g => g.Key, g => g.ToList()));

// Stage 3: Calculate customer totals
var customerTotals = ordersByCustomer.Select(grouped =>
    grouped.ToDictionary(
        kvp => kvp.Key,
        kvp => kvp.Value.Sum(o => o.Amount)));

// Stage 4: Find top customers
var topCustomers = customerTotals.Select(totals =>
    totals.OrderByDescending(kvp => kvp.Value)
          .Take(3)
          .Select(kvp => (kvp.Key, kvp.Value))
          .ToList());

// Register output handlers
Console.WriteLine("Registering output handlers...\n");

context.RegisterOutput(topCustomers, (top, ctx) =>
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
