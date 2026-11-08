# Guacamole Examples

This directory contains small console projects demonstrating the push-based incremental computation API. All of them are part of `guacamole.sln`, so CI builds them and catches public API drift.

## Examples

### 1. [BasicSourceUsage](BasicSourceUsage/)
Pushing data into a source and reacting to it.

**Key concepts:**
- A computing context with one input property (`People`) and its generated `PeopleSource` accessor
- Pushing and removing items
- Provider extension methods (`Where`, `Collect`, `Select`)
- Registering output handlers, and unregistering one by disposing its registration

### 2. [FileWatcherExample](FileWatcherExample/)
A simulated file system watcher pushing file changes.

**Key concepts:**
- Forwarding external events into a source (`Push`, `Replace`, `Remove`)
- Pipeline transformations (`Where`, `Collect`, `Select`)
- Output handlers that run automatically after each change

### 3. [DataPipelineExample](DataPipelineExample/)
A multi-stage data transformation pipeline.

**Key concepts:**
- Chaining `Where`, `Collect` and `Select` stages
- Counting how often an output handler runs
- Handlers run only when their provider's value changes

### 4. [CrossFileSymbolsExample](CrossFileSymbolsExample/)
Resolving references across files, as a compiler's type checker does.

**Key concepts:**
- Per-file declarations collected into a symbol table with `CollectToDictionary`, the first declaration in path order winning (`conflictComparer: StringComparer.Ordinal`), so the table does not depend on push order
- The table is updated from the changed files only, and an edit that leaves the declarations unchanged keeps the table's instance and version
- `Combine` to pair each file with the table, and `Restrict` so unrelated declaration changes do not re-check a file
- Per-item memoization: the output shows how many type checks each change runs
- `LookupOrDefault` for one entry of the table: its output handler runs only when that entry changes, not on every table change

## Push-Based Architecture

### Sources
Data is pushed through the source accessor the generator creates for each `partial` get-only provider property:

```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    // Generates a SourceFilesSource accessor
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
}

context.SourceFilesSource.Push(new SourceFile("Program.cs", "..."));
context.SourceFilesSource.Remove(oldFile);
context.SourceFilesSource.Replace(oldFile, newFile);
```

There is no commit step: each change takes effect immediately.

### Output Handlers
Output handlers run automatically after a source changes:

```csharp
var csharpFiles = context.SourceFiles.Where(f => f.Path.EndsWith(".cs")).Collect();

context.RegisterOutput(csharpFiles, (files, ctx) =>
{
    Console.WriteLine($"{files.Length} C# files");
});
```

What happens on a change:
- The source raises its `Changed` event synchronously
- The context increments the source's version (cached values are revalidated lazily when read; nothing is cleared eagerly)
- Each registered handler's provider is read, and the handler is called if the value changed since the handler last ran

`RegisterOutput` returns an `IDisposable`: dispose it to unregister just that handler (`BasicSourceUsage` does this). Disposing the context drops all handlers.

Note that the examples build their pipelines once, right after creating the context. Every operator call creates a new node with its own cache, so pipelines should not be rebuilt on every use.

## Running Examples

Each example is a standalone .NET project:

```bash
# Build an example
dotnet build examples/BasicSourceUsage

# Run an example
dotnet run --project examples/BasicSourceUsage

# Or navigate to the example directory
cd examples/BasicSourceUsage
dotnet run
```

All examples require the .NET 11 RC3 SDK or later and use C# partial properties (C# 13+). They reference the `Guacamole` and `Guacamole.SourceGenerators` projects directly; a NuGet consumer needs both packages.

## Further Reading

- [Quickstart](../docs/quickstart.md) - Getting started guide
- [Generated Code Reference](../docs/generated-code-reference.md) - What the generator emits
- [Performance Guide](../docs/performance-guide.md) - Caching behavior
- [README](../README.md) - Overview
