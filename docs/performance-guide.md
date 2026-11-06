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
// Only `report` is read: a change to Files reruns Parse (for the changed files only, see
// per-item memoization below), then Analyze, then Format.
var parsed = context.Files.Select(Parse);
var analyzed = parsed.Collect().Select(Analyze);
var report = analyzed.Select(Format);
context.RegisterOutput(report, (r, _) => Publish(r));

// Also reading `analyzed` caches it: if Analyze returns an equal value, Format does not rerun.
context.RegisterOutput(analyzed, (_, _) => { });
```

Output handlers run in registration order, so register handlers for upstream stages before downstream ones if you rely on their cached values.

### Per-item memoization

Collection operators (`Select`, `Where`, `SelectMany` on `IncrementalValuesProvider<T>`) remember, for each input item of their previous computation, what their lambda returned. When such a stage is recomputed, the lambda runs only for items that are new or changed; an item equal to one seen last time (by `EqualityComparer<T>.Default`, i.e. the item's own equality) reuses the previous result. Pushing one item to a 10,000-item source runs each recomputed stage's lambda once, not 10,000 times.

- A reused result is the **same instance** as before, so the next stage's memo hits too, even when the result type only has reference equality.
- The memo holds one entry per distinct item of the latest computation and is replaced when a computation completes; if a lambda throws, the stage returns an error and keeps its previous memo.
- `null` items are not memoized; the lambda runs for them every time.
- Items must be immutable (records are ideal): an item mutated in place still equals itself, so its stale result would be reused.
- The stage still walks the whole collection (and allocates the result array) on recomputation; only the lambda calls are saved. To avoid even that for unrelated data, split it into separate sources.
- Memoization is per item. A lambda that also depends on a combined value (`values.Combine(value)`) sees a new input for every item when that value changes; see [cross-item lookups](#cross-item-lookups-eg-symbol-tables) for keeping such inputs equal.

## Cross-Item Lookups (e.g. Symbol Tables)

Some per-item computations need data from *other* items: a compiler type-checking one file needs the declarations of every file. Do **not** accumulate that data as a side effect of a per-item lambda (for example a mutable symbol builder that each file adds its declarations to while it is checked). Such a lambda only sees the items processed before it, so its result depends on processing order; it also breaks per-item memoization and lazy evaluation, since lambdas may be skipped, rerun or run concurrently.

Instead, make the per-item computation a pure function of (item, table), and compute the table from all items:

```csharp
// 1. Per item: extract what other items need (pure, memoized per file)
var declarations = Parsed.Select(FileDeclarations.Extract);

// 2. All items: merge into one table, in a deterministic order, with structural equality
IncrementalValueProvider<SymbolTable> symbols = declarations.Collect().Select(SymbolTable.Merge);

// 3. Per item again: pair each file with the table
var typeChecked = Parsed.Combine(symbols).Select(pair => TypeCheck(pair.Left, pair.Right));
```

- **Merge in a defined order.** Sources enumerate in insertion order, so `Collect()` is deterministic for a given sequence of pushes, but results should not depend on push order either. Sort inside the merge (for example by path, `StringComparer.Ordinal`) and resolve conflicts explicitly (for example "first declaration wins").
- **Give the table structural equality** (an immutable sorted dictionary wrapped in a type implementing `IEquatable<T>`, or a record with a custom `Equals`). `Collect()` returns a new `ImmutableArray<T>` (reference equality) on every change, but if the merged table is equal to the previous one (say a file's comments changed but not its declarations), the table keeps its version. Read the table (for example register an output on it before the outputs that depend on it) so its version is tracked.
- **Narrow the table per item** when one item changing the table should not rerun every other item. `Combine` pairs every item with the new table, so every item's input changes when the table does. Restricting the table to what the item references keeps the input equal for unaffected items, and the next stage's per-item memo skips them:

  ```csharp
  var typeChecked = Parsed.Combine(symbols)
      .Select(pair => (File: pair.Left, Symbols: pair.Right.Restrict(pair.Left.ReferencedNames))) // cheap, reruns per file
      .Select(pair => TypeCheck(pair.File, pair.Symbols));                                    // expensive, memoized
  ```

With this structure, editing one file reruns its own parse and declaration extraction, one merge, and the type checks of the files whose (narrowed) inputs changed. See [`examples/CrossFileSymbolsExample`](../examples/CrossFileSymbolsExample/) for a runnable version and `tests/Guacamole.Tests/Regressions/Issue25CrossFileSymbolLookupTests.cs` for the order-dependence it avoids.

## Build Pipelines Once

Every distinct operator call creates a new node with a unique ID, and the cache is keyed by node ID. Identical operator calls are reused: calling the same operator on the same input with an equal delegate (a non-capturing lambda, a lambda that only captures `this`, or a method group on the same instance) returns the existing node. Lambdas that capture locals allocate a new closure per call, so they never match and create a new node each time.

- Build pipelines once, in a constructor (chaining to the generated `this()`) or at startup, and keep the providers.
- Avoid building pipelines in property getters, per request or inside loops, especially with capturing lambdas: each non-matching call starts with an empty cache and adds nodes to the context's graph, which are never removed.

## Equality

Equality is used to detect changes at every level: source contents, computed values (equality cut-off) and output handlers.

- Use records or types implementing `IEquatable<T>` for items and computed values. The generator reports GUAC0004 for input and user-managed provider properties whose item type lacks value equality.
- Pass a comparer to `Select`/`Where`/`SelectMany` when the default equality is too strict or does not exist. The comparer decides whether that stage's recomputed result counts as a change (for collections, element by element), both for the cache cut-off and for output handlers registered on it. The cut-off applies where the stage is read (`Value`/`GetValue`/`GetValues` or an output registration); a stage that is never read itself is looked through:

```csharp
var names = context.People.Select(p => p.Name, StringComparer.OrdinalIgnoreCase);
```

- `ImmutableArray<T>` (the result of `Collect()`) and other collection types such as `List<T>`, arrays or dictionaries compare **by reference**. A collected provider, or a `Select` that returns a new list or dictionary, therefore counts as changed every time it is recomputed, which disables the cut-off below it and fires its output handlers. Map collections to a type with structural equality, or pass a comparer, where that matters.

## Batching

`BeginBatch()` buffers operations, but `Commit()` still applies them one at a time, and each effective change raises its own notification, which runs all output handlers. `PushRange` behaves the same way. When loading many items with output handlers registered, consider registering the handlers after the initial load, or keeping handlers cheap.

## Memory

- Each context has an LRU value cache with a capacity of `ComputingContextBase.DefaultCacheCapacity` (10,000) entries. Evicted values are simply recomputed on the next read. To change it, override `CacheCapacity` on the context; the override is read once by the base constructor, so return a constant:

  ```csharp
  [ComputingContext]
  public partial class BuildContext : ComputingContextBase
  {
      protected override int CacheCapacity => 50_000;
      // ...
  }
  ```
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
- [ ] Collection items are immutable, and lambdas are pure (per-item results are reused)
- [ ] Data shared across items (symbol tables, indexes) is computed with `Collect()` and a deterministic merge into a type with structural equality, not accumulated in a lambda
- [ ] Output handlers are cheap and thread-safe

## References

- Cache validation: [`src/Guacamole/Pipeline/ProviderCacheCoordinator.cs`](../src/Guacamole/Pipeline/ProviderCacheCoordinator.cs)
- Computation graph: [`src/Guacamole/Pipeline/ComputationGraph.cs`](../src/Guacamole/Pipeline/ComputationGraph.cs)
- LRU cache: [`src/Guacamole/Caching/LRUCache.cs`](../src/Guacamole/Caching/LRUCache.cs)
- Benchmarks: [`benchmarks/Guacamole.Benchmarks/`](../benchmarks/Guacamole.Benchmarks/)
