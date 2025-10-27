using Guacamole;

namespace BasicSourceUsage;

// Define a simple data model
public record Person(string Name, int Age);

// Define a computing context with source properties
[ComputingContext]
public partial class PeopleContext : ComputingContextBase
{
    // Source property - generates PeopleSource accessor for pushing data
    public partial IncrementalValuesProvider<Person> People { get; }
}
