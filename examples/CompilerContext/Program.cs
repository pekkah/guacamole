using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Guacamole.Extensions;

namespace CompilerContext;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Compiler Context Example ===");
        Console.WriteLine("Demonstrates NEW property convention: Get-only properties are auto-managed\n");

        var services = new ServiceCollection();
        services.AddComputingContext<MyCompilerContext>();
        var serviceProvider = services.BuildServiceProvider();

        using var context = serviceProvider.GetComputingContext<MyCompilerContext>();

        // NEW CONVENTION: Get-only properties have auto-generated mutation methods
        Console.WriteLine("1. Setting project (generated SetProject method)...");
        context.SetProject(new Project("MyApp", "1.0.0"));

        Console.WriteLine("2. Adding source files (generated AddSourceFiles method)...");
        context.AddSourceFiles(new SourceFile("Program.cs", "// Main"));
        context.AddSourceFiles(new SourceFile("Utils.cs", "// Utils"));

        Console.WriteLine($"   Pending changes: {context.HasPendingChanges}");

        Console.WriteLine("\n3. Saving changes...");
        context.SaveChanges();
        Console.WriteLine($"   Pending changes: {context.HasPendingChanges}");

        // Register output to observe computed values
        Console.WriteLine("\n4. Registering output handler...");
        context.RegisterOutput(context.SourceFiles, (files, ctx) =>
        {
            Console.WriteLine($"   Output triggered! File count: {files.Count()}");
            foreach (var file in files)
            {
                Console.WriteLine($"     - {file.Path}");
            }
        });

        Console.WriteLine("\n5. Adding another file...");
        context.AddSourceFiles(new SourceFile("Models.cs", "// Models"));
        context.SaveChanges();

        Console.WriteLine("\n6. Replacing a file (generated ReplaceSourceFiles method)...");
        var oldFile = new SourceFile("Utils.cs", "// Utils");
        var newFile = new SourceFile("Utils.cs", "// Updated Utils");
        context.ReplaceSourceFiles(oldFile, newFile);
        context.SaveChanges();

        Console.WriteLine("\n✅ Example complete! All auto-generated methods work correctly.");
    }
}
