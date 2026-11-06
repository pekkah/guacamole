# Performance Guide

This guide explains how Guacamole decides what to recompute, and how to structure pipelines so it recomputes as little as possible. For the API itself see the [Quickstart](quickstart.md).

## How Recomputation Works

### Sources and versions

Every node in a context's graph has an output version. Pushing to a source (`Push`, `Remove`, `Replace`, `Clear`, `Set`) that actually changes it increments that source's version and nothing else: no cache is cleared and no dependent node is touched eagerly. Operations that do not change the source (pushing an item that is already present, setting an equal value, removing a missing item) raise no notification at all.

### Lazy validation on read

When you read a provider (`GetValue()`/`GetValues()`, `.Value`/`.Values`, or an output handler reading it), the provider:

1. Checks whether its cached value is still valid: every input version it recorded when it was computed must be unchanged (and, for inputs whose own cached values it used, those must still be valid).
2. If valid, returns the cached value. No lambda runs.
3. Otherwise records the current input versions, recomputes, and stores the result. Its own version is incremented **only if the new value is not equal** to the previous one (using the provider's comparer).

Step 3 is the equality cut-off: when a read provider's value did not change, downstream providers built from its cached value remain valid and are not recomputed.

### Only read nodes are cached

Values are cached for the providers that are read. Intermediate stages that are never read are recomputed inline whenever a provider that depends on them is recomputed. Validation looks through such intermediate stages to the inputs behind them, so correctness does not depend on what you read, but caching does.

```csharp
// Only `report` is read: a change to Files reruns Parse for every file, then Analyze, then Format.
var parsed = context.Files.Select(Parse);
var analyzed = parsed.Collect().Select(Analyze);
var report = analyzed.Select(Format);
context.RegisterOutput(report, (r, _) => Publish(r));

// Also reading `analyzed` caches it: if Analyze returns an equal value, Format does not rerun.
context.RegisterOutput(analyzed, (_, _) => { });
```

Output handlers run in registration order, so register handlers for upstream stages before downstream ones if you rely on their cached values.

### No per-item caching

Collection operators (`Select`, `Where`, `SelectMany` on `IncrementalValuesProvider<T>`) process the whole collection whenever they are recomputed. Changing one item of a 10,000-item source reruns the lambda for all 10,000 items in every recomputed stage. If a per-item computation is expensive:

- memoize it yourself (for example a dictionary keyed by the item, which works well with records), or
- split independent data into separate sources so unrelated changes do not reach it.

## Build Pipelines Once

Every distinct operator call creates a new node with a unique ID, and the cache is keyed by node ID. Identical operator calls are reused: calling the same operator on the same input with an equal delegate (a non-capturing lambda, a lambda that only captures `this`, or a method group on the same instance) returns the existing node. Lambdas that capture locals allocate a new closure per call, so they never match and create a new node each time.

- Build pipelines once, in a constructor (chaining to the generated `this()`) or at startup, and keep the providers.
- Avoid building pipelines in property getters, per request or inside loops, especially with capturing lambdas: each non-matching call starts with an empty cache and adds nodes to the context's graph, which are never removed.

## Equality

Equality is used to detect changes at every level: source contents, computed values (equality cut-off) and output handlers.

- Use records or types implementing `IEquatable<T>` for items and computed values. The generator reports GUAC0004 for input and user-managed provider properties whose item type lacks value equality.
- Pass a comparer to `Select`/`Where`/`SelectMany` when the default equality is too strict or does not exist:

```csharp
var names = context.People.Select(p => p.Name, StringComparer.OrdinalIgnoreCase);
```

- `ImmutableArray<T>` (the result of `Collect()`) and other collection types such as `List<T>`, arrays or dictionaries compare **by reference**. A collected provider, or a `Select` that returns a new list or dictionary, therefore counts as changed every time it is recomputed, which disables the cut-off below it and fires its output handlers. Map collections to a type with structural equality, or pass a comparer, where that matters.

## Batching

`BeginBatch()` buffers operations, but `Commit()` still applies them one at a time, and each effective change raises its own notification, which runs all output handlers. `PushRange` behaves the same way. When loading many items with output handlers registered, consider registering the handlers after the initial load, or keeping handlers cheap.

## Memory

- Each context graph has an LRU value cache with a capacity of 10,000 entries. Evicted values are simply recomputed on the next read.
- Nodes created by operator calls stay registered in the graph for the lifetime of the context (another reason to build pipelines once).
- `Dispose()` unsubscribes the context from its sources and drops its output registrations.

## Thread Safety and Performance

- Source operations take the source's own write lock only while updating its contents; notifications and output handlers run afterwards, without a lock.
- Reads do not lock the pipeline. Two threads reading the same stale provider may both compute it; the cache is updated atomically and the last result wins. Keep lambdas pure and side-effect free.
- Output handlers run synchronously on the thread that changed a source. Long-running handlers delay the `Push`/`Set` call that triggered them.

## Benchmarking

The repository contains BenchmarkDotNet benchmarks in [`benchmarks/Guacamole.Benchmarks`](../benchmarks/Guacamole.Benchmarks/) (`SimpleProviderBenchmarks`, `CachingBenchmarks`, `ComplexGraphBenchmarks`):

```bash
cd benchmarks/Guacamole.Benchmarks
dotnet run -c Release
```

To benchmark your own context, measure the three things that matter: a cached read (should run no lambdas), a read after a small change (should run only the affected stages), and the cost of a source operation with your output handlers registered.

```csharp
using BenchmarkDotNet.Attributes;
using Guacamole; // also brings PushRange into scope

[MemoryDiagnoser]
public class MyContextBenchmarks
{
    private BuildContext _context = null!;
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        _context = new BuildContext(new Compiler());
        _context.SourcesSource.PushRange(
            Enumerable.Range(0, 1000).Select(i => new SourceFile($"File{i}.cs", "a b c")));
        _ = _context.Result.Value; // warm the cache
    }

    [Benchmark]
    public BuildResult CachedRead() => _context.Result.Value;

    [Benchmark]
    public BuildResult ReadAfterChange()
    {
        _context.SourcesSource.Push(new SourceFile($"New{_counter++}.cs", "x"));
        return _context.Result.Value;
    }
}
```

(`BuildContext`, `Compiler`, `SourceFile` and `BuildResult` are the types from the [Quickstart](quickstart.md#derived-properties-on-the-context).)

## Checklist

- [ ] Pipelines are built once and the providers are kept
- [ ] Item and computed value types have value equality (records / `IEquatable<T>`) or explicit comparers
- [ ] Expensive intermediate stages are read (so they are cached) or memoized
- [ ] Output handlers are cheap and thread-safe

## References

- Cache validation: [`src/Guacamole/Pipeline/ProviderCacheCoordinator.cs`](../src/Guacamole/Pipeline/ProviderCacheCoordinator.cs)
- Computation graph: [`src/Guacamole/Pipeline/ComputationGraph.cs`](../src/Guacamole/Pipeline/ComputationGraph.cs)
- LRU cache: [`src/Guacamole/Caching/LRUCache.cs`](../src/Guacamole/Caching/LRUCache.cs)
- Benchmarks: [`benchmarks/Guacamole.Benchmarks/`](../benchmarks/Guacamole.Benchmarks/)
