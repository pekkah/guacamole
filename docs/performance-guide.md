# Performance Guide: ComputingContext

This guide describes the performance characteristics and optimization strategies for ComputingContext.

## Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| Cached Access | <1ms | Reading from provider cache |
| Incremental Update | <10ms | Small change propagation |
| Memory Overhead | <2x | Compared to raw data |
| Complexity | O(changes) | Not O(total data size) |
| Boxing/Reflection | Zero | Compile-time code generation |

## Zero Boxing Architecture

### Problem: Boxing Overhead

Traditional change tracking uses `object` or interfaces:
```csharp
// ❌ BAD - causes boxing for value types
List<IChange> changes;  // IChange forces boxing
changes.Add(new Change { Item = 42 });  // int boxed to object
```

### Solution: Generated Strongly-Typed Records

```csharp
// ✅ GOOD - no boxing
List<SourceFileChange> changes;  // Concrete type
changes.Add(new SourceFileChange.Add(sourceFile, timestamp));  // No boxing
```

**Benefit**:
- No heap allocations for value types
- Better CPU cache utilization
- JIT can optimize switch expressions

## O(changes) Complexity

### Principle

Incremental recomputation should scale with the number of changes, not the total dataset size.

### Example

```csharp
// Dataset: 10,000 files
context.AddSourceFiles(Enumerable.Range(1, 10000).Select(CreateFile));
await context.SaveChangesAsync();  // Time: T

// Change 1 file out of 10,000
context.ReplaceSourceFile(oldFile, newFile);
await context.SaveChangesAsync();  // Time: ~T/10000 (not T!)
```

### How It Works

1. **Change Tracking**: Only modified items tracked in change lists
2. **Graph Invalidation**: Only dependent nodes invalidated
3. **Lazy Recomputation**: Only when values are queried
4. **Precise Caching**: Cache keys include item identity

### Validation

Use `IncrementalUpdateBenchmarks.cs` to verify:
```csharp
[Params(10, 100, 1000)]
public int TotalItems { get; set; }

[Params(1, 10)]
public int ChangedItems { get; set; }

// Should scale with ChangedItems, not TotalItems
```

## Memory Efficiency

### Target: <2x Overhead

**Baseline**: Raw data in `HashSet<T>`
```csharp
HashSet<SourceFile> files;  // Baseline memory
```

**With Context**: Data + Change Tracking
```csharp
HashSet<SourceFile> _currentSourceFiles;  // Same as baseline
List<SourceFileChange> _SourceFilesChanges;  // Usually small
```

**Total**: ~1.1x-1.5x depending on pending changes

### Memory Management

1. **Change Lists Cleared**: After `SaveChanges()`, lists are emptied
2. **LRU Cache**: Computation graph uses LRU eviction for cache
3. **No Memory Leaks**: ReaderWriterLockSlim properly disposed

### Benchmarking

```csharp
[MemoryDiagnoser]
public class MemoryEfficiencyBenchmarks
{
    [Params(100, 1000, 10000)]
    public int ItemCount { get; set; }

    [Benchmark(Baseline = true)]
    public void BaselineMemory()
    {
        var data = new HashSet<SourceFile>(CreateFiles(ItemCount));
    }

    [Benchmark]
    public void ContextWithTracking()
    {
        var context = new CompilerContext(services);
        context.AddSourceFiles(CreateFiles(ItemCount));
        context.SaveChanges();
    }
}
```

## Thread Safety Model

### Multiple Readers, Single Writer

```csharp
// Concurrent reads OK
Task.WaitAll(
    Task.Run(() => context.SourceFiles.ToList()),
    Task.Run(() => context.SourceFiles.Count()),
    Task.Run(() => context.SourceFiles.First())
);

// Write blocks all
context.AddSourceFile(newFile);  // Exclusive lock
```

### Lock Implementation

```csharp
protected ReaderWriterLockSlim Lock { get; }

// Mutations acquire write lock
Lock.EnterWriteLock();
try { /* mutation */ }
finally { Lock.ExitWriteLock(); }

// Reads can happen concurrently (no explicit lock needed)
var items = context.SourceFiles.ToList();
```

### Performance Impact

- **Read contention**: None - multiple readers allowed
- **Write contention**: Blocked - only one writer at a time
- **Lock acquisition**: ~100ns on modern hardware

## Hot Path Optimizations

### 1. Duplicate Detection

```csharp
// O(1) duplicate check using HashSet
if (_currentSourceFiles.Contains(item))
{
    throw ContextOperationException.DuplicateItem("SourceFiles", item);
}
```

**Benefit**: Fast duplicate detection without linear scan

### 2. Change Application

```csharp
// Direct HashSet modification (no extra allocations)
foreach (var change in _SourceFilesChanges)
{
    switch (change)  // JIT optimizes sealed record switches
    {
        case SourceFileChange.Add add:
            _currentSourceFiles.Add(add.Item);  // O(1)
            break;
        // ...
    }
}
```

**Benefit**: Minimal allocations, optimal collection operations

### 3. Property Access

```csharp
// Lazy initialization with null check (no lock needed)
if (_SourceFilesProvider == null)
{
    _SourceFilesProvider = new IncrementalValuesProvider<SourceFile>(...);
}
return _SourceFilesProvider.Value;
```

**Benefit**: Read path has no lock overhead

## Benchmarking Your Context

### Setup BenchmarkDotNet

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class MyContextBenchmarks
{
    private CompilerContext _context;

    [GlobalSetup]
    public void Setup()
    {
        _context = new CompilerContext(services);
        // Pre-populate with data
    }

    [Benchmark]
    public void AddFile()
    {
        _context.AddSourceFile(CreateFile());
    }

    [Benchmark]
    public void SaveChanges()
    {
        _context.SaveChanges();
    }
}

class Program
{
    static void Main(string[] args)
    {
        BenchmarkRunner.Run<MyContextBenchmarks>();
    }
}
```

### Key Metrics to Watch

1. **Allocations**: Should be minimal for hot paths
2. **Mean Time**: Should meet <1ms or <10ms targets
3. **Scalability**: O(changes) not O(data size)

## Common Performance Issues

### Issue 1: Large Batch Operations

**Problem**: Adding 10,000 items at once
```csharp
context.AddSourceFiles(hugeList);  // All tracked in memory
```

**Solution**: Commit in chunks
```csharp
foreach (var batch in hugeList.Chunk(100))
{
    context.AddSourceFiles(batch);
    await context.SaveChangesAsync();
}
```

### Issue 2: Forgetting to SaveChanges

**Problem**: Changes accumulate without commit
```csharp
for (int i = 0; i < 10000; i++)
{
    context.AddSourceFile(files[i]);  // Memory grows!
}
// Never calls SaveChanges - change list has 10k items
```

**Solution**: Regular commits
```csharp
for (int i = 0; i < 10000; i++)
{
    context.AddSourceFile(files[i]);
    if (i % 100 == 0)
        await context.SaveChangesAsync();
}
```

### Issue 3: Over-Invalidation

**Problem**: Too many properties depend on one source
```csharp
// Every property depends on SourceFiles
ParsedTrees = SourceFiles.Select(...);
Types = SourceFiles.Select(...);
Errors = SourceFiles.Select(...);
```

**Solution**: Chain dependencies
```csharp
// Build dependency chain
ParsedTrees = SourceFiles.Select(Parse);
Types = ParsedTrees.SelectMany(ExtractTypes);
Errors = Types.SelectMany(AnalyzeType);
```

## Profiling Tools

### 1. Visual Studio Profiler
- CPU usage
- Memory allocations
- Lock contention

### 2. dotTrace/dotMemory
- Detailed allocation tracking
- Timeline analysis

### 3. BenchmarkDotNet
- Accurate microbenchmarks
- Statistical analysis
- Memory diagnostics

## Performance Checklist

- [ ] Run benchmarks to establish baseline
- [ ] Verify O(changes) complexity with different dataset sizes
- [ ] Check memory overhead is <2x
- [ ] Measure cached access (<1ms target)
- [ ] Measure incremental updates (<10ms target)
- [ ] Profile hot paths for allocations
- [ ] Test concurrent access patterns
- [ ] Validate no boxing in IL (use ILSpy)

## References

- Computation Graph: `src/Guacamole.Core/Pipeline/ComputationGraph.cs`
- LRU Cache: `src/Guacamole.Core/Caching/LRUCache.cs`
- Benchmarks: `benchmarks/ComputingContext.Benchmarks/`