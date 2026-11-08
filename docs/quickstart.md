# Guacamole Quickstart Guide

This guide walks you through building incremental computation pipelines with Guacamole.

## Table of Contents

- [What is Guacamole?](#what-is-guacamole)
- [Installation](#installation)
- [Core Concepts](#core-concepts)
- [Your First Computing Context](#your-first-computing-context)
- [Building Pipelines](#building-pipelines)
- [Pushing Changes](#pushing-changes)
- [Understanding Caching](#understanding-caching)
- [Output Handlers](#output-handlers)
- [Troubleshooting](#troubleshooting)
- [Next Steps](#next-steps)

## What is Guacamole?

Guacamole is a .NET framework for building **incremental computation pipelines**. Inspired by Roslyn's incremental generators, it tracks dependencies between computations and only recomputes what is needed when inputs change.

Typical uses: incremental compilers, build tools, file processing and data transformation pipelines.

## Installation

### Prerequisites

- .NET 11 RC3 SDK (11.0.100-rc.3) or later
- C# 15 (LangVersion latest; partial properties require C# 13+)

### Install via NuGet

```bash
dotnet add package Guacamole
dotnet add package Guacamole.SourceGenerators
```

Both packages are required: `Guacamole` is the runtime, and `Guacamole.SourceGenerators` contains the source generator that implements your `[ComputingContext]` classes. The runtime package does not pull in the generator.

## Core Concepts

### 1. Computing Context

A **computing context** is a `partial` class that:
- derives from `ComputingContextBase`
- is marked with `[ComputingContext]`
- declares its **inputs** as `partial` get-only provider properties

For every input property the source generator creates a push-based source and a `{Property}Source` accessor.

### 2. Sources

Sources are where data enters the graph:

- `IIncrementalSource<T>` (for `IncrementalValuesProvider<T>` inputs): `Push`, `Remove`, `Replace`, `Clear`, `GetSnapshot`. Items are unique and enumerated in insertion order (`Replace` keeps the position; `Remove` then `Push` appends).
- `IIncrementalValueSource<T>` (for `IncrementalValueProvider<T>` inputs): `Set`, `GetValue`

### 3. Providers

**Providers** are immutable descriptions of a value in the pipeline:

- **`IncrementalValueProvider<T>`** - a single value (`GetValue()`, `.Value`)
- **`IncrementalValuesProvider<T>`** - a collection of values (`GetValues()`, `.Values`); the values are an `IReadOnlyList<T>` (indexable, with `Count`), a read-only view of the cached result (copy them with `ToArray()` if you need an array to modify)

Providers are **lazy**: nothing is computed until a provider is read.

### 4. Computation Graph

Each context owns a computation graph. Every operator call (`Select`, `Where`, ...) adds a node to it. Changing a source increments that source's version; values computed from it are revalidated lazily the next time they are read.

## Your First Computing Context

### Step 1: Define Your Context

```csharp
using Guacamole;

namespace MyCompiler;

[ComputingContext]
public partial class CompilerContext : ComputingContextBase
{
    // Partial + get-only = input. The generator implements it.
    public partial IncrementalValueProvider<string> SourceCode { get; }
}
```

- `[ComputingContext]` tells the source generator to process the class.
- The class (and any class it is nested in) must be `partial`, and it must derive from `ComputingContextBase`.
- `public partial IncrementalValueProvider<string> SourceCode { get; }` declares an input.

### Step 2: What Gets Generated

For the context above, the generator emits (simplified; see [Generated Code Reference](generated-code-reference.md)):

```csharp
partial class CompilerContext
{
    private global::Guacamole.IIncrementalValueSource<string>? _SourceCodeSource;

    public CompilerContext() : base()
    {
        // creates the source, registers its node in the context's graph and subscribes to its changes
        _SourceCodeSource = CreateValueSource<string>("CompilerContext.SourceCode");
    }

    public partial global::Guacamole.IncrementalValueProvider<string> SourceCode
        => _SourceCodeSource.AsProvider();

    public global::Guacamole.IIncrementalValueSource<string> SourceCodeSource
        => _SourceCodeSource;
}
```

Note that the generator emits the **public parameterless constructor**; you cannot declare your own (see [Derived properties on the context](#derived-properties-on-the-context)).

### Step 3: Use Your Context

```csharp
using var context = new CompilerContext();

// Push input
context.SourceCodeSource.Set("let x = 42");

// Read it back through the provider
var result = context.SourceCode.GetValue();
if (result.IsSuccess)
{
    Console.WriteLine($"Source: {result.ValueOrDefault}");
}
```

Reading a single-value input before anything was `Set` returns a failed `Result` ("has never been initialized with a value").

Contexts are ordinary classes created with `new`. If you use dependency injection, register them like any other service (for example `services.AddScoped<CompilerContext>()`). Contexts are `IDisposable`.

## Building Pipelines

Transform providers with LINQ-style operators:

```csharp
var context = new CompilerContext();

IncrementalValueProvider<string[]> tokens = context.SourceCode.Select(code => code.Split(' '));
IncrementalValueProvider<int> tokenCount = tokens.Select(t => t.Length);
```

**Single value (`IncrementalValueProvider<T>`):** `Select`, `Where`, `Combine(other)`, `SelectMany` (returns a collection provider).

**Collection (`IncrementalValuesProvider<T>`):** `Select`, `Where`, `SelectMany`, `Collect()` (returns `IncrementalValueProvider<ImmutableArray<T>>`, compared element-wise; `Collect(comparer)` compares the items with a custom comparer), `Combine(singleValue)`, `Merge(other)` / `Merge(other, comparer)` (first, then second), `Flatten()` (an `IncrementalValuesProvider<ImmutableArray<T>>`, or an `IncrementalValueProvider<ImmutableArray<ImmutableArray<T>>>`, to an `IncrementalValuesProvider<T>`).

`Select`, `Where` and `SelectMany` have overloads that take an `IEqualityComparer<T>` for the result values.

A single-value `Where` may leave its provider with **no value**. That is not an error: `GetValue()` returns a `Result` whose `IsNone` is true, downstream operators skip their lambdas, and output handlers don't run. `.Value` throws in that case, so read possibly-filtered providers with `TryGetValue`:

```csharp
IncrementalValueProvider<string> nonEmpty = context.SourceCode.Where(code => code.Length > 0);

if (nonEmpty.Select(code => code.Split(' ')).TryGetValue(out var words))
{
    Console.WriteLine($"{words.Length} words");
}
```

### Build pipelines once

Every operator call creates a **new node with a unique ID**, and cached values are stored per node. So:

- Build each pipeline **once** (in a constructor or at startup) and keep the provider.
- Avoid building pipelines in property getters, per request or in a loop. Identical operator calls are reused: calling the same operator on the same input with an equal delegate (a non-capturing lambda, a lambda that only captures `this`, or a method group on the same instance) returns the existing node. Lambdas that capture locals allocate a new closure per call, so they never match and create a new node each time. New nodes start with an empty cache and stay registered in the context's graph until `TrimUnusedNodes()` removes them (see the [Performance Guide](performance-guide.md#build-pipelines-once)).

### Derived properties on the context

To expose derived providers as properties of the context, declare them **non-partial with a setter** (user-managed) and assign them in a constructor that chains to the generated `this()`:

```csharp
public record SourceFile(string Path, string Content);
public record CompiledUnit(string Path, int Tokens);
public record BuildResult(int Units);
public record BuildOptions(string Configuration);

public class Compiler
{
    public CompiledUnit Compile(SourceFile file) => new(file.Path, file.Content.Split(' ').Length);
}

[ComputingContext]
public partial class BuildContext : ComputingContextBase
{
    // Inputs
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

Property rules enforced by the generator:

| Declaration | Meaning |
|-------------|---------|
| `public partial IncrementalValuesProvider<T> X { get; }` | Input; generator implements it and adds `XSource` |
| `public IncrementalValuesProvider<T> X { get; set; }` (or `private set`) | User-managed; generator ignores it |
| `public partial IncrementalValuesProvider<T> X { get; set; }` | Error GUAC0005 |
| `public IncrementalValuesProvider<T> X { get; }` or `=> ...` | Error GUAC0003 |

Alternatively, keep the context minimal and build pipelines next to it, as the [examples](../examples/) do.

## Pushing Changes

There is no commit step: every source operation takes effect immediately.

```csharp
using Guacamole; // also brings the PushRange and BeginBatch extension methods into scope

var context = new BuildContext(new Compiler());

// Collection inputs (set semantics, using the item's equality; items are enumerated in insertion order)
context.SourcesSource.Push(new SourceFile("Main.cs", "a b"));   // pushing an equal item again is a no-op
context.SourcesSource.PushRange(new[] { new SourceFile("Utils.cs", "c") });
context.SourcesSource.Replace(
    new SourceFile("Utils.cs", "c"),
    new SourceFile("Utils.cs", "c d"));                          // keeps the old item's position; throws if the old item is missing (even if the new one equals it) or the new one exists; an equal new item for a present old one is a no-op
context.SourcesSource.Remove(new SourceFile("Utils.cs", "c d")); // removing a missing item is a no-op
IReadOnlyCollection<SourceFile> current = context.SourcesSource.GetSnapshot();

// Single-value inputs
context.OptionsSource.Set(new BuildOptions("Release"));         // setting an equal value is a no-op; null throws
```

Each operation that actually changes the source raises the source's `Changed` event. The context then increments the source's version and runs its output handlers, synchronously, before the call returns.

### Batches

`BeginBatch()` buffers operations and applies them on `Commit()`:

```csharp
using (var batch = context.SourcesSource.BeginBatch())
{
    batch.Push(new SourceFile("A.cs", "x"));
    batch.Push(new SourceFile("B.cs", "y"));
    batch.Commit();
}
```

- `Commit()` applies the operations in order in one step, with a single change notification (none if the source ends up unchanged, for example after a `Push` and a `Remove` of the same new item).
- It is all or nothing: if an operation would fail (`Replace` of a missing item, even with an equal replacement, or with an item that is present, at that point of the batch), `Commit()` throws and nothing is applied.
- After `Commit()` the batch is done: adding operations throws `InvalidOperationException`.
- `Rollback()` discards the buffered operations.
- Disposing a batch that was neither committed nor rolled back **commits** it.

## Understanding Caching

1. **First read** - Reading a provider computes its value and caches it, together with the versions of the inputs it used.
2. **Cache hits** - Later reads return the cached value while those input versions are unchanged.
3. **Source changes** - A change only increments the source's version; nothing is cleared eagerly.
4. **Recomputation** - The next read of an affected provider brings the stages it depends on up to date and recomputes those whose inputs changed. A stage whose new value equals the old one keeps its version, so the stages downstream of it stay valid and are not recomputed (equality cut-off), whether or not that stage is read itself.

### What is cached

Every stage of a pipeline is cached, whether you read it (with `GetValue()`/`GetValues()`, `.Value`/`.Values`, or through an output handler) or only read the stages built from it.

For example, with `Result = Sources.Select(Compile).Collect().Select(Link)` and only `Result` being read, a change to `Sources` reruns `Compile` for the **changed** files only, then `Link`, unless the compiled results are equal to the previous ones. Collection operators (`Select`, `Where`, `SelectMany`) memoize their lambda per item: items equal to ones seen in their previous computation reuse the previous result, so keep items immutable and lambdas pure.

### Cross-item lookups (e.g. symbol tables)

When a per-item stage needs data from other items (type checking a file needs every file's declarations), compute that data from all items and combine it back, instead of accumulating it in a lambda (which makes results depend on processing order):

```csharp
var symbols = Parsed
    .Select(FileDeclarations.Extract)   // per file
    .Collect()                          // all files
    .Select(SymbolTable.Merge);         // sort by path inside Merge; SymbolTable has structural equality

var typeChecked = Parsed
    .Combine(symbols)
    .Select(pair => (File: pair.Left, Symbols: pair.Right.Restrict(pair.Left.ReferencedNames))) // optional narrowing
    .Select(pair => TypeCheck(pair.File, pair.Symbols));
```

An edit that leaves the table equal (for example a comment) does not rerun other files' type checks, and the optional `Restrict` keeps unaffected files' inputs equal when unrelated declarations change. See [Cross-item lookups](performance-guide.md#cross-item-lookups-eg-symbol-tables) and [`examples/CrossFileSymbolsExample`](../examples/CrossFileSymbolsExample/).

### Equality matters

```csharp
context.OptionsSource.Set(new BuildOptions("Release"));
context.OptionsSource.Set(new BuildOptions("Release"));   // equal record: no change, no notification
```

Change detection uses equality everywhere (source contents, computed values, output handlers). Use records or types implementing `IEquatable<T>`; the generator warns (GUAC0004) when an input's item type lacks value equality.

## Output Handlers

```csharp
var context = new BuildContext(new Compiler());

using var registration = context.RegisterOutput(context.Compiled, (units, ctx) =>
{
    Console.WriteLine($"Compiled {units.Count} files");
});

context.SourcesSource.Push(new SourceFile("Main.cs", "a b"));
// Output: Compiled 1 files
```

Handler behavior:
- Handlers are not run at registration; they run after **any** source of the context changes.
- A handler is called only if its provider computes a value (not an error, and not "no value" from a `Where`) and the value differs from the last value delivered to that handler.
- Handlers run synchronously on the thread that changed the source.
- If handlers throw, the remaining handlers still run and the exceptions are rethrown together as an `AggregateException` from the `Push`/`Set`/... call.
- `RegisterOutput` returns an `IDisposable`: disposing it unregisters that handler only (disposing twice is a no-op).
- After `Dispose()`, the context no longer listens to its sources and all of its handlers are dropped; reading its providers or changing its sources throws `ObjectDisposedException`.

## Troubleshooting

**Q: My output handler is never called**
- It only runs after a source changes, not at registration.
- It is skipped while its provider fails (for example a single-value input that was never `Set`, or a lambda that throws). Call `GetValue()` and inspect `ErrorValue`.
- It is skipped when the value equals the last delivered value.

**Q: Everything recomputes even though nothing relevant changed**
```csharp
// Types without value equality: every new instance looks like a change
public class MyType { public string Value { get; set; } = ""; }

// Records have value equality
public record MyType(string Value);
```
Also check that you are not rebuilding pipelines (each operator call creates a new, uncached node).

**Q: error GUAC0003 on my derived property**

Derived providers must be non-partial with a setter, assigned in a constructor (see [Derived properties on the context](#derived-properties-on-the-context)).

**Q: error CS0111 (type already defines a member with the same parameter types) on my constructor**

The generator emits the parameterless constructor and reports **GUAC0008** if you declare your own. Declare a constructor with parameters that chains to `: this()` instead; constructors that never reach `: this()` get warning **GUAC0009** because the generated sources would stay uninitialized.

## Next Steps

1. Explore the [Generated Code Reference](generated-code-reference.md) for what the generator emits and its diagnostics
2. Run the [examples](../examples/README.md), e.g. `dotnet run --project examples/BasicSourceUsage`
3. Read the [Performance Guide](performance-guide.md)
4. Upgrading from the mutation/`SaveChanges` API? See the [Migration Guide](../MIGRATION_GUIDE.md)
