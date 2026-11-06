# Guacamole Examples

This directory contains simple, practical examples demonstrating the push-based incremental compilation architecture.

## Examples

### 1. BasicSourceUsage/
Simple example showing how to use incremental sources to push data and trigger computation.

**Key concepts:**
- Creating a computing context with source properties
- Pushing data through sources
- Using provider extension methods (Where, Collect, Select)
- Automatic cache invalidation and recomputation
- Registering output handlers

### 2. FileWatcherExample/
Simulated file system watcher example showing how to push file changes through incremental compilation.

**Key concepts:**
- Integrating external event sources (simulated file watcher)
- Push-based reactive updates
- Incremental pipeline transformations (Where, Collect, Select)
- Automatic output triggering

### 3. DataPipelineExample/
Example showing multi-stage incremental data transformation pipelines with caching.

**Key concepts:**
- Building computation pipelines with Where, Collect, Select
- Dependency tracking and cache invalidation
- Multiple transformation stages
- Performance benefits of incremental recomputation

## Push-Based Architecture

### Sources Replace Mutation Methods
Instead of mutation methods, data is pushed through sources:

```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    // Property generates a source accessor
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
}

// Use the source to push data
context.SourceFilesSource.Push(new SourceFile("Program.cs", "..."));
context.SourceFilesSource.Remove(oldFile);
context.SourceFilesSource.Replace(oldFile, newFile);

// Changes trigger automatic recomputation - no SaveChanges() needed
```

### Automatic Output Triggering
Outputs are triggered automatically when sources change:

```csharp
context.RegisterOutput(context.Diagnostics, (diagnostics, ctx) => {
    // Called automatically when SourceFiles changes
    foreach (var diagnostic in diagnostics) {
        Console.WriteLine(diagnostic);
    }
});
```

### No SaveChanges Required
The push-based architecture eliminates the need for `SaveChanges()`:
- Sources trigger events immediately when data changes
- Context automatically invalidates affected caches
- Registered outputs are called with updated values

## Running Examples

Each example is a standalone .NET project. Build and run using standard .NET commands:

```bash
# Build an example
dotnet build examples/BasicSourceUsage

# Run an example
dotnet run --project examples/BasicSourceUsage

# Or navigate to the example directory
cd examples/BasicSourceUsage
dotnet run
```

All examples require .NET 11 RC3 SDK or later and use C# partial properties (C# 13+).

## Further Reading

- `PUSH_BASED_ARCHITECTURE_RESEARCH.md` - Detailed architecture documentation
- `IMPLEMENTATION_SUMMARY.md` - Implementation overview
- `docs/quickstart.md` - Getting started guide
