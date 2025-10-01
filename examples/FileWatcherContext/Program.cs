using System;
using Microsoft.Extensions.DependencyInjection;
using Guacamole.Extensions;

namespace FileWatcherContext;

class Program
{
    static void Main(string[] args)
    {
        // Demonstrates that the generated code compiles and instantiates correctly
        var services = new ServiceCollection();
        services.AddComputingContext<MyFileWatcherContext>();
        var serviceProvider = services.BuildServiceProvider();

        var context = serviceProvider.GetComputingContext<MyFileWatcherContext>();

        Console.WriteLine($"FileWatcherContext created successfully!");
        Console.WriteLine($"Type: {context.GetType().Name}");
    }
}
