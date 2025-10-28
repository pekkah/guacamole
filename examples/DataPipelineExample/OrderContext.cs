using Guacamole;

namespace DataPipelineExample;

// Define data models
public record Order(int Id, string Customer, decimal Amount, string Status);

// Define computing context with source property
[ComputingContext]
public partial class OrderContext : ComputingContextBase
{
    // Source property - generates OrdersSource accessor for pushing order data
    public partial IncrementalValuesProvider<Order> Orders { get; }
}
