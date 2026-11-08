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

## Installation

Guacamole requires .NET 11 (see [Requirements](#requirements)). Install both packages:

```bash
dotnet add package Guacamole --prerelease
dotnet add package Guacamole.SourceGenerators --prerelease
```

> **Note**: `Guacamole` is the runtime library. `Guacamole.SourceGenerators` contains the source generator that implements `[ComputingContext]` classes. The runtime package does not depend on the generator package, so **install both**. The generator is a development dependency: it runs at compile time only and is not passed on to projects that reference yours.

`--prerelease` installs the latest preview: prerelease versions are published to nuget.org from `main`, and stable versions from release tags. Until the first stable release only previews exist, so `dotnet add package` without `--prerelease` finds no version; afterwards, omit it to get the latest stable version. In a `PackageReference`, give a preview version from nuget.org explicitly.

The [changelog](https://github.com/pekkah/guacamole/blob/main/CHANGELOG.md) lists the changes in each version, and the [packages](#packages) table links to nuget.org.

## Quick Start

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

// Pull a value on demand (Result-based; use tokenCount.Value to throw on error instead)
var result = tokenCount.GetValue();
if (result.IsSuccess)
{
    Console.WriteLine(result.ValueOrDefault);
}
```

### Build pipelines once

Every operator call (`Select`, `Where`, `Combine`, `Collect`, ...) creates a **new computation node with a unique ID**, and cached values are stored per node. Build a pipeline once (in a constructor or at startup) and keep the resulting provider. Avoid building pipelines inside property getters or per request. Identical operator calls are reused: calling the same operator on the same input with an equal delegate (a non-capturing lambda, a lambda that only captures `this`, or a method group on the same instance) returns the existing node. Lambdas that capture locals allocate a new closure per call, so they never match and create a new node each time. Such nodes start with an empty cache and stay registered in the context's graph until you call `context.TrimUnusedNodes()` (or the context does it automatically, if it overrides `AutoTrimInterval`), which removes the nodes no provider, output registration or other node references anymore (see [Performance Guide](https://github.com/pekkah/guacamole/blob/main/docs/performance-guide.md#build-pipelines-once)).

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

// Filter a single value: a rejected value becomes "no value" (not an error)
provider.Where(x => x > 0)
provider.Where(x => x.IsValid, customComparer)
if (provider.Where(x => x > 0).TryGetValue(out var positive)) { /* ... */ }

// Combine two providers
first.Combine(second)  // Returns IncrementalValueProvider<(TFirst, TSecond)>

// Transform to multiple values
provider.SelectMany(x => x.Children)
provider.SelectMany(x => x.Items, customComparer)

// Look up one key of a dictionary (e.g. from CollectToDictionary): only a change of that key's
// entry reaches the lookup and the stages after it
table.Lookup("List")           // IncrementalValueProvider<TValue>, "no value" while the key is absent
table.LookupOrDefault("List")  // IncrementalValueProvider<TValue?>, default while the key is absent
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
values.Collect()  // Returns IncrementalValueProvider<ImmutableArray<T>>, compared element-wise
values.Collect(customComparer)  // Compares the collected items with customComparer

// Collect to an immutable dictionary, updated from the changed items only
values.CollectToDictionary(x => x.Name, x => x.Path)  // IncrementalValueProvider<ImmutableDictionary<TKey, TValue>>
values.CollectToDictionary(x => x.Name, x => x.Path, conflictComparer: StringComparer.Ordinal)  // smallest value wins a duplicate key

// Combine with single value
values.Combine(singleValue)  // Pairs each value with the single value

// Concatenate two collection providers (first, then second)
values.Merge(otherValues)
values.Merge(otherValues, customComparer)  // Compares the values with customComparer

// Flatten collections of collections
arrays.Flatten()   // IncrementalValuesProvider<ImmutableArray<T>> -> IncrementalValuesProvider<T>
nested.Flatten()   // IncrementalValueProvider<ImmutableArray<ImmutableArray<T>>> -> IncrementalValuesProvider<T>
```

`Flatten()` is shorthand for `SelectMany(array => array)`: on a collection of arrays it is as incremental as `SelectMany` (only new or changed arrays are flattened again); on a single nested array it flattens the whole value whenever it changes. `Merge` concatenates both inputs into a new array whenever either changes (a memory copy), and passes on their item-level deltas, so the stages after it only rerun for the changed items.

How these behave:

- **Lazy evaluation** - nothing is computed until a provider is read with `GetValue()`/`GetValues()` (or `.Value`/`.Values`), or an output handler reads it.
- **Every stage is cached.** Each stage of a pipeline caches its result together with the versions of the inputs it read, whether or not you read that stage directly. Reading a provider first brings the stages it was computed from up to date (recursively), so a stage reruns only when one of its own inputs actually changed.
- **Version-based invalidation** - a source change only increments that source's version; cached values are revalidated lazily on the next read. A stage whose recomputed value equals its previous value (by its comparer) keeps its version, so the stages downstream of it stay valid and are not recomputed. This equality cut-off applies at every stage, including intermediate stages that are never read themselves.
- **Per-item memoization** - a recomputed collection `Select`/`Where`/`SelectMany` runs its lambda only for new or changed items; items equal to ones seen in the previous computation reuse the previous result (the same instance). Items should be immutable.
- **Cross-item lookups** (e.g. a symbol table built from all files) - compute shared data from all items, with `CollectToDictionary` for a lookup table (updated in proportion to the changed items, with a deterministic rule for duplicate keys) or `Collect()` and a deterministic merge into a type with structural equality, then `Combine` it with each item; never accumulate it in a lambda, which makes results depend on processing order. For single-value stages that each need a few entries of a table, `table.Lookup(key)` depends on that key only, so an edit reruns only the lookups of the keys whose entries changed. See [Cross-item lookups](https://github.com/pekkah/guacamole/blob/main/docs/performance-guide.md#cross-item-lookups-eg-symbol-tables).
- **Custom equality comparers** decide whether a recomputed value counts as changed. Use records or `IEquatable<T>` types; without value equality every recomputation looks like a change.
- **Errors are values** - exceptions thrown by your lambdas become failed `Result`s; `GetValue()`/`GetValues()` do not throw them (`.Value`/`.Values` and `TryGetValue` do, as `InvalidOperationException`). Downstream stages pass the error on without running their lambdas. Failed results (and `null` values) are cached like values: a failing stage runs again only when one of its inputs changes, and an error equal to the previous one (same messages, inner errors, location and diagnostic ID) keeps the stages downstream of it cached. See [Every stage is cached](https://github.com/pekkah/guacamole/blob/main/docs/performance-guide.md#every-stage-is-cached).
- **A filtered-out single value is "no value", not an error.** When a single-value `Where` rejects its input, `GetValue()` returns a `Result` with `IsNone == true` (`IsSuccess` and `IsError` are false). Downstream `Select`/`Where`/`Combine` propagate "no value" without running their lambdas; `SelectMany` and collection `Combine` produce no items; output handlers are not called. `.Value` throws `InvalidOperationException`; use `TryGetValue(out value)`, which returns `false` for no value (and throws only if the computation failed).
- **`Result.ValueOrDefault` never throws.** It returns the value on success and `default` for None or an error, so check `IsSuccess` (or use `Match`) first. The providers' `.Value`/`.Values` are the throwing accessors.

## Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| **Guacamole** | Runtime: `ComputingContextBase`, sources, providers and operators, computation graph, caching, DI registration | [![NuGet](https://img.shields.io/nuget/v/Guacamole.svg)](https://www.nuget.org/packages/Guacamole/) |
| **Guacamole.SourceGenerators** | Source generator and diagnostics for `[ComputingContext]` classes (separate package; required) | [![NuGet](https://img.shields.io/nuget/v/Guacamole.SourceGenerators.svg)](https://www.nuget.org/packages/Guacamole.SourceGenerators/) |

## Requirements

- **.NET 11.** The `Guacamole` package targets `net11.0`, so projects that use it must target .NET 11 or later.
- **The .NET 11 SDK** (or Visual Studio / Rider with the matching compiler): `Guacamole.SourceGenerators` is built against Roslyn 5.9 (Microsoft.CodeAnalysis 5.9.0), and an older compiler cannot load it. Use the .NET 11 RC3 SDK (`11.0.100-rc.3`) or later; the packages are currently built and tested with RC3 (pinned in this repository's `global.json`).
- **C# 13 or later** for partial properties. Projects targeting `net11.0` default to C# 15.

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

The tag name (without `v`) is the package version. Pushes to `main` only build, test and pack. Publishing uses nuget.org trusted publishing (no stored API key); see [docs/ci-cd.md](https://github.com/pekkah/guacamole/blob/main/docs/ci-cd.md#nuget-trusted-publishing) for the one-time setup and details.

## Examples

The [`examples/`](https://github.com/pekkah/guacamole/tree/main/examples/) directory contains runnable console projects. They are part of `guacamole.sln`, so CI builds them:

- [`examples/BasicSourceUsage`](https://github.com/pekkah/guacamole/tree/main/examples/BasicSourceUsage/) - Pushing items, `Where`/`Collect`/`Select`, output handlers
- [`examples/FileWatcherExample`](https://github.com/pekkah/guacamole/tree/main/examples/FileWatcherExample/) - Simulated file watcher pushing file changes
- [`examples/DataPipelineExample`](https://github.com/pekkah/guacamole/tree/main/examples/DataPipelineExample/) - Multi-stage data transformation pipeline
- [`examples/CrossFileSymbolsExample`](https://github.com/pekkah/guacamole/tree/main/examples/CrossFileSymbolsExample/) - Cross-file symbol resolution with `CollectToDictionary`, `Combine` and `LookupOrDefault`

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
context.SourcesSource.Push(new SourceFile("Program.cs", "..."));   // collection with set semantics: duplicates are ignored, items keep insertion order
context.SourcesSource.Remove(oldFile);
context.SourcesSource.Replace(oldFile, newFile);                   // newFile takes oldFile's position; throws if oldFile is missing (even when newFile equals it) or newFile already present; an equal newFile for a present oldFile is a no-op
context.SourcesSource.Clear();
context.OptionsSource.Set(new BuildOptions("Release"));             // single value; setting an equal value is a no-op

// Each effective change increments the source's version and runs the context's output handlers
```

### 2. **Computation Graph**
Automatic dependency tracking and lazy invalidation:

- Providers form a directed acyclic graph (DAG); every operator call adds a new node
- A source change increments that source's version; dependents are revalidated lazily when read
- Computed values live in an LRU cache per context: 10,000 entries by default; override `protected override int CacheCapacity => ...;` on the context to change it
- Unused pipeline nodes are removed by `context.TrimUnusedNodes()`; override `protected override int AutoTrimInterval => ...;` (off by default) to have the context trim automatically as nodes are added

### 3. **Incremental Providers**
Immutable pipeline structures that lazily compute values:

- `IncrementalValueProvider<T>` - Single value provider
- `IncrementalValuesProvider<T>` - Collection provider
- Composable via extension methods (Select, Where, Combine, etc.)
- Every stage caches its value; a stage whose recomputed value is unchanged keeps downstream stages cached (see [Provider Extension Methods](#provider-extension-methods))

### Source Generation

For each `[ComputingContext]` class the generator emits one file containing:

- A public parameterless constructor that creates a collection source (for `IncrementalValuesProvider<T>` properties) or a single-value source (for `IncrementalValueProvider<T>` properties) per input property through the protected `CreateSource<T>`/`CreateValueSource<T>` helpers of `ComputingContextBase`, which register the source's node in the graph and subscribe the context to the source's changes
- The implementation of each partial provider property (returns the source's provider)
- A `{Property}Source` accessor typed `IIncrementalSource<T>` or `IIncrementalValueSource<T>`

The generator also reports diagnostics (GUAC0003-GUAC0009) for invalid declarations; see [Generated Code Reference](https://github.com/pekkah/guacamole/blob/main/docs/generated-code-reference.md#diagnostics).

### Output Handlers

```csharp
IDisposable registration = context.RegisterOutput(provider, (value, ctx) => { /* ... */ });
// later: stop just this handler
registration.Dispose();
```

- Handlers run after **any** source of the context changes, not at registration time. A value is delivered synchronously by the thread whose `Push`/`Set`/... changed the source, before that call returns, unless a delivery to the same handler is already running: then the call returns at once and the delivering thread calls the handler again with the latest value after it returns (see [Thread Safety](#thread-safety)).
- A handler is invoked only if its provider computes a value (not an error, and not "no value" from a `Where`; a `null` value is delivered) and the value differs from the last value delivered to that handler; after a "no value" phase, a reappearing value is delivered again even if it equals the last one (the provider's comparer when it was created with one, e.g. `Select(selector, comparer)`, otherwise `EqualityComparer<T>.Default`; element-wise for collections). `Collect()` results compare element-wise (in order), so a handler on a collected provider does not run for a re-collect with equal items; `ImmutableArray<T>` values compare element-wise and `ImmutableDictionary<TKey, TValue>`/`ImmutableSortedDictionary<TKey, TValue>` values entry by entry (see [Equality](https://github.com/pekkah/guacamole/blob/main/docs/performance-guide.md#equality)); list and other dictionary values returned by a `Select` compare by reference.
- All handlers run even if one throws; afterwards the exceptions are rethrown as one `AggregateException` (all handler exceptions as its inner exceptions) from the `Push`/`Set`/... call on the thread that delivered them, which is not the triggering call when the delivery was handed to another thread.
- `RegisterOutput` returns an `IDisposable`; disposing it unregisters that handler only (thread-safe, idempotent; deliveries that start afterwards, and deliveries on the thread that disposed it, do not call the handler, but a delivery in progress on another thread may still make one call just after `Dispose` returns). Collection handlers receive an `IReadOnlyList<T>`.
- `Dispose()` on the context unsubscribes it from its sources and drops all of its output registrations. A disposed context no longer tracks source changes, so instead of returning stale values, reading its providers (`GetValue`, `Value`, `TryGetValue`, `GetValues`, `Values`) and changing its sources (`Set`, `Push`, `PushRange`, `Remove`, `Replace`, `Clear`, `BeginBatch`, batch `Commit`) throw `ObjectDisposedException`. Disposing a batch that is still pending discards its operations without throwing, so `using` blocks end cleanly. The sources' own `GetValue`/`GetSnapshot` keep working.

### Thread Safety

What is synchronized:

- Each source guards its contents with its own lock (collection sources publish an immutable snapshot, so their reads take no lock), so `Push`/`Remove`/`Replace`/`Clear`/`Set` and `GetSnapshot`/`GetValue` can be called concurrently.
- The LRU value cache locks internally, version counters and node registries use concurrent collections, and storing a computed value together with its version information is a single atomic step.
- Registering outputs, subscribing sources and `Dispose()` are synchronized with each other; a change notification that races with `Dispose()` is ignored.
- Each handler's own calls are serialized: it is never called concurrently with itself or re-entrantly, never receives the same value twice in a row, and once all concurrent `Push`/`Set`/... calls have returned, the last value it received is its provider's current value. A change that arrives while the handler runs does not wait for it: the thread running the handler calls it again with the then current value after it returns (so the handler, and any exception it throws, may run on another thread than the one that made the change, and intermediate values may be skipped). A handler that changes a source is called again with the new value after it returns, not from within itself. A `Push`/`Set`/... call that is delivering keeps delivering while other threads keep changing sources, so with slow handlers and continuous concurrent writes its latency is unbounded.

What is not:

- Change notifications and output handlers run synchronously with no lock of the context held. Different handlers can run concurrently with each other, so handlers that share state must synchronize it.
- Reading a provider does not lock the pipeline: two threads reading the same stale node may both compute it. Keep your lambdas pure.
- `BeginBatch()` returns a buffer that is not thread-safe; `Commit()` applies its operations atomically under the source's lock (all or nothing), with a single notification.

### Namespaces

Everything a typical application uses (contexts, providers and their operators, sources, `Result`, `ComputationError`, `ProviderMetadata`, the dependency-injection extensions) is in the `Guacamole` namespace, so `using Guacamole;` is all you need. The only other public namespace is `Guacamole.Diagnostics` (`MetricsConfiguration` and the `GuacamoleMetrics` instruments). The computation graph, nodes, value cache and concrete source classes are internal implementation details. See the [Migration Guide](https://github.com/pekkah/guacamole/blob/main/MIGRATION_GUIDE.md) for the old namespaces and the members that became internal.

## Documentation

- [Quickstart](https://github.com/pekkah/guacamole/blob/main/docs/quickstart.md) - Step-by-step introduction
- [Generated Code Reference](https://github.com/pekkah/guacamole/blob/main/docs/generated-code-reference.md) - What the generator emits, and its diagnostics
- [Performance Guide](https://github.com/pekkah/guacamole/blob/main/docs/performance-guide.md) - How caching works and how to get the most out of it
- [CI/CD](https://github.com/pekkah/guacamole/blob/main/docs/ci-cd.md) - Build, test and release workflow
- [Examples README](https://github.com/pekkah/guacamole/blob/main/examples/README.md) - Guide to the examples
- [Migration Guide](https://github.com/pekkah/guacamole/blob/main/MIGRATION_GUIDE.md) - Breaking API changes: namespace consolidation, and moving from the removed mutation/`SaveChanges` API
- [Changelog](https://github.com/pekkah/guacamole/blob/main/CHANGELOG.md)
- [Push-Based Architecture Research](https://github.com/pekkah/guacamole/blob/main/PUSH_BASED_ARCHITECTURE_RESEARCH.md) - Design research (historical; parts of it differ from the implementation)

## License

[MIT](https://github.com/pekkah/guacamole/blob/main/LICENSE)
