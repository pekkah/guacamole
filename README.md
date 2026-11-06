# Guacamole

[![CI](https://github.com/pekkah/guacamole/actions/workflows/ci.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/ci.yml)
[![Publish](https://github.com/pekkah/guacamole/actions/workflows/publish.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/v/Guacamole.svg)](https://www.nuget.org/packages/Guacamole/)

A .NET framework for building incremental compilers and computation pipelines, inspired by Roslyn's incremental generators. Provides automatic dependency tracking, caching, and change propagation for efficient recomputation when inputs change.

## Features

- **Push-Based Reactive Architecture**: External code pushes changes into sources; registered output handlers run automatically
- **Incremental Computation**: Automatic dependency tracking with lazy, version-based cache validation
- **Provider-based API**: Familiar Roslyn-style `IncrementalValueProvider<T>` and `IncrementalValuesProvider<T>`
- **Source Generation**: Generates push-based source accessors for `[ComputingContext]` classes at compile time
- **Thread-safe building blocks**: Sources, the value cache and version bookkeeping are synchronized (see [Thread safety](#thread-safety) for what is and is not covered)
- **Performance**: Equality-based cut-off (unchanged values do not invalidate dependents), LRU value cache, BenchmarkDotNet benchmarks

## Quick Start

Install the packages:

```bash
dotnet add package Guacamole
dotnet add package Guacamole.SourceGenerators
```

> **Note**: `Guacamole` is the runtime library. `Guacamole.SourceGenerators` contains the source generator that implements `[ComputingContext]` classes. The runtime package does not depend on the generator package, so **install both**.

Define a computing context. Every `partial` get-only provider property becomes an input source:

```csharp
using Guacamole;

[ComputingContext]
public partial class MyCompilerContext : ComputingContextBase
{
    // Input: the generator implements this property and adds a SourceCodeSource accessor
    public partial IncrementalValueProvider<string> SourceCode { get; }
}
```

Build the pipeline **once**, then push data through the generated source accessor:

```csharp
using var context = new MyCompilerContext();

// Build the pipeline once and keep the provider
IncrementalValueProvider<int> tokenCount = context.SourceCode.Select(code => code.Split(' ').Length);

// Output handlers run whenever a source of the context changes (if the value changed)
context.RegisterOutput(tokenCount, (count, ctx) => Console.WriteLine($"Tokens: {count}"));

// Push data: the context is notified synchronously and runs its output handlers
context.SourceCodeSource.Set("let x = 42");

// Pull a value on demand (Result-based; use .Value to throw on error instead)
var result = tokenCount.GetValue();
if (result.IsSuccess)
{
    Console.WriteLine(result.Value);
}
```

### Build pipelines once

Every operator call (`Select`, `Where`, `Combine`, `Collect`, ...) creates a **new computation node with a unique ID**, and cached values are stored per node. Build a pipeline once (in a constructor or at startup) and keep the resulting provider. Avoid building pipelines inside property getters or per request. Identical operator calls are reused: calling the same operator on the same input with an equal delegate (a non-capturing lambda, a lambda that only captures `this`, or a method group on the same instance) returns the existing node. Lambdas that capture locals allocate a new closure per call, so they never match and create a new node each time. Such nodes start with an empty cache and stay registered in the context's graph.

The generator emits the context's public parameterless constructor, so you cannot declare your own parameterless constructor. To expose derived pipelines as properties, declare them as non-partial properties with a setter and assign them in a constructor that chains to `this()`:

```csharp
[ComputingContext]
public partial class BuildContext : ComputingContextBase
{
    // Inputs (generated): SourcesSource and OptionsSource accessors
    public partial IncrementalValuesProvider<SourceFile> Sources { get; }
    public partial IncrementalValueProvider<BuildOptions> Options { get; }

    // Derived (user-managed): not partial, has a setter
    public IncrementalValuesProvider<CompiledUnit> Compiled { get; private set; }
    public IncrementalValueProvider<BuildResult> Result { get; private set; }

    public BuildContext(Compiler compiler) : this()
    {
        Compiled = Sources.Select(compiler.Compile);
        Result = Compiled.Collect().Select(units => new BuildResult(units.Length));
    }
}
```

A get-only provider property that is not `partial` (for example `=> Sources.Select(...)`) is rejected with GUAC0003, which also steers you away from building pipelines in getters.

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

// Concatenate two collection providers
values.Merge(otherValues)
```

How these behave:

- **Lazy evaluation** - nothing is computed until a provider is read with `GetValue()`/`GetValues()` (or `.Value`/`.Values`), or an output handler reads it.
- **Caching happens at the nodes you read.** The provider you call `GetValue()`/`GetValues()` on caches its result together with the versions of the inputs it used. Intermediate stages that are never read directly are not cached: they are recomputed inline whenever the node you read is recomputed, and cache validation looks through them to the sources they depend on. To cache an intermediate stage, read it (for example by registering an output on it).
- **Version-based invalidation** - a source change only increments that source's version; cached values are revalidated lazily on the next read. A read node whose recomputed value equals its previous value keeps its version, so downstream nodes built from its cached value stay valid and are not recomputed.
- **No per-item caching** - a collection `Select`/`Where` reruns its lambda for every item when it is recomputed.
- **Custom equality comparers** decide whether a recomputed value counts as changed. Use records or `IEquatable<T>` types; without value equality every recomputation looks like a change.
- **Errors are values** - exceptions thrown by your lambdas become failed `Result`s; `GetValue()`/`GetValues()` do not throw them (`.Value`/`.Values` do).

## Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| **Guacamole** | Runtime: `ComputingContextBase`, sources, providers and operators, computation graph, caching, DI registration | [![NuGet](https://img.shields.io/nuget/v/Guacamole.svg)](https://www.nuget.org/packages/Guacamole/) |
| **Guacamole.SourceGenerators** | Source generator and diagnostics for `[ComputingContext]` classes (separate package; required) | [![NuGet](https://img.shields.io/nuget/v/Guacamole.SourceGenerators.svg)](https://www.nuget.org/packages/Guacamole.SourceGenerators/) |

## Requirements

- .NET 11 RC3 SDK (11.0.100-rc.3) or later
- C# 15 (LangVersion latest; partial properties require C# 13+)

## Building

```bash
dotnet build guacamole.sln
```

## Testing

```bash
dotnet test
```

Test projects use xUnit v3 on Microsoft.Testing.Platform (selected in `global.json`).

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

The tag name (without `v`) is the package version. Pushes to `main` only build, test and pack. Publishing uses nuget.org trusted publishing (no stored API key); see [docs/ci-cd.md](docs/ci-cd.md#nuget-trusted-publishing) for the one-time setup and details.

## Examples

The [`examples/`](examples/) directory contains runnable console projects. They are part of `guacamole.sln`, so CI builds them:

- [`examples/BasicSourceUsage`](examples/BasicSourceUsage/) - Pushing items, `Where`/`Collect`/`Select`, output handlers
- [`examples/FileWatcherExample`](examples/FileWatcherExample/) - Simulated file watcher pushing file changes
- [`examples/DataPipelineExample`](examples/DataPipelineExample/) - Multi-stage data transformation pipeline

Run an example with:
```bash
dotnet run --project examples/BasicSourceUsage
```

## Architecture

Guacamole is built on three core concepts:

### 1. **Push-Based Sources**
External code pushes changes into sources:

```csharp
// A source accessor is generated for each partial get-only provider property
context.SourcesSource.Push(new SourceFile("Program.cs", "..."));   // collection with set semantics: duplicates are ignored
context.SourcesSource.Remove(oldFile);
context.SourcesSource.Replace(oldFile, newFile);                   // throws if oldFile is missing or newFile already present
context.SourcesSource.Clear();
context.OptionsSource.Set(new BuildOptions("Release"));             // single value; setting an equal value is a no-op

// Each effective change increments the source's version and runs the context's output handlers
```

### 2. **Computation Graph**
Automatic dependency tracking and lazy invalidation:

- Providers form a directed acyclic graph (DAG); every operator call adds a new node
- A source change increments that source's version; dependents are revalidated lazily when read
- Computed values live in an LRU cache (10,000 entries per context graph)

### 3. **Incremental Providers**
Immutable pipeline structures that lazily compute values:

- `IncrementalValueProvider<T>` - Single value provider
- `IncrementalValuesProvider<T>` - Collection provider
- Composable via extension methods (Select, Where, Combine, etc.)
- Values are cached for the nodes that are read (see [Provider Extension Methods](#provider-extension-methods))

### Source Generation

For each `[ComputingContext]` class the generator emits one file containing:

- A public parameterless constructor that creates a collection source (for `IncrementalValuesProvider<T>` properties) or a single-value source (for `IncrementalValueProvider<T>` properties) per input property through the protected `CreateSource<T>`/`CreateValueSource<T>` helpers of `ComputingContextBase`, which register the source's node in the graph and subscribe the context to the source's changes
- The implementation of each partial provider property (returns the source's provider)
- A `{Property}Source` accessor typed `IIncrementalSource<T>` or `IIncrementalValueSource<T>`

The generator also reports diagnostics (GUAC0003-GUAC0009) for invalid declarations; see [Generated Code Reference](docs/generated-code-reference.md#diagnostics).

### Output Handlers

```csharp
context.RegisterOutput(provider, (value, ctx) => { /* ... */ });
```

- Handlers run synchronously, on the thread that changed a source, after **any** source of the context changes. They are not run at registration time.
- A handler is invoked only if its provider computes successfully and the value differs from the last value delivered to that handler (`EqualityComparer<T>.Default` for single values, element-wise `SequenceEqual` for collections). `ImmutableArray<T>` (the result of `Collect()`) compares by reference, so a handler on a collected provider runs whenever it is recomputed.
- All handlers run even if one throws; afterwards the exceptions are rethrown as one `AggregateException` from the `Push`/`Set`/... call that triggered them.
- `Dispose()` unsubscribes the context from its sources and drops its output registrations.

### Thread Safety

What is synchronized:

- Each source guards its contents with its own `ReaderWriterLockSlim`, so `Push`/`Remove`/`Replace`/`Clear`/`Set` and `GetSnapshot`/`GetValue` can be called concurrently.
- The LRU value cache locks internally, version counters and node registries use concurrent collections, and storing a computed value together with its version information is a single atomic step.
- Registering outputs, subscribing sources and `Dispose()` are synchronized with each other; a change notification that races with `Dispose()` is ignored.

What is not:

- Change notifications and output handlers run synchronously on the caller's thread with no lock held, so handlers triggered by concurrent changes can run concurrently. Make handlers thread-safe or serialize your writes.
- Reading a provider does not lock the pipeline: two threads reading the same stale node may both compute it. Keep your lambdas pure.
- `BeginBatch()` returns a buffer that is not thread-safe; `Commit()` applies its operations one by one, each with its own notification.

### Namespaces

Everything a typical application uses (contexts, providers and their operators, sources, `Result`, `ComputationError`, `ProviderMetadata`, the dependency-injection extensions) is in the `Guacamole` namespace, so `using Guacamole;` is all you need. The only other public namespace is `Guacamole.Diagnostics` (`MetricsConfiguration` and the `GuacamoleMetrics` instruments). The computation graph, nodes, value cache and concrete source classes are internal implementation details. See the [Migration Guide](MIGRATION_GUIDE.md) for the old namespaces and the members that became internal.

## Documentation

- [Quickstart](docs/quickstart.md) - Step-by-step introduction
- [Generated Code Reference](docs/generated-code-reference.md) - What the generator emits, and its diagnostics
- [Performance Guide](docs/performance-guide.md) - How caching works and how to get the most out of it
- [CI/CD](docs/ci-cd.md) - Build, test and release workflow
- [Examples README](examples/README.md) - Guide to the examples
- [Migration Guide](MIGRATION_GUIDE.md) - Breaking API changes: namespace consolidation, and moving from the removed mutation/`SaveChanges` API
- [Changelog](CHANGELOG.md)
- [Push-Based Architecture Research](PUSH_BASED_ARCHITECTURE_RESEARCH.md) - Design research (historical; parts of it differ from the implementation)

## License

MIT
