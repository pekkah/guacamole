# Guacamole

[![CI](https://github.com/pekkah/guacamole/actions/workflows/ci.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/ci.yml)
[![Publish](https://github.com/pekkah/guacamole/actions/workflows/publish.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/v/Guacamole.svg)](https://www.nuget.org/packages/Guacamole/)

A .NET framework for building incremental compilers and computation pipelines, inspired by Roslyn's incremental generators. Provides automatic dependency tracking, caching, and change propagation for efficient recomputation when inputs change.

## Features

- **Incremental Computation**: Automatic dependency tracking and selective recomputation
- **Provider-based API**: Familiar Roslyn-style `IncrementalValueProvider<T>` and `IncrementalValuesProvider<T>`
- **Source Generation**: Generate context classes with mutation methods and change tracking at compile time
- **Thread-safe**: Built-in reader/writer locking for concurrent access
- **Dependency Injection**: First-class DI support with Microsoft.Extensions.DependencyInjection
- **Performance**: LRU caching, optimized graph invalidation, comprehensive benchmarks

## Quick Start

Install the package:

```bash
dotnet add package Guacamole
```

Define a computing context:

```csharp
using Guacamole;
using Guacamole.Core.Providers;

[ComputingContext]
public partial class MyCompilerContext : ComputingContextBase
{
    public IncrementalValueProvider<string> SourceCode { get; }
    public IncrementalValueProvider<ParseTree> ParsedTree { get; }

    public MyCompilerContext(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        SourceCode = CreateInput<string>();
        ParsedTree = SourceCode.Select(Parse);
    }

    private ParseTree Parse(string source) => /* ... */;
}
```

Use the context with dependency injection:

```csharp
var services = new ServiceCollection();
services.AddComputingContext<MyCompilerContext>();
var provider = services.BuildServiceProvider();

var context = provider.GetComputingContext<MyCompilerContext>();

// Update input and propagate changes
context.UpdateSourceCode("let x = 42");
context.SaveChanges(); // Triggers incremental recomputation

var tree = context.ParsedTree.Value;
```

## Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| **Guacamole** | Complete framework including `ComputingContextBase`, incremental computation engine, and provider APIs. Installing this package automatically includes the source generators. | [![NuGet](https://img.shields.io/nuget/v/Guacamole.svg)](https://www.nuget.org/packages/Guacamole/) |
| **Guacamole.SourceGenerators** | Source generators for code generation (automatically installed with Guacamole) | [![NuGet](https://img.shields.io/nuget/v/Guacamole.SourceGenerators.svg)](https://www.nuget.org/packages/Guacamole.SourceGenerators/) |

## Requirements

- .NET 10 RC1 or later
- C# 13

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

## Examples

See the [`examples/`](examples/) directory for complete working examples:

- [`CompilerContext`](examples/CompilerContext/) - Incremental compiler pipeline
- [`FileWatcherContext`](examples/FileWatcherContext/) - File system monitoring with incremental updates

## Architecture

Guacamole is built on three core concepts:

1. **Computing Contexts** - User-defined contexts that inherit from `ComputingContextBase` and define incremental providers
2. **Computation Graph** - Automatic dependency tracking and invalidation via `ComputationGraph`
3. **Providers** - Immutable pipeline structures (`IncrementalValueProvider<T>`, `IncrementalValuesProvider<T>`) that lazily compute values

The source generators analyze `[ComputingContext]` classes and generate:
- Type-safe mutation methods (e.g., `UpdateSourceCode()`)
- Change tracking infrastructure
- `SaveChanges()` method for batched updates
- Computation nodes for each provider

## License

MIT