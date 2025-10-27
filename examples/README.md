# Guacamole Examples

This directory contains simple, practical examples demonstrating the push-based incremental compilation architecture.

## Examples

### 1. BasicSourceUsage.cs
Simple example showing how to use incremental sources to push data and trigger computation.

**Key concepts:**
- Creating a computing context with source properties
- Pushing data through sources
- Automatic cache invalidation and recomputation
- Registering output handlers

### 2. FileWatcherExample.cs
Real-world example of using file system watcher to push file changes through incremental compilation.

**Key concepts:**
- Integrating external event sources (file watcher)
- Push-based reactive updates
- Incremental pipeline transformations (Select, Where)
- Automatic output triggering

### 3. DataPipelineExample.cs
Example showing incremental data transformation pipelines with caching.

**Key concepts:**
- Building computation pipelines with Select, Where
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

Run any example with dotnet-script:

```bash
dotnet script examples/BasicSourceUsage.cs
```

Or reference the Guacamole DLL:

```bash
dotnet run app.cs examples/BasicSourceUsage.cs
```

## Further Reading

- `PUSH_BASED_ARCHITECTURE_RESEARCH.md` - Detailed architecture documentation
- `IMPLEMENTATION_SUMMARY.md` - Implementation overview
- `docs/quickstart.md` - Getting started guide
