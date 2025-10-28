using Guacamole;
using BasicSourceUsage;

Console.WriteLine("=== Guacamole Basic Source Usage Example ===\n");

// Create context
var context = new PeopleContext();

// Create computed properties using provider extension methods
var adults = context.People.Where(p => p.Age >= 18);
var adultCount = adults.Collect().Select(a => a.Length);

// Register output handlers - these are called automatically when data changes
Console.WriteLine("Registering output handlers...\n");

context.RegisterOutput(adultCount, (count, ctx) =>
{
    Console.WriteLine($"[OUTPUT] Adult count updated: {count}");
});

var adultsCollection = adults.Collect();
context.RegisterOutput(adultsCollection, (adultsData, ctx) =>
{
    Console.WriteLine($"[OUTPUT] Adults list updated:");
    foreach (var adult in adultsData.OrderBy(a => a.Name))
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
