using System;
using Microsoft.Extensions.DependencyInjection;
using Guacamole.Extensions;

namespace CompilerContext;

class Program
{
    static void Main(string[] args)
    {
        // Demonstrates that the generated code compiles and instantiates correctly
        var services = new ServiceCollection();
        services.AddComputingContext<MyCompilerContext>();
        var serviceProvider = services.BuildServiceProvider();

        var context = serviceProvider.GetComputingContext<MyCompilerContext>();

        Console.WriteLine($"CompilerContext created successfully!");
        Console.WriteLine($"Type: {context.GetType().Name}");
    }
}
