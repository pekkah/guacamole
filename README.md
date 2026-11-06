# Guacamole

[![CI](https://github.com/pekkah/guacamole/actions/workflows/ci.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/ci.yml)
[![Publish](https://github.com/pekkah/guacamole/actions/workflows/publish.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/v/Guacamole.svg)](https://www.nuget.org/packages/Guacamole/)

A .NET framework for building incremental compilers and computation pipelines, inspired by Roslyn's incremental generators. Provides automatic dependency tracking, caching, and change propagation for efficient recomputation when inputs change.

## Features

- **Push-Based Reactive Architecture**: External sources push data changes, triggering automatic recomputation
- **Incremental Computation**: Automatic dependency tracking and selective recomputation
- **Provider-based API**: Familiar Roslyn-style `IncrementalValueProvider<T>` and `IncrementalValuesProvider<T>`
- **Source Generation**: Generate context classes with push-based source accessors at compile time
- **Thread-safe**: Built-in reader/writer locking for concurrent access
- **Dependency Injection**: First-class DI support with Microsoft.Extensions.DependencyInjection
- **Performance**: LRU caching, optimized graph invalidation, comprehensive benchmarks

## Quick Start

Install the packages:

```bash
dotnet add package Guacamole
dotnet add package Guacamole.SourceGenerators
```

> **Note**: `Guacamole` provides the core runtime functionality, while `Guacamole.SourceGenerators` contains the source generators that create push-based source accessors at compile time. Both packages are required for full functionality.

Define a computing context:

```csharp
using Guacamole;

[ComputingContext]
public partial class MyCompilerContext : ComputingContextBase
{
    // Source property - generates SourceCodeSource accessor
    public partial IncrementalValueProvider<string> SourceCode { get; }

    // Derived computation
    public IncrementalValueProvider<ParseTree> ParsedTree { get; }

    public MyCompilerContext()
    {
        // Build computation pipeline
        ParsedTree = SourceCode.Select(Parse);
    }

    private ParseTree Parse(string source) => /* ... */;
}
```

Use the push-based source API:

```csharp
var context = new MyCompilerContext();

// Register output handler - called automatically when data changes
context.RegisterOutput(context.ParsedTree, (tree, ctx) =>
{
    Console.WriteLine($"Parsed: {tree}");
});

// Push data through source - triggers automatic recomputation
context.SourceCodeSource.Set("let x = 42");

// Output handler called automatically - no SaveChanges() needed!

var result = context.ParsedTree.GetValue();
if (result.IsSuccess)
{
    var tree = result.Value;
}
```

## Provider Extension Methods

Guacamole provides a rich set of extension methods for building computation pipelines, similar to Roslyn's incremental generators:

### IncrementalValueProvider<T> Extensions

```csharp
// Transform a single value
provider.Select(x => x * 2)
provider.Select(x => x.ToString(), StringComparer.OrdinalIgnoreCase)

// Filter a single value
provider.Where(x => x > 0)
provider.Where(x => x.IsValid, customComparer)

// Combine two providers
first.Combine(second)  // Returns IncrementalValueProvider<(TFirst, TSecond)>

// Transform to multiple values
provider.SelectMany(x => x.Children)
provider.SelectMany(x => x.Items, customComparer)
```

### IncrementalValuesProvider<T> Extensions

```csharp
// Transform each value
values.Select(x => x.Name)
values.Select(x => x.ToString(), customComparer)

// Filter values
values.Where(x => x.Age >= 18)
values.Where(x => x.IsActive, customComparer)

// Flatten nested collections
values.SelectMany(x => x.Tags)
values.SelectMany(x => x.Children, customComparer)

// Collect to immutable array
values.Collect()  // Returns IncrementalValueProvider<ImmutableArray<T>>

// Combine with single value
values.Combine(singleValue)  // Pairs each value with the single value
```

All methods support:
- **Custom equality comparers** for fine-grained cache control
- **Automatic caching** at each pipeline stage
- **Lazy evaluation** - computed only when accessed
- **Dependency tracking** - automatic invalidation when inputs change

## Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| **Guacamole** | Complete framework including `ComputingContextBase`, incremental computation engine, provider APIs, and all core infrastructure | [![NuGet](https://img.shields.io/nuget/v/Guacamole.svg)](https://www.nuget.org/packages/Guacamole/) |
| **Guacamole.SourceGenerators** | Source generators for code generation (automatically installed with Guacamole) | [![NuGet](https://img.shields.io/nuget/v/Guacamole.SourceGenerators.svg)](https://www.nuget.org/packages/Guacamole.SourceGenerators/) |

## Requirements

- .NET 11 RC3 SDK (11.0.100-rc.3) or later
- C# 15 (LangVersion latest; partial properties require C# 13+)

## Building

```bash
dotnet build
```

## Testing

```bash
dotnet test
```

## Benchmarking

```bash
cd benchmarks/Guacamole.Benchmarks
dotnet run -c Release
```

## Releasing

Packages are published to nuget.org only from version tags (or a manual run of the Publish workflow):

```bash
git checkout main && git pull
git tag -a v0.2.0 -m "Release 0.2.0"      # or v0.2.0-beta.1 for a prerelease
git push origin v0.2.0
```

The tag name (without `v`) is the package version. Pushes to `main` only build, test and pack. See [docs/ci-cd.md](docs/ci-cd.md) for details.

## Examples

See the [`examples/`](examples/) directory for complete working examples:

- [`BasicSourceUsage.cs`](examples/BasicSourceUsage.cs) - Fundamental push-based source usage patterns
- [`FileWatcherExample.cs`](examples/FileWatcherExample.cs) - File system integration with incremental compilation
- [`DataPipelineExample.cs`](examples/DataPipelineExample.cs) - Multi-stage data transformation pipeline

Run examples with:
```bash
dotnet script examples/BasicSourceUsage.cs
```

## Architecture

Guacamole is built on three core concepts:

### 1. **Push-Based Sources**
External data sources push changes into the computation graph:

```csharp
// Sources are auto-generated for each property
context.SourceFilesSource.Push(new SourceFile("Program.cs", "..."));
context.SourceFilesSource.Remove(oldFile);
context.SourceFilesSource.Replace(oldFile, newFile);

// Changes trigger automatic cache invalidation and recomputation
```

### 2. **Computation Graph**
Automatic dependency tracking and invalidation:

- Providers form a directed acyclic graph (DAG)
- Changes propagate through dependencies automatically
- LRU cache for computed values
- Thread-safe concurrent access

### 3. **Incremental Providers**
Immutable pipeline structures that lazily compute values:

- `IncrementalValueProvider<T>` - Single value provider
- `IncrementalValuesProvider<T>` - Collection provider
- Composable via extension methods (Select, Where, Combine, etc.)
- Automatic caching and dependency tracking

### Source Generation

The source generators analyze `[ComputingContext]` classes and generate:

- **Source accessor properties** (e.g., `NameSource`, `ItemsSource`)
- **Push-based reactive sources** (`IncrementalSource<T>`, `IncrementalValueSource<T>`)
- **Automatic cache invalidation** infrastructure
- **Computation nodes** for each provider in the graph
- **Event subscriptions** for change notifications

### Key Differences from Mutation-Based Approaches

**No SaveChanges Required:**
```csharp
// Old (mutation-based):
context.AddItem(item);
context.SaveChanges();  // Explicit commit

// New (push-based):
context.ItemsSource.Push(item);  // Triggers automatically
```

**Reactive by Default:**
```csharp
// Outputs trigger automatically when sources change
context.RegisterOutput(context.Results, (results, ctx) => {
    // Called automatically when ItemsSource changes
});
```

## Documentation

- [Push-Based Architecture Research](PUSH_BASED_ARCHITECTURE_RESEARCH.md) - Detailed architecture documentation
- [Implementation Summary](IMPLEMENTATION_SUMMARY.md) - Implementation overview
- [Examples README](examples/README.md) - Guide to examples

## License

MIT
