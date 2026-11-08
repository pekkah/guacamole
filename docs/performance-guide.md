# Performance Guide

This guide explains how Guacamole decides what to recompute, and how to structure pipelines so it recomputes as little as possible. For the API itself see the [Quickstart](quickstart.md).

## How Recomputation Works

### Sources and versions

Every node in a context's graph has an output version. Pushing to a source (`Push`, `Remove`, `Replace`, `Clear`, `Set`) that actually changes it increments that source's version and marks the stages downstream of it as possibly changed ("dirty"); no cache is cleared and nothing is recomputed eagerly. Marking walks only the stages that have read the source, directly or through other stages, and stops at stages that an earlier change has already marked since a read last re-verified a value: several changes between two reads (to one source or to sources sharing downstream stages) walk the downstream stages once, and each further change costs a constant. Operations that do not change the source (pushing an item that is already present, setting an equal value, removing a missing item) raise no notification at all.

### Lazy validation on read

When you read a provider (`GetValue()`/`GetValues()`, `.Value`/`.Values`, or an output handler reading it), the provider:

1. Returns its cached value right away if no source has changed since that value was last verified.
2. Returns it right away, too, if none of the sources it depends on has changed since it was last verified (the stage is not marked dirty).
3. Otherwise verifies it: each input the value was computed from (in the order it was read) is first brought up to date itself, recursively, using the input's own cached value if that is still valid and recomputing the input if not. If every input still has the version recorded when the value was computed, the cached value is valid and is returned. No lambda of this stage runs.
4. Otherwise recomputes, records the versions of the inputs it read, and stores the result. Its own version is incremented **only if the new value is not equal** to the previous one (using the stage's comparer).

Step 2 keeps verification proportional to what changed: after one source change, only the stages on the paths from that source to the provider are verified input by input, and every other input costs a single check. Step 4 is the equality cut-off: when a stage's recomputed value did not change, the stages downstream of it remain valid and are not recomputed.

### Every stage is cached

Every stage of a pipeline caches its result and version information, whether or not it is read itself, and the equality cut-off applies at each of them. You do not need to read (or register outputs on) intermediate stages to get their caching:

```csharp
// Only `report` is read. A change to Files reruns Parse (for the changed files only, see
// per-item memoization below). If the parsed files are equal to the previous ones, nothing else
// reruns; otherwise Analyze reruns, and if its result is equal to the previous one, Format does not.
var parsed = context.Files.Select(Parse);
var analyzed = parsed.Collect().Select(Analyze);
var report = analyzed.Select(Format);
context.RegisterOutput(report, (r, _) => Publish(r));
```

Each stage occupies one entry in the context's value cache (see [Memory](#memory)). An evicted entry is recomputed when it is next needed, and counts as a change for the stages downstream of it.

Failed results and `null` values are the exception: they are not cached. A stage whose lambda throws (or returns `null`) runs again each time it is read, including by output handlers after every source change, and counts as changed for the stages downstream of it. Keep expensive lambdas from failing routinely; return a value that represents the failure (for example a record with a diagnostics list) when a failure is an expected outcome.

### Per-item memoization

Collection operators (`Select`, `Where`, `SelectMany` on `IncrementalValuesProvider<T>`) remember, for each input item of their previous computation, what their lambda returned. When such a stage is recomputed, the lambda runs only for items that are new or changed; an item equal to one seen last time (by `EqualityComparer<T>.Default`, i.e. the item's own equality) reuses the previous result. Pushing one item to a 10,000-item source runs each recomputed stage's lambda once, not 10,000 times.

- A reused result is the **same instance** as before, so the next stage's memo hits too, even when the result type only has reference equality.
- The memo holds one entry per distinct item of the latest computation and is updated in place when a computation completes; if a lambda throws, the stage returns an error and keeps its previous memo.
- `null` items are not memoized; the lambda runs for them whenever the stage walks its items (see below).
- Items must be immutable (records are ideal): an item mutated in place still equals itself, so its stale result would be reused.
- After a few item changes, a stage does not even walk the collection: it applies its input's item-level delta to its previous output (see [Item-level deltas](#item-level-deltas)). When no delta is available it walks the whole collection (one comparison per unchanged item, and a new result array unless every result is unchanged), saving only the lambda calls and the memo bookkeeping for unchanged items.
- Memoization is per item. A lambda that also depends on a combined value (`values.Combine(value)`) sees a new input for every item when that value changes; see [cross-item lookups](#cross-item-lookups-eg-symbol-tables) for keeping such inputs equal.

### Item-level deltas

Collection sources record which items each change inserted, removed or replaced, and the collection stages `Select`, `Where`, `SelectMany` and `values.Combine(value)` pass that on: each derives its new output from its previous output and the edits of its input (copying the unchanged items in bulk and running its lambda only for the edited ones), and records the edits of its output for the next stage. The change check of a stage (is the new output equal to the cached one, so that the stages after it stay cached?) and of `Collect()` then compares only the replaced items. After K item changes in an N-item source, the per-item work of such a pipeline is O(K).

What is still O(N) per read: each stage allocates a new output array and copies the unchanged items into it (a memory copy, about 1 ns per item), the source copies its snapshot the same way, and whatever consumes a `Collect()`ed array (a sum, building a dictionary) walks all of it. `Collect()` itself wraps its input's array without copying.

A stage falls back to walking its whole input (the behaviour described above) when no delta from the input it last read is available:

- the first computation, or the input recomputed more than once since the stage last read it (only the latest delta is kept; for example a provider read rarely while another pipeline on the same source is read after every change);
- more changes between two reads than about half the source's items, `Clear()`, and occasionally after many removals (when the source compacts its storage);
- an input that does not record deltas: custom nodes, single-value operators, `Merge`, or a stage reading an error;
- concurrent recomputations of the same stage (one of them walks).

The results are the same either way; only the cost differs.

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
- **Give the table structural equality** (an immutable sorted dictionary wrapped in a type implementing `IEquatable<T>`, or a record with a custom `Equals`). `Collect()` compares the collected declarations element by element, so the merge reruns only when they changed, and if the merged table is equal to the previous one, the table keeps its version and the type checks are not rerun. (If a file's comments changed but not its declarations, the extracted declarations are already equal, so not even the merge reruns.)
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
- Avoid building pipelines in property getters, per request or inside loops, especially with capturing lambdas: each non-matching call starts with an empty cache and adds nodes to the context's graph (each with a cached value and a version record).
- If you do build pipelines dynamically, call `context.TrimUnusedNodes()` periodically (for example after each batch of requests). It removes the nodes that are no longer referenced, with their cached values and version records:
  - A node stays alive while a provider, an output registration or a downstream node references it, and it is never removed while it is alive, so trimming never changes results.
  - The context also keeps the nodes created by operator calls alive, so that identical calls (for example in a property getter) keep sharing one node and its cache. `TrimUnusedNodes()` releases those references: unreferenced nodes can then be garbage-collected, and the next call removes them. A node requested again by an identical call in between is kept.
  - It returns the number of nodes removed. Since removal waits for the garbage collector, memory stays bounded across repeated calls rather than shrinking in a single one.
  - A removed node that an identical operator call requests later is recreated with an empty cache.

```csharp
foreach (var request in requests)
{
    var threshold = request.Threshold; // captured: a new node per request
    var matches = context.Items.Where(item => item.Score > threshold).Collect();
    Respond(request, matches.Value);
}

context.TrimUnusedNodes(); // the per-request nodes are removed once they have been collected
```

In a long-running host, you can let the context trim itself instead of calling `TrimUnusedNodes()`: override `AutoTrimInterval` (off by default, `0`) with the number of new nodes after which the context trims automatically.

```csharp
[ComputingContext]
public partial class QueryContext : ComputingContextBase
{
    protected override int AutoTrimInterval => 1_000; // trim after every 1,000 new pipeline nodes
    // ...
}
```

- Only nodes newly added to the graph count. Reading existing pipelines and identical operator calls that reuse a node cost nothing, and a context that builds its pipelines once never trims.
- Each trim scans the registry, so when many nodes remain registered, the next trim waits for at least half as many new nodes as remained. The cost of trimming stays proportional to the number of nodes added.
- A trim runs on the thread that added the node, but never inside a node computation (it waits for the next node added outside one) and never waits for another trim: if one is running, the next new node tries again. While a trim runs, other threads creating new nodes wait for it (reads do not).
- Trimming is still two-phase: nodes released by one trim are removed by a later one, after the garbage collector has collected them. As long as new nodes keep being added, the graph stays bounded (roughly the live nodes plus a few intervals' worth) without explicit calls; when no new nodes are added, no trims run, but the graph does not grow either.
- Like explicit trimming, it never affects results, but a node kept alive only by the context (for example an identical operator call in a property getter whose provider is not stored) may be collected after a trim and recreated with an empty cache. Choose an interval well above the number of nodes such calls create between reads, or store the providers.
- Like `CacheCapacity`, the override is read once by the base constructor, so return a constant. A negative value throws `ArgumentOutOfRangeException`.

## Equality

Equality is used to detect changes at every level: source contents, computed values (equality cut-off) and output handlers.

- Use records or types implementing `IEquatable<T>` for items and computed values. The generator reports GUAC0004 for input and user-managed provider properties whose item type lacks value equality.
- Pass a comparer to `Select`/`Where`/`SelectMany`/`Collect` when the default equality is too strict or does not exist. The comparer decides whether that stage's recomputed result counts as a change (for collections, element by element), both for the cache cut-off (at that stage, whether or not it is read itself) and for output handlers registered on it:

```csharp
var names = context.People.Select(p => p.Name, StringComparer.OrdinalIgnoreCase);
```

- `Collect()` compares its result element by element, in order (with `EqualityComparer<T>.Default`, or the comparer passed to `Collect(comparer)`): a re-collect with the same items in the same order keeps its version, so the stages below it stay cached and its output handlers do not fire. A different order counts as a change.
- Any stage whose result is an `ImmutableArray<T>` compares it the same way by default, element by element and in order (a single-value `Select` returning one, or collection items that are immutable arrays). Value tuples of two or three components that contain one, such as the `(ImmutableArray<T>, TOther)` produced by `Combine`, are compared component by component. Nesting works too (an immutable array of tuples, a tuple of tuples). A comparer passed to the operator always takes precedence. The same rules apply to `IIncrementalValueSource<T>.Set` (setting an equal, newly built immutable array is not a change: no notification, no recomputation) and to the per-item memo of collection `Select`/`Where`/`SelectMany` (an item that is an equal, newly built immutable array reuses its previous result). Collection sources themselves still keep their set of items with the item type's default equality, so two separately built immutable arrays with equal items are two distinct items there.
- Other collection types such as `List<T>`, arrays, `ImmutableList<T>` or dictionaries returned by a `Select` compare **by reference** (or by their own `Equals`), and so do tuples of four or more components. Such a stage counts as changed every time it is recomputed, which disables the cut-off below it and fires its output handlers. Return an `ImmutableArray<T>`, map the result to a type with structural equality, or pass a comparer where that matters:

```csharp
var lookup = context.Settings.Select(
    s => s.ToDictionary(x => x.Key, x => x.Value),
    new DictionaryComparer<string, string>()); // your IEqualityComparer<Dictionary<string, string>>
```

## Batching

`PushRange` adds all its items in one step and raises a single change notification (none if every item was already present), so output handlers run once per range and downstream stages recompute once. Use it to load many items.

`BeginBatch()` buffers `Push`/`Remove`/`Replace` operations, and `Commit()` (or disposing the batch) applies them in order in one step with a single change notification, so the source's version changes once and output handlers run once for the whole batch. Group related changes (for example all the files touched by one save) into a batch.

```csharp
using (var batch = context.FilesSource.BeginBatch())
{
    batch.Replace(oldMain, newMain);
    batch.Remove(deletedFile);
    batch.Push(addedFile);
} // one notification
```

A batch is all or nothing: if an operation would fail (`Replace` of an item that is not present at that point of the batch, even with an equal replacement, or with one that is, or a `null` item), `Commit()` throws without applying any operation and without a notification. Operations that cancel out (a `Push` of a new item and a `Remove` of it) raise no notification. A committed batch cannot be reused: `Push`/`Remove`/`Replace` after `Commit()` throw `InvalidOperationException`; begin a new batch instead.

## Memory

- Each context has an LRU value cache with a capacity of `ComputingContextBase.DefaultCacheCapacity` (10,000) entries. Every pipeline stage takes one entry (whether or not it is read itself), so size it for the number of stages, not only the providers you read. Evicted values are simply recomputed when next needed. To change it, override `CacheCapacity` on the context; the override is read once by the base constructor, so return a constant:

  ```csharp
  [ComputingContext]
  public partial class BuildContext : ComputingContextBase
  {
      protected override int CacheCapacity => 50_000;
      // ...
  }
  ```
- Outside the cache, collection `Select`, `Where` and `SelectMany` stages keep their per-item memo (the latest input and results), and `Where`, `SelectMany` and `values.Combine(value)` also keep their latest output for [item-level deltas](#item-level-deltas) (`SelectMany` also an offset per item, `Combine` its latest input). That is about one or two N-element arrays per collection stage, released when the node is trimmed.
- Nodes created by operator calls stay registered in the graph until `TrimUnusedNodes()` (called explicitly, or automatically with `AutoTrimInterval`) removes the ones nothing references anymore (see [Build Pipelines Once](#build-pipelines-once)).
- `Dispose()` unsubscribes the context from its sources and drops its output registrations; reading its providers or changing its sources afterwards throws `ObjectDisposedException` (a single volatile read on each provider read).

## Thread Safety and Performance

- Source operations take the source's own write lock only while updating its contents; notifications and output handlers run afterwards, without a lock.
- Reads do not lock the pipeline. Two threads reading the same stale provider may both compute it; the cache is updated atomically and the last result wins. Keep lambdas pure and side-effect free.
- Output handlers run synchronously, usually on the thread that changed a source. Long-running handlers delay the `Push`/`Set` call that triggered them. Each handler's calls are serialized without blocking: a change that arrives while the handler runs returns at once and is delivered by the thread already running it, after it returns.

## Benchmarking

The repository contains BenchmarkDotNet benchmarks in [`benchmarks/Guacamole.Benchmarks`](../benchmarks/Guacamole.Benchmarks/):

| Class | Measures |
|-------|----------|
| `SimpleProviderBenchmarks`, `ComplexGraphBenchmarks` | Reading a 5-stage chain / 21-stage tree |
| `CachingBenchmarks` | Cold read (cache cleared) vs warm read, and a warm read of a collection stage's `.Values` |
| `IncrementalUpdateBenchmarks` | Reading a total over N sources (a binary reduction tree) after one source changed, for N = 16, 256, 4,096, against recomputing the total directly |
| `CollectionChangeBenchmarks` | Replacing K of N items, then reading `Items.Select(work).Collect().Select(Sum)`, against recomputing all N items directly |
| `CollectionPipelineBenchmarks` | Replacing K of 10,000 items, then reading a longer pipeline (`Combine` with a value, `Select`, `Where`, `SelectMany`, `Collect`, sum), against recomputing directly |
| `MemoryOverheadBenchmarks` | Building a context over N items and reading the total, against computing the results directly (time and allocations) |
| `SourceFanOutBenchmarks` | 100 changes to a source read by N stages, then a read (time per change), against one change and a read |
| `GraphConstructionBenchmarks` | Building a chain of N nodes with `ComputationGraph.AddNode` |
| `CacheMetricsBenchmarks` | Value cache lookups and stores with cache metrics on (the default, nothing listening) and off |

```bash
cd benchmarks/Guacamole.Benchmarks
dotnet run -c Release -- --filter "*"                                # all benchmarks, default job
dotnet run -c Release -- --filter "*IncrementalUpdate*" --job short  # one class, quick run
```

Reports go to `BenchmarkDotNet.Artifacts/results/` in the current directory (HTML, CSV, GitHub Markdown; pass `--artifacts <path>` to choose another folder). `BenchmarkDotNet.Artifacts/` is not tracked by git, so a local run leaves the working tree clean; the curated results below (and the older [October 2025 baseline](benchmarks/2025-10-baseline.md)) are the committed reference. Add `--inProcess` where BenchmarkDotNet cannot start a separate benchmark process. `tests/Guacamole.Benchmarks.Tests` runs every benchmark once (dry job, in-process) and checks with call counters that the incremental benchmarks recompute what they claim to.

### Benchmark results

One short run (`--job short --inProcess`, 3 iterations) per benchmark, before and after back to back, on a 4-core Intel Xeon 2.1 GHz VM, Linux, .NET 11 RC3. The error margins are wide, so read these as orders of magnitude, not exact figures. The per-item/per-leaf work (`Work.Mix`) takes about 0.11 µs.

**One change in a graph of N sources** (`IncrementalUpdateBenchmarks`):

| Sources | Stages | Cached read | One source changed | Direct recompute |
|--------:|-------:|------------:|-------------------:|-----------------:|
| 16 | 62 | 53 ns | 6.0 µs | 1.8 µs |
| 256 | 1,022 | 52 ns | 13 µs | 27 µs |
| 4,096 | 16,382 | 53 ns | 21 µs | 0.44 ms |

**K changed items out of N** (`CollectionChangeBenchmarks`, `Items.Select(work).Collect().Select(Sum)`, incremental vs direct; before the item-level deltas of #61 in parentheses):

| Items | K = 1 | K = 10 | K = 100 |
|------:|------:|-------:|--------:|
| 1,000 | 8.0 µs (15 µs) vs 116 µs | 14 µs (19 µs) vs 113 µs | 62 µs (64 µs) vs 111 µs |
| 10,000 | 34 µs (108 µs) vs 1.10 ms | 42 µs (116 µs) vs 1.12 ms | 95 µs (161 µs) vs 1.12 ms |

The incremental variant allocated 17 KB per change and read at 1,000 items and 161 KB at 10,000 items (K = 1; 174 KB at K = 100), with no gen2 collections, about the same as before: the source snapshot and the `Select` result are still new N-element arrays. The direct one allocates nothing.

**K changed items through a longer pipeline** (`CollectionPipelineBenchmarks`, 10,000 items, `Items.Combine(factor).Select(work).Where(even).SelectMany(fan-out 0-2).Collect().Select(Sum)`):

| K | Incremental | Before #61's deltas | Direct recompute |
|--:|------------:|--------------------:|-----------------:|
| 1 | 153 µs | 414 µs | 1.34 ms |
| 100 | 309 µs | 507 µs | 1.42 ms |

It allocates about 420 KB per change and read (each stage's output array; the `Combine` tuples go to the large object heap), the same as before.

**Many changes between two reads** (`SourceFanOutBenchmarks`, one source read by N stages): 100 changes and then a read cost 0.18 µs / 0.44 µs / 3.8 µs per change for N = 100 / 1,000 / 10,000, one walk over the stages for the first change plus a constant for each further one; a single change and a read cost 3.9 µs / 29 µs / 0.36 ms. Before #71 every change walked all N stages: 3.0 µs / 28 µs / 0.38 ms per change.

**Graph construction and metrics** (`GraphConstructionBenchmarks`, `CacheMetricsBenchmarks`): building a chain of 1,000 / 4,000 nodes takes 2.2 ms / 11 ms (111 ms / 2.6 s before #76, when the cycle check walked the whole chain for each node). A value cache lookup takes about 48 ns and a store about 52 ns whether cache metrics are on or off: metrics are only recorded while a listener (for example OpenTelemetry) has enabled them.

**Memory overhead** (`MemoryOverheadBenchmarks`): building a context over 1,000 / 10,000 items and reading the total took 0.20 ms / 2.2 ms and allocated 126 KB / 1.2 MB, against 0.11 ms / 1.1 ms and 8 KB / 78 KB for computing the results directly (about 16x the allocations).

The other benchmarks (`IncrementalUpdateBenchmarks`, `MemoryOverheadBenchmarks`) were unchanged by #61's deltas, within the error margins. For comparison, the same run of the engine before the changes in #57, #60, #61 (its first part) and #62: one source changed 11 µs / 96 µs / 2.5 ms (16 / 256 / 4,096 sources); K changed items 75 / 93 / 275 µs at 1,000 items and 1.04 / 1.02 / 1.37 ms at 10,000 (607-870 KB per read at 10,000, with gen2 collections); building 1.2 ms / 16 ms and 1.5 MB / 18.6 MB.

What this shows:

- **Cached reads are constant-time.** A read with no change since the last one costs about 50 ns (80 ns before #76 removed a lock taken on every cache access), whatever the graph size.
- **Recomputation and verification are proportional to the changes.** After one source changes, only the changed leaf's lambda and the stages on its path to the root rerun, and only the stages on that path are verified input by input; every other stage costs one check. The time grows with the depth of the tree (log N here), not its size. After K item replacements, the per-item lambda runs K times. The tests in `tests/Guacamole.Benchmarks.Tests/IncrementalScenarioTests.cs` assert these call counts.
- **Collection stages do O(K) work per K item changes, plus O(N) memory copies.** `Select`, `Where`, `SelectMany` and `values.Combine(value)` apply their input's item-level delta (see [Item-level deltas](#item-level-deltas)), and the change checks of these stages and of `Collect()` compare only the replaced items. What remains O(N) is copying: the source snapshot and each stage's output array are new arrays (about 3 ns per item and stage here, allocation included), and the sum after `Collect()` walks all N results. At 10,000 items and K = 1 that is about 34 µs, against 108 µs when every stage walked and compared all N items.
- **Each change has a fixed cost** of a few µs: the source update, marking the stages downstream of the source, and recomputing the changed path (about 0.5 µs per recomputed stage here). With work as cheap as 0.1 µs per stage, small graphs (16 sources) are still faster to recompute directly. Incremental evaluation pays off when the graph is large or the work per stage or item is clearly more expensive than that (parsing, type checking, anything allocating or doing I/O).
- **Setup costs about twice a direct computation.** `PushRange` adds all items in one step; the remaining allocations are the source's storage and snapshot, one memo entry per item and the result arrays.

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
- [ ] Stages whose results are often equal after a change have value equality or a comparer, so the cut-off stops recomputation there
- [ ] Collection items are immutable, and lambdas are pure (per-item results are reused)
- [ ] Data shared across items (symbol tables, indexes) is computed with `Collect()` and a deterministic merge into a type with structural equality, not accumulated in a lambda
- [ ] Output handlers are cheap and thread-safe

## References

- Cache validation: [`src/Guacamole/Pipeline/NodeEvaluation.cs`](../src/Guacamole/Pipeline/NodeEvaluation.cs)
- Computation graph: [`src/Guacamole/Pipeline/ComputationGraph.cs`](../src/Guacamole/Pipeline/ComputationGraph.cs)
- LRU cache: [`src/Guacamole/Caching/LRUCache.cs`](../src/Guacamole/Caching/LRUCache.cs)
- Benchmarks: [`benchmarks/Guacamole.Benchmarks/`](../benchmarks/Guacamole.Benchmarks/)
