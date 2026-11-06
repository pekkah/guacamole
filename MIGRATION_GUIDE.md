# Migration Guide: From Mutation-Based to Push-Based Sources

Earlier versions of the generator produced mutation methods (`AddX`, `RemoveX`, `SetX`, ...) and a `SaveChanges()` commit step. Those have been **removed**. Data now enters a context through push-based sources, and changes take effect immediately. This guide shows how to update code written against the old API.

There was no transition period: the mutation methods were removed outright, so code using them no longer compiles.

## Summary of Changes

### Removed

- `Add{Property}(T item)`, `Remove{Property}(T item)`, `Replace{Property}(T oldItem, T newItem)`
- Batch variants such as `Add{Property}s(IEnumerable<T>)` / `Remove{Property}s(IEnumerable<T>)`
- `Set{Property}(T value)` for single-value properties
- `SaveChanges()`, `SaveChangesAsync(CancellationToken)`, `DiscardChanges()`, `HasPendingChanges`
- Generated change records and computation node classes

### Generated now

For each `partial` get-only provider property `X`:

- `IIncrementalSource<T> XSource` for `IncrementalValuesProvider<T> X`
- `IIncrementalValueSource<T> XSource` for `IncrementalValueProvider<T> X`
- the implementation of `X` itself, and the context's public parameterless constructor

See the [Generated Code Reference](docs/generated-code-reference.md).

### Source API

| `IIncrementalSource<T>` (collections) | `IIncrementalValueSource<T>` (single values) |
|---------------------------------------|----------------------------------------------|
| `Push(T item)` - no-op if an equal item exists | `Set(T value)` - no-op if equal to the current value; `null` throws |
| `Remove(T item)` - no-op if missing | `GetValue()` - current value or `default` |
| `Replace(T oldItem, T newItem)` - throws if `oldItem` is missing or `newItem` already present | |
| `Clear()` | |
| `GetSnapshot()` | |
| `PushRange(items)`, `BeginBatch()` (extension methods in `Guacamole.Sources`) | |

## Side-by-Side

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

## Common Scenarios

### Removing and replacing items

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

### Batch updates

```csharp
using Guacamole.Sources;

// Before
context.AddSourceFiless(files);
context.SaveChanges();

// After
context.SourceFilesSource.PushRange(files);
```

`PushRange` and `BeginBatch()` apply operations one at a time; each effective change raises its own notification and runs the output handlers. There is no single coalesced invalidation.

### Discarding changes

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

### Output handlers

`RegisterOutput` is unchanged, but handlers now run after every effective source change instead of after `SaveChanges()`:

- they run synchronously, on the thread that called `Push`/`Set`/...
- a handler runs only if its provider computes successfully and its value changed since the handler last ran
- if handlers throw, all handlers still run and the exceptions are rethrown together as an `AggregateException` from the source call

### Derived properties

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

## Updating Tests

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

## Troubleshooting

**"My output handler is called more often than before."** Handlers used to run once per `SaveChanges()`; now they run after every effective change. Register handlers after bulk loading, or make them cheap. Handlers are skipped when their value did not change, so make sure item and result types have value equality (records or `IEquatable<T>`).

**"GUAC0008" (or CS0111 with older generator versions).** The generator emits the parameterless constructor; see [Derived properties](#derived-properties).

**"GUAC0003 / GUAC0005."** See the property rules in the [Generated Code Reference](docs/generated-code-reference.md#property-classification).
