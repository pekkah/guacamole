# Migration Guide

This guide covers the breaking API changes made before 1.0:

- [API review cleanup (#83)](#api-review-cleanup-83)
- [Namespace consolidation](#namespace-consolidation)
- [Internal types and members](#internal-types-and-members)
- [Single-value `Where` produces "no value"](#single-value-where-produces-no-value)
- [Removed settings and dead code](#removed-settings-and-dead-code)
- [Collection sources keep insertion order](#collection-sources-keep-insertion-order)
- [From mutation-based to push-based sources](#from-mutation-based-to-push-based-sources)

## API Review Cleanup (#83)

An architecture review of the public API before 1.0 removed dead members and made the remaining contracts consistent. Most changes are renames that the compiler points out.

| Before | After |
|--------|-------|
| `[ComputingContext(GenerateAsyncMethods = ..., GenerateBatchMethods = ..., Name = ...)]` | `[ComputingContext]` |
| `GetValues()` → `Result<IEnumerable<T>, ComputationError>`, `.Values` → `IEnumerable<T>` | `Result<IReadOnlyList<T>, ComputationError>`, `IReadOnlyList<T>` |
| collection `RegisterOutput(provider, Action<IEnumerable<T>, ComputingContextBase>)` | `Action<IReadOnlyList<T>, ComputingContextBase>` |
| `void RegisterOutput(...)` | `IDisposable RegisterOutput(...)` |
| `result.Value` (on `Result<TValue, TError>`) | `result.ValueOrDefault` |
| `AddGuacamoleCore`, `GuacamoleCoreOptions` | `AddGuacamole`, `GuacamoleOptions` |
| `ProviderOptions`, `ProviderOptions.DefaultTags`, `IProviderMetadataFactory` | removed |
| `SourceChangedEventArgs.Timestamp` (`DateTime`) | `DateTimeOffset` |
| `ProviderMetadata(string trackingName, [CallerFilePath] ...)` constructor | `ProviderMetadata.Named(trackingName)` |

New: `Merge(other, comparer)`.

### `ComputingContextAttribute` has no properties

`GenerateAsyncMethods`, `GenerateBatchMethods` and `Name` were left over from the mutation-based design: the source generator never read them. Remove the arguments: `[ComputingContext(GenerateAsyncMethods = false)]` becomes `[ComputingContext]`. Generated code is unchanged.

### Collection values are `IReadOnlyList<T>`

`IncrementalValuesProvider<T>.GetValues()` returns `Result<IReadOnlyList<T>, ComputationError>` (was `Result<IEnumerable<T>, ComputationError>`), `.Values` returns `IReadOnlyList<T>` (was `IEnumerable<T>`), and the collection `RegisterOutput` handler is an `Action<IReadOnlyList<T>, ComputingContextBase>` (was `Action<IEnumerable<T>, ComputingContextBase>`). The values were already a read-only list view of the cached array (#69); now the type says so, so you can use `Count` and the indexer without LINQ. Cached reads still return the same view without allocating.

Code that assigns the values to `IEnumerable<T>` keeps compiling. Explicitly typed lambdas (`(IEnumerable<T> items, ComputingContextBase ctx) => ...`) and variables of type `Result<IEnumerable<T>, ComputationError>` need the new type.

### `RegisterOutput` returns `IDisposable`

Both `RegisterOutput` overloads return an `IDisposable` registration (they returned `void`). Disposing it unregisters just that handler; disposing it twice, or after the context was disposed, does nothing. Existing calls that ignore the return value keep compiling. Code that passed a method group of `RegisterOutput` to an `Action<...>` delegate needs a `Func<..., IDisposable>` or a lambda.

### `Result.Value` is now `Result.ValueOrDefault`

`Result<TValue, TError>.Value` returned `default` for None and errors, while the providers' `.Value` throws: the same name had opposite contracts. It is renamed to `ValueOrDefault`, with unchanged behavior; the providers' throwing `.Value`/`.Values` are unchanged.

```csharp
// Before
var result = provider.GetValue();
if (result.IsSuccess) Use(result.Value);

// After
var result = provider.GetValue();
if (result.IsSuccess) Use(result.ValueOrDefault);
```

`IsSuccess`, `IsNone`, `IsError`, `ErrorValue`, `Match`, `GetValueOrDefault(...)`, `Map`, `Bind` and `MapError` are unchanged. A search for `.GetValue().Value`, `.GetValues().Value` and `result.Value` finds most call sites; the compiler reports the rest.

### Dependency-injection surface

| Before | After |
|--------|-------|
| `services.AddGuacamoleCore()` / `AddGuacamoleCore(Action<GuacamoleCoreOptions>)` | `services.AddGuacamole()` / `AddGuacamole(Action<GuacamoleOptions>)` |
| `GuacamoleCoreOptions` | `GuacamoleOptions` (no settings yet) |
| `GuacamoleCoreOptions.ProviderOptions`, `ProviderOptions`, `ProviderOptions.DefaultTags` | removed |
| `IProviderMetadataFactory` (and its DI registration) | removed |

Nothing in the framework read `DefaultTags` or resolved `IProviderMetadataFactory`: providers get their metadata from operators such as `WithTrackingName`, never from the container. If you used the factory to create metadata, create it directly, for example `ProviderMetadata.Named("parse").WithTags("app", "x")`. Contexts are still registered like any other service (`services.AddScoped<MyContext>()`).

### `ProviderMetadata.Named` replaces the tracking-name constructor

`ProviderMetadata` had a constructor `(string trackingName, [CallerFilePath] string callerFilePath = "", ...)` next to the record's positional constructor, so `new ProviderMetadata("parse")` did not compile (CS0121, ambiguous call), and a second string argument silently became the caller file path (#90). The caller-info constructor is now a factory:

| Want | Write |
|------|-------|
| Tracking name and the caller's location as `CreationLocation` | `ProviderMetadata.Named("parse")` |
| Tracking name only | `new ProviderMetadata("parse")` or `new ProviderMetadata(TrackingName: "parse")` |

Source providers no longer carry a bogus `CreationLocation` built from a description string: their metadata is `TrackingName = "Source: {sourceId}"` with tags `source` and `value-source` or `collection-source`.

### `SourceChangedEventArgs.Timestamp` is a `DateTimeOffset`

`SourceChangedEventArgs.Timestamp` and the constructor's `timestamp` parameter are `DateTimeOffset` (were `DateTime`). Sources still record UTC (`Offset` is zero). Use `Timestamp.UtcDateTime` where a `DateTime` is needed.

## Namespace Consolidation

All public types used to be spread over `Guacamole`, `Guacamole.Sources`, `Guacamole.Exceptions` and several `Guacamole.Core.*` namespaces. They now live in `Guacamole`, plus `Guacamole.Diagnostics` for metrics, and the `Core` segment is gone. Infrastructure types moved to `Guacamole.Pipeline` and `Guacamole.Caching` and are now internal (see [Internal types and members](#internal-types-and-members)). A typical application needs only:

```csharp
using Guacamole;
```

| Old namespace | Types | New namespace |
|---------------|-------|---------------|
| `Guacamole.Sources` | `IIncrementalSource<T>`, `IIncrementalValueSource<T>`, `SourceChangedEventArgs`, `IBatchOperation<T>`, `IncrementalSourceExtensions` (`PushRange`, `BeginBatch`) | `Guacamole` |
| `Guacamole.Sources` | `IncrementalSource<T>`, `IncrementalValueSource<T>`, `BatchOperation<T>` | internal |
| `Guacamole.Core.Common` | `Result<TValue, TError>` | `Guacamole` |
| `Guacamole.Core.Pipeline` | `ComputationError` | `Guacamole` |
| `Guacamole.Core.Diagnostics` | `ProviderMetadata`, `SourceLocation` | `Guacamole` |
| `Guacamole.Exceptions` | `ContextOperationException` | removed (unused) |
| `Guacamole.Core.Pipeline` | `ComputationGraph`, `IComputationNode`, `IComputationNode<T>`, `ICollectionComputationNode<T>`, `IHasComparer<T>`, `ComputationContext`, `VersionTracker`, `NodeVersionInfo` | internal |
| `Guacamole.Core.Nodes` | `ComputationNodeBase<T>`, `CollectionComputationNodeBase<T>` | internal |
| `Guacamole.Core.Caching` | `ICache<TKey, TValue>`, `LRUCache<TKey, TValue>`, `ICacheStatisticsProvider`, `CacheStatistics` | internal |
| `Guacamole.Core.Diagnostics` | `GuacamoleMetrics`, `MetricsConfiguration` | `Guacamole.Diagnostics` |

Unchanged in `Guacamole`: `ComputingContextBase`, `ComputingContextAttribute`, `IncrementalValueProvider<T>`, `IncrementalValuesProvider<T>`, the operator extension classes, `DiagnosticProviderExtensions` and the dependency-injection extensions and options.

To migrate, replace `using Guacamole.Sources;`, `using Guacamole.Core.Common;`, `using Guacamole.Core.Pipeline;`, `using Guacamole.Core.Nodes;`, `using Guacamole.Core.Caching;` and `using Guacamole.Exceptions;` with `using Guacamole;`, and `using Guacamole.Core.Diagnostics;` with `using Guacamole.Diagnostics;` if you configure metrics. Code generated by the source generator is updated automatically when you rebuild.

## Internal Types and Members

The computation graph, its nodes and the value cache are implementation details and are no longer public:

- `ComputationGraph` (including `ReplaceNode`, `WithCachedValue`, `ValueCache`, `MarkSourceChanged`, `InvalidateCacheForNode` and the runtime-node queries), `IComputationNode`, `IComputationNode<T>`, `ICollectionComputationNode<T>`, `IHasComparer<T>`, `ComputationContext`, `ComputationNodeBase<T>`, `CollectionComputationNodeBase<T>`, `VersionTracker`, `NodeVersionInfo`
- `ICache<TKey, TValue>`, `LRUCache<TKey, TValue>`, `ICacheStatisticsProvider`, `CacheStatistics`
- `IncrementalValueProvider<T>.Node`/`.Graph`, `IncrementalValuesProvider<T>.Node`/`.Graph` and the providers' constructors (providers are created by sources and operators only; the positional `Deconstruct` is gone)
- `IncrementalSource<T>`, `IncrementalValueSource<T>` and `BatchOperation<T>`: use the `IIncrementalSource<T>`, `IIncrementalValueSource<T>` and `IBatchOperation<T>` interfaces
- `ComputingContextBase.Graph` and `SubscribeToSource`: a context creates its sources with the protected `CreateSource<T>(id)` / `CreateValueSource<T>(id)` helpers, which register and subscribe them. The generated constructor uses them; rebuild to regenerate it.
- `IComputationContextFactory`, `IIncrementalValueProviderFactory` and `IIncrementalValuesProviderFactory` (they only accepted node types)
- `GuacamoleMetrics.StartActivity`, `RecordComputation`, `RecordCacheOperation`, `UpdateGraphMetrics` and `UpdateCacheMetrics`. The instruments themselves stay public; observe them with a `MeterListener` or OpenTelemetry.

Custom computation nodes are not supported: build pipelines from sources with the provider operators (`Select`, `Where`, `Combine`, `SelectMany`, `Collect`, ...).

## Single-Value `Where` Produces "No Value"

`IncrementalValueProvider<T>.Where` used to report a rejected value as an error (`"Value filtered out by predicate"`), so `.Value` threw and downstream operators saw an error. A rejected value is now a distinct, non-error **no value** state:

| | Before | Now |
|---|--------|-----|
| `GetValue()` | `IsSuccess == false`, `ErrorValue.Message == "Value filtered out by predicate"` | `IsNone == true`, `IsSuccess == false`, `IsError == false`, `ErrorValue == null` |
| `.Value` | throws `InvalidOperationException` ("Failed to compute value: ...") | throws `InvalidOperationException` ("The provider has no value ...") |
| `TryGetValue(out v)` | - | returns `false` (throws only if the computation failed) |
| downstream `Select`/`Where`/`Combine` | error result | no value; lambdas are not run (errors still win in `Combine`) |
| downstream `SelectMany`, `values.Combine(value)` | error result | empty collection |
| output handlers | not called | not called; called again when a value reappears |

`Result<TValue, TError>` gained the third state: `Result.None`, `IsNone` and `IsError`, plus `Match` overloads with an `onNone` handler. `Map`/`Bind`/`MapError` propagate None. The two-handler `Match` overloads and `GetValueOrDefault(Func<TError, TValue>)` throw `InvalidOperationException` for None, and `default(Result<TValue, TError>)` is now None (it used to be an error with a `null` error).

To migrate, replace `!result.IsSuccess` checks that mean "failed" with `result.IsError`, and handle `result.IsNone` where a `Where` can reject the value. Replace `try { provider.Value } catch` patterns with `provider.TryGetValue(out var value)`.

## Removed Settings and Dead Code

Several options were accepted but never had any effect. They were removed rather than left as dead knobs; delete the assignments.

| Removed | Notes |
|---------|-------|
| `GuacamoleCoreOptions.CacheOptions` (`CacheOptions.DefaultCapacity`, `EnableStatistics`, `CleanupIntervalMs`) | Contexts never used the DI-registered cache; each context's graph had a hardcoded 10,000-entry cache. Override `ComputingContextBase.CacheCapacity` instead (see below). |
| `GuacamoleCoreOptions.PipelineOptions` (`MaxDependencyDepth`, `EnablePerformanceMetrics`, `ComputationTimeoutMs`) | Never read. |
| `ProviderOptions.EnableProfiling`, `ProviderOptions.EnableMetadataTracking` | Never read (the latter only by the removed node-based factories). `ProviderOptions.DefaultTags` and `IProviderMetadataFactory` were removed later as well (see [Dependency-injection surface](#dependency-injection-surface)). |
| DI registrations of `ICache<string, object>`, `ICacheStatisticsProvider`, `ComputationGraph` and the node-based factories | Unused; `AddGuacamole` (formerly `AddGuacamoleCore`) now registers only `GuacamoleOptions`. |
| `MetricsConfiguration.EnableDetailedTracing`, `TracingSampleRate`, `MaxTrackingNamesPerProcess`, `EnableProviderMetrics`, `EnableMemoryMetrics`, `MinimumDurationThresholdMs`, `RecordIndividualNodePerformance`, `TrackInvalidationCascades`, `IsValid()` | Never read. Tracing needs no switch: activities are only created while a listener is attached. `With(...)` now takes `enableGraphMetrics` instead of `enableDetailedTracing`. |
| `GuacamoleMetrics.CacheInvalidationsTotal`, `ProviderTransformationsTotal`, `ProvidersCreatedTotal`, `MemoryUsageEstimated` | Never recorded. |
| `ComputingContextBase.Lock` | Never used by the framework. Use your own lock if a derived context needs one. |
| `ContextOperationException` | Not thrown anywhere. |

What now works:

- **Cache capacity**: `protected virtual int CacheCapacity` on `ComputingContextBase` (default `ComputingContextBase.DefaultCacheCapacity`, 10,000) sets the size of the context's value cache. It is read once by the base constructor, so the override must return a constant.
- **Computation metrics**: providers record `guacamole.computations.total`, `guacamole.computation.duration` and `guacamole.computation.errors.total` each time they compute a value (not for cache hits), when `MetricsConfiguration.EnableComputationMetrics` is on. `CollectTrackingNameDimensions` and `CollectPipelineStageDimensions` control the `tracking.name` and `pipeline.stage` dimensions.

## Collection Sources Keep Insertion Order

`IIncrementalSource<T>` used to store its items in a hash set and enumerate them in hash order. String hash codes are randomized per process, so the order of `GetSnapshot()`, `.Values` and `Collect()` could differ between runs. Sources now enumerate in **insertion order**; set semantics are unchanged (pushing an item that is already present is still a no-op):

| Operation | Position of the item |
|-----------|----------------------|
| `Push(item)` / `PushRange(items)` / batch `Push` | appended, in push order |
| `Push(existing)` | unchanged (no-op) |
| `Replace(old, new)` | `new` takes `old`'s position |
| `Remove(item)` then `Push(item)` | appended at the end |
| `Clear()` then `Push(...)` | a new order starts |

Code that sorted values only to compensate for the random order keeps working. Code that relied on a particular hash order (rare, and never stable across runs) should sort explicitly. Results should still not depend on processing order: see [cross-item lookups](docs/performance-guide.md#cross-item-lookups-eg-symbol-tables) for computing values such as symbol tables that combine all items.

## From Mutation-Based to Push-Based Sources

Earlier versions of the generator produced mutation methods (`AddX`, `RemoveX`, `SetX`, ...) and a `SaveChanges()` commit step. Those have been **removed**. Data now enters a context through push-based sources, and changes take effect immediately. This guide shows how to update code written against the old API.

There was no transition period: the mutation methods were removed outright, so code using them no longer compiles.

### Summary of Changes

#### Removed

- `Add{Property}(T item)`, `Remove{Property}(T item)`, `Replace{Property}(T oldItem, T newItem)`
- Batch variants such as `Add{Property}s(IEnumerable<T>)` / `Remove{Property}s(IEnumerable<T>)`
- `Set{Property}(T value)` for single-value properties
- `SaveChanges()`, `SaveChangesAsync(CancellationToken)`, `DiscardChanges()`, `HasPendingChanges`
- Generated change records and computation node classes

#### Generated now

For each `partial` get-only provider property `X`:

- `IIncrementalSource<T> XSource` for `IncrementalValuesProvider<T> X`
- `IIncrementalValueSource<T> XSource` for `IncrementalValueProvider<T> X`
- the implementation of `X` itself, and the context's public parameterless constructor

See the [Generated Code Reference](docs/generated-code-reference.md).

#### Source API

| `IIncrementalSource<T>` (collections) | `IIncrementalValueSource<T>` (single values) |
|---------------------------------------|----------------------------------------------|
| `Push(T item)` - no-op if an equal item exists | `Set(T value)` - no-op if equal to the current value; `null` throws |
| `Remove(T item)` - no-op if missing | `GetValue()` - current value, or `default` before the first `Set` (the provider reports an error instead) |
| `Replace(T oldItem, T newItem)` - throws if `oldItem` is missing or `newItem` already present; no-op if they are equal | |
| `Clear()` | |
| `GetSnapshot()` | |
| `PushRange(items)`, `BeginBatch()` (extension methods in `Guacamole`) | |

### Side-by-Side

The context declaration is unchanged:

```csharp
[ComputingContext]
public partial class CompilerContext : ComputingContextBase
{
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
    public partial IncrementalValueProvider<CompilerOptions> Options { get; }
}
```

**Before:**
```csharp
context.AddSourceFiles(file1);
context.AddSourceFiles(file2);
context.SetOptions(options);
context.SaveChanges();
```

**After:**
```csharp
context.SourceFilesSource.Push(file1);
context.SourceFilesSource.Push(file2);
context.OptionsSource.Set(options);
// No commit: each call notifies the context and runs its output handlers before returning
```

### Common Scenarios

#### Removing and replacing items

```csharp
// Before
context.RemoveSourceFiles(oldFile);
context.ReplaceSourceFiles(a, b);
context.SaveChanges();

// After
context.SourceFilesSource.Remove(oldFile);
context.SourceFilesSource.Replace(a, b);
```

Behavior differences:
- `Push` of an item that is already present is silently ignored (the old `Add` threw `ContextOperationException`).
- `Remove` of a missing item is a no-op.
- `Replace` throws `InvalidOperationException` if the old item is missing or the new item already exists.

#### Batch updates

```csharp
using Guacamole;

// Before
context.AddSourceFiless(files);
context.SaveChanges();

// After
context.SourceFilesSource.PushRange(files);
```

`PushRange` and a batch's `Commit()` apply their operations in one step with a single change notification (none if nothing changed), so output handlers run once. A batch is all or nothing: if one of its operations would fail, `Commit()` throws and applies none of them.

#### Discarding changes

There is no `DiscardChanges()`. A batch can be rolled back before it is committed:

```csharp
using (var batch = context.SourceFilesSource.BeginBatch())
{
    batch.Push(file1);
    batch.Push(file2);
    batch.Rollback();   // discards both
}
```

Note that **disposing a batch that was neither committed nor rolled back commits it**. Call `Rollback()` explicitly to discard.

#### Output handlers

`RegisterOutput` is unchanged, but handlers now run after every effective source change instead of after `SaveChanges()`:

- they run synchronously, on the thread that called `Push`/`Set`/...
- a handler runs only if its provider computes successfully and its value changed since the handler last ran
- if handlers throw, all handlers still run and the exceptions are rethrown together as an `AggregateException` from the source call

#### Derived properties

User-managed (derived) properties are non-`partial` with a setter. Because the generator now emits the context's parameterless constructor, assign them in a constructor that chains to `: this()`:

```csharp
[ComputingContext]
public partial class CompilerContext : ComputingContextBase
{
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }

    public IncrementalValuesProvider<SyntaxTree> SyntaxTrees { get; private set; }

    public CompilerContext(IParser parser) : this()
    {
        SyntaxTrees = SourceFiles.Select(parser.Parse);
    }
}
```

If your old context declared its own parameterless constructor, move that code into a constructor with parameters (or build the pipeline outside the context). Build pipelines once: distinct operator calls create new nodes with their own caches (identical calls with non-capturing lambdas are reused).

### Updating Tests

**Before:**
```csharp
var context = new CompilerContext();
context.AddSourceFiles(file);
context.SaveChanges();
Assert.Contains(file, context.SourceFiles.Values);
```

**After:**
```csharp
var context = new CompilerContext();
context.SourceFilesSource.Push(file);
Assert.Contains(file, context.SourceFiles.Values);
```

### Troubleshooting

**"My output handler is called more often than before."** Handlers used to run once per `SaveChanges()`; now they run after every effective change. Register handlers after bulk loading, or make them cheap. Handlers are skipped when their value did not change, so make sure item and result types have value equality (records or `IEquatable<T>`).

**"GUAC0008" (or CS0111 with older generator versions).** The generator emits the parameterless constructor; see [Derived properties](#derived-properties).

**"GUAC0003 / GUAC0005."** See the property rules in the [Generated Code Reference](docs/generated-code-reference.md#property-classification).
