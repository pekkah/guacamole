# Push-Based Incremental Compilation Architecture Research

**Date**: 2025-10-27
**Purpose**: Research and design a push-based data flow system to replace mutation methods in Guacamole's incremental compilation context

---

## Executive Summary

This document proposes replacing the current **mutation-based** approach (Add/Remove/Replace/SaveChanges) with a **push-based reactive architecture** where external data sources (like file watchers) push changes directly into the incremental computation graph. This design eliminates the need for explicit mutation methods and transaction boundaries, making the API more intuitive and enabling true reactive incremental compilation.

### Key Benefits
- **Elimination of mutation methods**: No more `AddSourceFiles()`, `RemoveSourceFiles()`, `SaveChanges()`
- **Reactive by default**: Changes flow automatically through the computation pipeline
- **Better separation of concerns**: Data sources are decoupled from the context
- **Closer to Roslyn's design**: Mirrors how Roslyn generators receive input pipelines
- **Cleaner API**: Users work with sources and pipelines, not mutation methods

---

## Current Architecture: Problems & Limitations

### Current Flow (Mutation-Based)

```
User Code: context.AddSourceFiles(file1, file2)
           ↓ (Change tracked)
User Code: context.SaveChanges()
           ↓ (Changes applied, cache invalidated)
           ↓ (Output handlers triggered)
Handler:   Receives recomputed value
```

### Problems with Current Approach

1. **Manual Transaction Management**
   - Users must remember to call `SaveChanges()`
   - Easy to forget, leading to stale data
   - Async complexity: when to await SaveChangesAsync?

2. **Tight Coupling**
   - Mutation methods are generated per-context
   - Data sources (file watchers, etc.) must know about context API
   - Hard to compose multiple data sources

3. **Not Reactive**
   - Changes don't flow automatically
   - Requires explicit commit to trigger recomputation
   - Breaks the reactive paradigm of incremental computation

4. **API Surface Bloat**
   - Every property generates 3-6 mutation methods
   - Methods like `AddSourceFilesBatch`, `RemoveSourceFiles`, etc.
   - Cognitive overhead for users

5. **Doesn't Match Roslyn's Design**
   - Roslyn generators receive **input providers** that are already reactive
   - No explicit mutation in Roslyn - just pipeline transformations
   - We're one abstraction layer below where we should be

### Current Generated Methods (Example)

```csharp
public partial class CompilerContext : ComputingContextBase
{
    // Generated: Mutation methods (what we want to eliminate)
    public void AddSourceFiles(SourceFile file) { ... }
    public void RemoveSourceFiles(SourceFile file) { ... }
    public void ReplaceSourceFiles(SourceFile oldFile, SourceFile newFile) { ... }
    public void AddSourceFilesBatch(IEnumerable<SourceFile> files) { ... }
    public void RemoveSourceFilesBatch(IEnumerable<SourceFile> files) { ... }
    public void SaveChanges() { ... }
    public Task SaveChangesAsync(CancellationToken ct) { ... }
    public void DiscardChanges() { ... }

    // The property users actually want
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
}
```

---

## Proposed Architecture: Push-Based Sources

### Core Concept

Instead of mutation methods, **properties become sources that external producers push data into**. The context becomes a **container of reactive sources** rather than a mutable state machine.

### New Flow (Push-Based)

```
External Source: fileWatcher.Push(file)
                ↓ (Directly updates source)
                ↓ (Cache automatically invalidated)
                ↓ (Output handlers automatically triggered)
Handler:        Receives recomputed value
```

### Design Goals

1. **Zero mutation methods**: No Add/Remove/Replace/SaveChanges
2. **Declarative data flow**: Users declare sources, data flows automatically
3. **Composable sources**: File watchers, network streams, timers, etc.
4. **Automatic invalidation**: No manual transaction boundaries
5. **Roslyn-like API**: Match Roslyn's generator initialization pattern

---

## Design: Source Abstraction

### Core Interfaces

```csharp
namespace Guacamole.Sources;

/// <summary>
/// Represents a source of data that can be pushed into the incremental computation graph.
/// Sources are the entry points for external data (file changes, network events, etc.)
/// </summary>
/// <typeparam name="T">Type of items this source produces</typeparam>
public interface IIncrementalSource<T>
{
    /// <summary>
    /// Unique identifier for this source (used for graph node registration)
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// The provider that exposes this source's values to the computation pipeline
    /// </summary>
    IncrementalValuesProvider<T> AsProvider();

    /// <summary>
    /// Adds an item to the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    void Push(T item);

    /// <summary>
    /// Removes an item from the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    void Remove(T item);

    /// <summary>
    /// Replaces an item in the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    void Replace(T oldItem, T newItem);

    /// <summary>
    /// Clears all items from the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets the current snapshot of items (thread-safe read)
    /// </summary>
    IReadOnlyCollection<T> GetSnapshot();
}

/// <summary>
/// Represents a single-value source (non-collection)
/// </summary>
public interface IIncrementalValueSource<T>
{
    string SourceId { get; }
    IncrementalValueProvider<T> AsProvider();
    void Set(T value);
    T GetValue();
}
```

### Concrete Implementation: IncrementalSource<T>

```csharp
namespace Guacamole.Sources;

/// <summary>
/// Thread-safe implementation of IIncrementalSource using immutable collections
/// </summary>
public sealed class IncrementalSource<T> : IIncrementalSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private ImmutableHashSet<T> _items = ImmutableHashSet<T>.Empty;
    private readonly ComputationGraph _graph;
    private readonly ICollectionComputationNode<T> _node;

    public string SourceId { get; }

    public IncrementalSource(string sourceId, ComputationGraph graph)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        // Create the computation node for this source
        _node = new SourceCollectionNode<T>(sourceId, this);
        _graph = _graph.AddNode(_node);
    }

    public IncrementalValuesProvider<T> AsProvider()
    {
        return new IncrementalValuesProvider<T>(
            _node,
            _graph,
            new ProviderMetadata($"Source: {SourceId}", "External source"));
    }

    public void Push(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _lock.EnterWriteLock();
        try
        {
            if (_items.Contains(item))
                return; // Already exists, no-op

            _items = _items.Add(item);

            // Invalidate cache immediately - no SaveChanges needed!
            InvalidateCacheAndTrigger();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _lock.EnterWriteLock();
        try
        {
            var newItems = _items.Remove(item);
            if (newItems == _items)
                return; // Item didn't exist, no change

            _items = newItems;
            InvalidateCacheAndTrigger();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Replace(T oldItem, T newItem)
    {
        ArgumentNullException.ThrowIfNull(oldItem);
        ArgumentNullException.ThrowIfNull(newItem);

        if (EqualityComparer<T>.Default.Equals(oldItem, newItem))
            return;

        _lock.EnterWriteLock();
        try
        {
            if (!_items.Contains(oldItem))
                throw new InvalidOperationException($"Item to replace not found in source '{SourceId}'");

            if (_items.Contains(newItem))
                throw new InvalidOperationException($"Replacement item already exists in source '{SourceId}'");

            _items = _items.Remove(oldItem).Add(newItem);
            InvalidateCacheAndTrigger();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_items.IsEmpty)
                return;

            _items = ImmutableHashSet<T>.Empty;
            InvalidateCacheAndTrigger();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IReadOnlyCollection<T> GetSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return _items.ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Invalidates cache and triggers dependent recomputation immediately
    /// </summary>
    private void InvalidateCacheAndTrigger()
    {
        // Remove this node from cache
        _graph.ValueCache.Remove(SourceId);

        // Find and invalidate all dependents
        var dependents = FindAllDependents();
        foreach (var dependent in dependents)
        {
            _graph.ValueCache.Remove(dependent);
        }

        // NOTE: We don't have direct access to output handlers here
        // This will be handled by the context's notification mechanism (see below)
    }

    private HashSet<string> FindAllDependents()
    {
        var dependents = new HashSet<string>();
        var toCheck = new Queue<string>();
        toCheck.Enqueue(SourceId);

        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();

            foreach (var kvp in _graph.Dependencies)
            {
                if (kvp.Value.Contains(current) && !dependents.Contains(kvp.Key))
                {
                    dependents.Add(kvp.Key);
                    toCheck.Enqueue(kvp.Key);
                }
            }
        }

        return dependents;
    }

    /// <summary>
    /// Internal computation node that reads from the source
    /// </summary>
    private sealed class SourceCollectionNode<TItem> : ICollectionComputationNode<TItem>
    {
        private readonly IncrementalSource<TItem> _source;

        public string Id { get; }
        public Type OutputType => typeof(IEnumerable<TItem>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty; // Sources have no dependencies

        public SourceCollectionNode(string id, IncrementalSource<TItem> source)
        {
            Id = id;
            _source = source;
        }

        public Result<IEnumerable<TItem>, ComputationError> Compute(ComputationContext context)
        {
            // Check cache first
            if (context.CachedValues.TryGetValue(Id, out var cached) && cached is IEnumerable<TItem> cachedItems)
                return Result<IEnumerable<TItem>, ComputationError>.Success(cachedItems);

            // Read current snapshot from source
            var items = _source.GetSnapshot();
            return Result<IEnumerable<TItem>, ComputationError>.Success(items);
        }
    }
}
```

---

## Updated Context Design

### New Context API (No Mutations)

```csharp
[ComputingContext]
public partial class CompilerContext : ComputingContextBase
{
    // Properties are now SOURCES, not providers with mutation methods
    // The source generator creates a backing IIncrementalSource<T> field
    // and exposes it via AsProvider()

    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
    public partial IncrementalValueProvider<CompilationOptions> Options { get; }

    // Behind the scenes (generated):
    // private readonly IncrementalSource<SourceFile> _sourceFilesSource;
    // public IncrementalValuesProvider<SourceFile> SourceFiles => _sourceFilesSource.AsProvider();

    // Public access to push into sources (generated):
    public IIncrementalSource<SourceFile> SourceFilesSource => _sourceFilesSource;
    public IIncrementalValueSource<CompilationOptions> OptionsSource => _optionsSource;
}

// Usage - much cleaner!
var context = new CompilerContext();

// Get the source and push data
var sourceFilesSource = context.SourceFilesSource;
sourceFilesSource.Push(new SourceFile("Program.cs", "content"));

// Or create external adapters
var fileWatcher = new FileSystemWatcher("/src");
fileWatcher.Changed += (s, e) => sourceFilesSource.Push(ReadFile(e.FullPath));

// Pipeline works the same
var diagnostics = context.SourceFiles
    .Select(file => Compile(file))
    .Where(result => result.HasErrors)
    .SelectMany(result => result.Errors);

// Register outputs (automatic reactive behavior)
context.RegisterOutput(diagnostics, (errors, ctx) =>
{
    Console.WriteLine($"Compilation errors: {errors.Count()}");
});

// No SaveChanges needed - changes flow automatically!
```

---

## Example: File Watcher Integration

### Complete File Watcher Implementation

```csharp
namespace Guacamole.Examples.Compiler;

/// <summary>
/// Watches a directory for source file changes and pushes them to an incremental source
/// </summary>
public sealed class SourceFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly IIncrementalSource<SourceFile> _sourceFilesSource;
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, SourceFile> _fileMap = new();

    public SourceFileWatcher(string path, IIncrementalSource<SourceFile> sourceFilesSource)
    {
        _basePath = Path.GetFullPath(path);
        _sourceFilesSource = sourceFilesSource ?? throw new ArgumentNullException(nameof(sourceFilesSource));

        _watcher = new FileSystemWatcher(_basePath)
        {
            Filter = "*.cs",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = false,
            IncludeSubdirectories = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
    }

    public void Start()
    {
        // Initial scan
        foreach (var filePath in Directory.EnumerateFiles(_basePath, "*.cs", SearchOption.AllDirectories))
        {
            var file = LoadSourceFile(filePath);
            if (file != null)
            {
                _fileMap[filePath] = file;
                _sourceFilesSource.Push(file);
            }
        }

        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        var file = LoadSourceFile(e.FullPath);
        if (file != null)
        {
            _fileMap[e.FullPath] = file;
            _sourceFilesSource.Push(file); // Automatically triggers recomputation!
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var newFile = LoadSourceFile(e.FullPath);
        if (newFile != null && _fileMap.TryGetValue(e.FullPath, out var oldFile))
        {
            _fileMap[e.FullPath] = newFile;
            _sourceFilesSource.Replace(oldFile, newFile); // Automatically triggers recomputation!
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (_fileMap.TryRemove(e.FullPath, out var file))
        {
            _sourceFilesSource.Remove(file); // Automatically triggers recomputation!
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Treat as delete + create
        if (_fileMap.TryRemove(e.OldFullPath, out var oldFile))
        {
            _sourceFilesSource.Remove(oldFile);
        }

        var newFile = LoadSourceFile(e.FullPath);
        if (newFile != null)
        {
            _fileMap[e.FullPath] = newFile;
            _sourceFilesSource.Push(newFile);
        }
    }

    private SourceFile? LoadSourceFile(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var relativePath = Path.GetRelativePath(_basePath, path);
            return new SourceFile(relativePath, content, File.GetLastWriteTimeUtc(path));
        }
        catch (IOException)
        {
            return null; // File locked or not accessible
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}

/// <summary>
/// Source file record with content hash for efficient equality comparison
/// </summary>
public sealed record SourceFile(string Path, string Content, DateTime LastModified)
{
    private readonly Lazy<int> _contentHash = new(() => ComputeContentHash(Content));

    public bool Equals(SourceFile? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        // Fast path: compare hashes first
        return Path == other.Path && _contentHash.Value == other._contentHash.Value;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Path, _contentHash.Value);
    }

    private static int ComputeContentHash(string content)
    {
        // Use xxHash or similar for fast content hashing
        return content.GetHashCode();
    }
}

// Usage Example
public class Program
{
    public static async Task Main(string[] args)
    {
        using var context = new CompilerContext();

        // Set up the file watcher to push source files
        using var watcher = new SourceFileWatcher("./src", context.SourceFilesSource);

        // Define the compilation pipeline
        var compilationResults = context.SourceFiles
            .Select(file => ParseAndCompile(file))
            .Collect();

        var diagnostics = compilationResults
            .SelectMany(results => results)
            .Where(result => result.HasErrors)
            .SelectMany(result => result.Diagnostics);

        // Register output handler - called automatically when files change
        context.RegisterOutput(diagnostics, (errors, ctx) =>
        {
            Console.Clear();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Compilation completed");
            Console.WriteLine($"Total errors: {errors.Count()}");

            foreach (var error in errors.Take(10))
            {
                Console.WriteLine($"  {error.Severity} {error.Code}: {error.Message}");
                Console.WriteLine($"    at {error.Location}");
            }
        });

        // Start watching - changes flow automatically!
        watcher.Start();

        Console.WriteLine("Watching for file changes. Press Ctrl+C to exit...");
        await Task.Delay(Timeout.Infinite);
    }

    private static CompilationResult ParseAndCompile(SourceFile file)
    {
        // Actual compilation logic here
        return new CompilationResult(file.Path, ...);
    }
}
```

---

## Source Generator Changes

### Updated Property Generation

```csharp
// OLD: Generated mutation-heavy property
public partial IncrementalValuesProvider<SourceFile> SourceFiles
{
    get
    {
        if (_sourceFilesNode == null)
        {
            lock (_sourceFilesNodeLock)
            {
                if (_sourceFilesNode == null)
                {
                    _sourceFilesNode = new SourceFilesNode(this);
                    Graph = Graph.AddNode(_sourceFilesNode);
                }
            }
        }
        return new IncrementalValuesProvider<SourceFile>(_sourceFilesNode, Graph, ...);
    }
}

// NEW: Source-backed property (no mutations!)
private readonly IncrementalSource<SourceFile> _sourceFilesSource;

public IncrementalValuesProvider<SourceFile> SourceFiles => _sourceFilesSource.AsProvider();

public IIncrementalSource<SourceFile> SourceFilesSource => _sourceFilesSource;
```

### Updated Context Constructor Generation

```csharp
// Generated constructor initializes sources
public CompilerContext() : base()
{
    // Initialize sources (not state + mutation tracking!)
    _sourceFilesSource = new IncrementalSource<SourceFile>("CompilerContext.SourceFiles", Graph);
    _optionsSource = new IncrementalValueSource<CompilationOptions>("CompilerContext.Options", Graph);
}
```

### What Gets Removed from Generation

- ❌ `MutationMethodGenerator.cs` - Completely removed
- ❌ `ChangeTypeGenerator.cs` - Completely removed
- ❌ `SaveChangesGenerator.cs` - Completely removed
- ❌ All `_current{Property}` fields
- ❌ All `_{Property}Changes` lists
- ❌ `HasPendingChanges` property
- ✅ Keep: `ComputationNodeGenerator.cs` (but simplified to just wrap sources)
- ✅ Keep: `PropertyGenerator.cs` (but generates source-backed properties)

---

## Handling Output Triggers

### Challenge: Output Handler Notification

In the current design, `SaveChanges()` explicitly calls `TriggerOutputs()` after committing changes. With push-based sources, we need **automatic notification** when sources change.

### Solution 1: Observable Pattern (Recommended)

Sources implement `IObservable<ChangeEvent>` and the context subscribes:

```csharp
public sealed class IncrementalSource<T> : IIncrementalSource<T>, IObservable<SourceChangeEvent>
{
    private readonly List<IObserver<SourceChangeEvent>> _observers = new();

    public IDisposable Subscribe(IObserver<SourceChangeEvent> observer)
    {
        _observers.Add(observer);
        return new Unsubscriber(_observers, observer);
    }

    private void NotifyObservers()
    {
        var changeEvent = new SourceChangeEvent(SourceId, DateTime.UtcNow);
        foreach (var observer in _observers)
        {
            observer.OnNext(changeEvent);
        }
    }

    public void Push(T item)
    {
        // ... existing logic ...
        NotifyObservers(); // Notify after change
    }
}

// Context subscribes to all sources
public abstract partial class ComputingContextBase
{
    protected void SubscribeToSource<T>(IIncrementalSource<T> source)
    {
        source.Subscribe(new SourceChangeObserver(this));
    }

    private sealed class SourceChangeObserver : IObserver<SourceChangeEvent>
    {
        private readonly ComputingContextBase _context;

        public SourceChangeObserver(ComputingContextBase context)
        {
            _context = context;
        }

        public void OnNext(SourceChangeEvent changeEvent)
        {
            // Trigger output handlers when any source changes
            _context.TriggerOutputs();
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}

// Generated constructor subscribes
public CompilerContext() : base()
{
    _sourceFilesSource = new IncrementalSource<SourceFile>("...", Graph);
    SubscribeToSource(_sourceFilesSource); // Auto-subscribe
}
```

### Solution 2: Event-Based (Simpler)

```csharp
public sealed class IncrementalSource<T> : IIncrementalSource<T>
{
    public event EventHandler<SourceChangedEventArgs>? Changed;

    public void Push(T item)
    {
        // ... existing logic ...
        Changed?.Invoke(this, new SourceChangedEventArgs(SourceId));
    }
}

// Context hooks up event handlers
public CompilerContext() : base()
{
    _sourceFilesSource = new IncrementalSource<SourceFile>("...", Graph);
    _sourceFilesSource.Changed += (s, e) => TriggerOutputs();
}
```

---

## Migration Path

### Phase 1: Add Sources (Non-Breaking)

1. Implement `IIncrementalSource<T>` and `IncrementalValueSource<T>`
2. Update source generator to create both:
   - Source fields (new)
   - Mutation methods (keep for backwards compatibility)
3. Mutation methods internally delegate to sources:
   ```csharp
   public void AddSourceFiles(SourceFile file)
   {
       _sourceFilesSource.Push(file);
   }
   ```

### Phase 2: Deprecate Mutations

1. Mark mutation methods with `[Obsolete]`
2. Update documentation to show source-based patterns
3. Provide migration guide

### Phase 3: Remove Mutations (Breaking Change)

1. Remove mutation method generation
2. Remove SaveChanges/DiscardChanges
3. Bump major version (2.0.0)

---

## Advanced Patterns

### Batched Updates

For performance, allow batching:

```csharp
public interface IIncrementalSource<T>
{
    // Existing methods...

    /// <summary>
    /// Begins a batch operation. Changes are buffered and invalidation is deferred.
    /// </summary>
    IBatchOperation BeginBatch();
}

public interface IBatchOperation : IDisposable
{
    void Push(T item);
    void Remove(T item);
    void Commit(); // Triggers single invalidation for all changes
    void Rollback();
}

// Usage
using (var batch = source.BeginBatch())
{
    foreach (var file in GetAllFiles())
    {
        batch.Push(file);
    }
    batch.Commit(); // Single invalidation for all files
}
```

### Composite Sources

Combine multiple sources:

```csharp
public static IIncrementalSource<T> Merge<T>(
    params IIncrementalSource<T>[] sources)
{
    return new CompositeSource<T>(sources);
}

// Usage
var allFiles = IncrementalSource.Merge(
    context.SourceFilesSource,
    context.GeneratedFilesSource
);
```

### Filtered Sources

Create derived sources with filters:

```csharp
public static IIncrementalSource<T> Where<T>(
    this IIncrementalSource<T> source,
    Func<T, bool> predicate)
{
    return new FilteredSource<T>(source, predicate);
}

// Usage
var csFiles = context.SourceFilesSource
    .Where(file => file.Path.EndsWith(".cs"));
```

---

## Performance Considerations

### Current Architecture Performance

| Aspect | Current (Mutations) | Proposed (Sources) |
|--------|-------------------|-------------------|
| **Memory** | Change lists + current state | Only current state (immutable) |
| **Lock Contention** | High (write lock for tracking + commit) | Lower (write lock only on actual change) |
| **Invalidation** | Batched (on SaveChanges) | Immediate (per-change) |
| **Throughput** | Better for bulk changes | Better for incremental changes |

### Optimization: Batched Invalidation

```csharp
private int _batchDepth = 0;
private bool _needsInvalidation = false;

public IBatchOperation BeginBatch()
{
    Interlocked.Increment(ref _batchDepth);
    return new BatchOperation(this);
}

private void EndBatch()
{
    if (Interlocked.Decrement(ref _batchDepth) == 0)
    {
        if (_needsInvalidation)
        {
            InvalidateCacheAndTrigger();
            _needsInvalidation = false;
        }
    }
}

public void Push(T item)
{
    // ... update items ...

    if (_batchDepth > 0)
        _needsInvalidation = true;
    else
        InvalidateCacheAndTrigger();
}
```

---

## Comparison with Roslyn

### Roslyn's Approach

```csharp
[Generator]
public class MyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Roslyn provides these as SOURCES
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(...)
            .Where(...)
            .Select(...);

        // No mutation methods!
        // Sources are push-based from the compiler

        context.RegisterSourceOutput(classDeclarations, (spc, cls) =>
        {
            // Generate code
        });
    }
}
```

### Our Approach (After This Design)

```csharp
public class MyCompiler
{
    public void Initialize()
    {
        var context = new CompilerContext();

        // We provide sources that external systems push to
        var sourceFiles = context.SourceFilesSource;

        // Pipeline is identical to Roslyn
        var classDeclarations = context.SourceFiles
            .Select(file => Parse(file))
            .SelectMany(tree => tree.GetClasses())
            .Where(cls => cls.IsPartial);

        // Output registration is identical
        context.RegisterOutput(classDeclarations, (classes, ctx) =>
        {
            // Generate code
        });

        // External system pushes data (this is the only difference)
        fileWatcher.Changed += (s, e) =>
        {
            var file = ReadFile(e.FullPath);
            sourceFiles.Push(file); // Push-based!
        };
    }
}
```

**Key Insight**: Roslyn owns the compiler, so it controls the sources. We're building a framework, so users control the sources. The solution is to give them push-based sources they can feed from external systems.

---

## Recommended Implementation Order

1. **Week 1-2**: Core Source Infrastructure
   - [ ] Implement `IIncrementalSource<T>` interface
   - [ ] Implement `IncrementalSource<T>` class
   - [ ] Implement `IncrementalValueSource<T>` for single values
   - [ ] Add unit tests for thread safety and correctness

2. **Week 2-3**: Integration with Computation Graph
   - [ ] Create `SourceCollectionNode<T>` and `SourceValueNode<T>`
   - [ ] Implement automatic cache invalidation
   - [ ] Add observable pattern for change notification
   - [ ] Integrate with `TriggerOutputs()` mechanism

3. **Week 3-4**: Source Generator Updates
   - [ ] Update property generator to create source-backed properties
   - [ ] Keep mutation methods, make them delegate to sources (backwards compatibility)
   - [ ] Update context constructor generation
   - [ ] Add `{Property}Source` accessor properties

4. **Week 4-5**: Examples and Documentation
   - [ ] Implement `FileSystemWatcher` example
   - [ ] Create network stream source example
   - [ ] Write migration guide
   - [ ] Update API documentation

5. **Week 5-6**: Performance Optimization
   - [ ] Implement batched operations
   - [ ] Add benchmarks comparing mutation vs push-based
   - [ ] Optimize lock contention
   - [ ] Profile and tune

6. **Week 6+**: Deprecation and Cleanup
   - [ ] Mark mutation methods as `[Obsolete]`
   - [ ] Gather feedback from users
   - [ ] Plan breaking change for 2.0.0
   - [ ] Eventually remove mutation generation

---

## Open Questions

1. **Automatic vs Manual Trigger**
   - Should sources always trigger immediately, or allow users to control triggering?
   - Proposal: Immediate by default, with `BeginBatch()` for manual control

2. **Error Handling**
   - What happens if a Push() operation fails (e.g., invalid data)?
   - Should sources validate, or leave that to the pipeline?
   - Proposal: Sources do minimal validation (null checks), pipelines do domain validation

3. **Async Sources**
   - Should sources support async operations?
   - Do we need `PushAsync()` for I/O-bound scenarios?
   - Proposal: Keep sources synchronous, use async wrappers if needed

4. **Source Disposal**
   - Should sources be disposable?
   - What happens to the graph when a source is disposed?
   - Proposal: Sources implement `IDisposable`, removal from graph is manual

5. **Multi-tenancy**
   - Can multiple contexts share sources?
   - Should sources support multiple subscribers?
   - Proposal: Yes, sources are reusable across contexts

---

## Conclusion

The push-based source architecture provides a **cleaner, more reactive API** that better matches Roslyn's design while enabling true incremental compilation. By eliminating mutation methods and transaction boundaries, we make the framework more intuitive and powerful.

### Key Takeaways

✅ **Eliminate SaveChanges()**: No more manual transaction management
✅ **Reactive by default**: Changes flow automatically through pipelines
✅ **Better composability**: Sources are first-class, reusable components
✅ **Matches Roslyn**: API surface similar to Roslyn's incremental generators
✅ **Extensible**: Easy to add new source types (timers, networks, etc.)

### Next Steps

1. **Prototype** the core `IncrementalSource<T>` implementation
2. **Validate** with file watcher example
3. **Benchmark** performance vs current mutation-based approach
4. **Get feedback** from potential users
5. **Iterate** on design based on real-world usage

---

**End of Research Document**
