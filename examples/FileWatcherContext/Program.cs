using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Guacamole.Extensions;

namespace FileWatcherContext;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== File Watcher Context Example ===");
        Console.WriteLine("Demonstrates NEW property convention: Get-only properties are auto-managed\n");

        var services = new ServiceCollection();
        services.AddComputingContext<MyFileWatcherContext>();
        var serviceProvider = services.BuildServiceProvider();

        using var context = serviceProvider.GetComputingContext<MyFileWatcherContext>();

        // NEW CONVENTION: Files property is get-only, so mutation methods are auto-generated
        Console.WriteLine("1. Adding tracked files (generated AddFiles method)...");
        var configFile = new TrackedFile("/home/user/config.json", DateTime.UtcNow, 1024);
        var dataFile = new TrackedFile("/home/user/data.db", DateTime.UtcNow, 4096);

        context.AddFiles(configFile);
        context.AddFiles(dataFile);

        Console.WriteLine($"   Pending changes: {context.HasPendingChanges}");

        Console.WriteLine("\n2. Saving changes...");
        context.SaveChanges();
        Console.WriteLine($"   Pending changes: {context.HasPendingChanges}");

        // Register output to observe file changes
        Console.WriteLine("\n3. Registering output handler to monitor file changes...");
        context.RegisterOutput(context.Files, (files, ctx) =>
        {
            Console.WriteLine($"   Files changed! Tracking {files.Count()} files:");
            foreach (var file in files)
            {
                Console.WriteLine($"     - {file.Path} ({file.Size} bytes, modified: {file.LastModified:HH:mm:ss})");
            }
        });

        Console.WriteLine("\n4. Simulating file modification (replace)...");
        System.Threading.Thread.Sleep(1000); // Wait 1 second
        var updatedConfigFile = new TrackedFile("/home/user/config.json", DateTime.UtcNow, 2048);
        context.ReplaceFiles(configFile, updatedConfigFile);
        context.SaveChanges();

        Console.WriteLine("\n5. Removing a file (generated RemoveFiles method)...");
        context.RemoveFiles(dataFile);
        context.SaveChanges();

        Console.WriteLine("\n✅ Example complete! File watching with auto-generated methods works correctly.");
    }
}
