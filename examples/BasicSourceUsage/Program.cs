using Guacamole;
using BasicSourceUsage;

Console.WriteLine("=== Guacamole Basic Source Usage Example ===\n");

// Create context
using var context = new PeopleContext();

// Create computed properties using provider extension methods
var adults = context.People.Where(p => p.Age >= 18);
var adultCount = adults.Collect().Select(a => a.Length);

// Register output handlers - these are called automatically when data changes
Console.WriteLine("Registering output handlers...\n");

// RegisterOutput returns a registration: dispose it to stop that handler
var countRegistration = context.RegisterOutput(adultCount, (count, ctx) =>
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

Console.WriteLine("\nUnregistering the adult count handler, then removing an adult (only the list updates)...");
countRegistration.Dispose();
context.PeopleSource.Remove(new Person("Alice", 25));

Console.WriteLine("\nDone! Note: No SaveChanges() needed - outputs triggered automatically.");
