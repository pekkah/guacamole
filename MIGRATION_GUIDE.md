# Migration Guide: From Mutation-Based to Push-Based Architecture

This guide shows how to migrate from the current mutation-based API to the proposed push-based architecture.

---

## Overview

The push-based architecture eliminates manual mutation methods (`Add`, `Remove`, `SaveChanges`) and replaces them with **reactive sources** that automatically invalidate the computation graph when data changes.

### Benefits of Migration

✅ **No more SaveChanges()** - Changes flow automatically
✅ **Simpler API** - Fewer methods to remember
✅ **True reactive behavior** - Matches Roslyn's design
✅ **Better composability** - Sources are first-class objects
✅ **Less boilerplate** - No transaction management

---

## Side-by-Side Comparison

### Before: Mutation-Based (Current)

```csharp
[ComputingContext]
public partial class CompilerContext : ComputingContextBase
{
    // Property with generated mutation methods
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
}

// Usage
var context = new CompilerContext();

// Mutation methods (generated)
context.AddSourceFiles(file1);
context.AddSourceFiles(file2);
context.AddSourceFiles(file3);

// MUST call SaveChanges to commit
context.SaveChanges(); // <-- Easy to forget!

// Pipeline
var diagnostics = context.SourceFiles
    .Select(file => Compile(file))
    .Where(result => result.HasErrors)
    .SelectMany(result => result.Errors);

context.RegisterOutput(diagnostics, (errors, ctx) =>
{
    Console.WriteLine($"Errors: {errors.Count()}");
});

// File watcher - must manually call mutations
fileWatcher.Changed += (s, e) =>
{
    var file = ReadFile(e.FullPath);
    context.AddSourceFiles(file);
    context.SaveChanges(); // <-- Must remember this!
};
```

### After: Push-Based (Proposed)

```csharp
[ComputingContext]
public partial class CompilerContext : ComputingContextBase
{
    // Property is now backed by a source
    public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }

    // Accessor for the source (generated)
    public IIncrementalSource<SourceFile> SourceFilesSource { get; }
}

// Usage
var context = new CompilerContext();

// Get the source
var sourceFilesSource = context.SourceFilesSource;

// Push data (NO SaveChanges needed!)
sourceFilesSource.Push(file1);
sourceFilesSource.Push(file2);
sourceFilesSource.Push(file3);
// Changes flow automatically! ✨

// Pipeline (unchanged)
var diagnostics = context.SourceFiles
    .Select(file => Compile(file))
    .Where(result => result.HasErrors)
    .SelectMany(result => result.Errors);

context.RegisterOutput(diagnostics, (errors, ctx) =>
{
    Console.WriteLine($"Errors: {errors.Count()}");
});

// File watcher - just push, automatic recomputation!
fileWatcher.Changed += (s, e) =>
{
    var file = ReadFile(e.FullPath);
    sourceFilesSource.Push(file); // <-- That's it!
};
```

---

## Common Scenarios

### Scenario 1: Initial Data Load

**Before:**
```csharp
var context = new CompilerContext();

foreach (var file in Directory.GetFiles("./src", "*.cs"))
{
    context.AddSourceFiles(ReadFile(file));
}

context.SaveChanges(); // Must remember!
```

**After:**
```csharp
var context = new CompilerContext();
var source = context.SourceFilesSource;

foreach (var file in Directory.GetFiles("./src", "*.cs"))
{
    source.Push(ReadFile(file));
    // Automatic recomputation after each file!
}

// Or use batch for better performance:
using (var batch = source.BeginBatch())
{
    foreach (var file in Directory.GetFiles("./src", "*.cs"))
    {
        batch.Push(ReadFile(file));
    }
    batch.Commit(); // Single invalidation for all files
}
```

### Scenario 2: File Watcher

**Before:**
```csharp
var watcher = new FileSystemWatcher("./src");
watcher.Changed += (s, e) =>
{
    try
    {
        var file = ReadFile(e.FullPath);
        context.AddSourceFiles(file);
        context.SaveChanges();
    }
    catch (Exception ex)
    {
        // Handle errors
    }
};
```

**After:**
```csharp
var source = context.SourceFilesSource;
var watcher = new FileSystemWatcher("./src");
watcher.Changed += (s, e) =>
{
    try
    {
        var file = ReadFile(e.FullPath);
        source.Push(file); // Automatic!
    }
    catch (Exception ex)
    {
        // Handle errors
    }
};
```

### Scenario 3: Removing Items

**Before:**
```csharp
context.RemoveSourceFiles(fileToRemove);
context.SaveChanges(); // Must remember!
```

**After:**
```csharp
source.Remove(fileToRemove); // Automatic!
```

### Scenario 4: Replacing Items

**Before:**
```csharp
context.ReplaceSourceFiles(oldFile, newFile);
context.SaveChanges(); // Must remember!
```

**After:**
```csharp
source.Replace(oldFile, newFile); // Automatic!
```

### Scenario 5: Batch Updates

**Before:**
```csharp
context.AddSourceFilesBatch(files);
context.SaveChanges();
```

**After:**
```csharp
source.PushRange(files);
// Or for more control:
using (var batch = source.BeginBatch())
{
    foreach (var file in files)
    {
        batch.Push(file);
    }
    batch.Commit();
}
```

### Scenario 6: Discarding Changes

**Before:**
```csharp
context.AddSourceFiles(file1);
context.AddSourceFiles(file2);
// Oh no, mistake!
context.DiscardChanges(); // Rollback
```

**After:**
```csharp
// Use batch operations for rollback capability
using (var batch = source.BeginBatch())
{
    batch.Push(file1);
    batch.Push(file2);
    // Oh no, mistake!
    batch.Rollback(); // Rollback
}
// Or just don't commit:
using (var batch = source.BeginBatch())
{
    batch.Push(file1);
    batch.Push(file2);
    // Don't call Commit() - changes are discarded
}
```

### Scenario 7: Single Value Properties

**Before:**
```csharp
context.SetOptions(newOptions);
context.SaveChanges();
```

**After:**
```csharp
var optionsSource = context.OptionsSource;
optionsSource.Set(newOptions); // Automatic!
```

---

## Generated Code Comparison

### Before: Generated Mutation Methods

```csharp
// Generated for: public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }

private List<SourceFile> _currentSourceFiles = new();
private List<SourceFilesChange> _sourceFilesChanges = new();

public void AddSourceFiles(SourceFile item)
{
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(item);

    Lock.EnterWriteLock();
    try
    {
        if (_currentSourceFiles.Contains(item))
            throw ContextOperationException.DuplicateItem("SourceFiles", item);

        _sourceFilesChanges.Add(new SourceFilesChange.Add(item, DateTimeOffset.UtcNow));
    }
    finally
    {
        Lock.ExitWriteLock();
    }
}

public void RemoveSourceFiles(SourceFile item) { /* ... */ }
public void ReplaceSourceFiles(SourceFile oldItem, SourceFile newItem) { /* ... */ }
public void AddSourceFilesBatch(IEnumerable<SourceFile> items) { /* ... */ }
public void RemoveSourceFilesBatch(IEnumerable<SourceFile> items) { /* ... */ }

public void SaveChanges()
{
    // Apply all changes, invalidate cache, trigger outputs
}

public void DiscardChanges()
{
    // Clear all change lists
}
```

### After: Source-Backed Property

```csharp
// Generated for: public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }

private readonly IncrementalSource<SourceFile> _sourceFilesSource;

public IncrementalValuesProvider<SourceFile> SourceFiles => _sourceFilesSource.AsProvider();

public IIncrementalSource<SourceFile> SourceFilesSource => _sourceFilesSource;

// Constructor:
public CompilerContext() : base()
{
    _sourceFilesSource = new IncrementalSource<SourceFile>(
        "CompilerContext.SourceFiles",
        Graph
    );

    // Subscribe to source changes to trigger output handlers
    _sourceFilesSource.Changed += (s, e) => TriggerOutputs();
}

// That's it! No mutation methods needed.
```

---

## Breaking Changes in 2.0.0

When migrating to the push-based architecture, the following methods will be removed:

### Removed Methods

- ❌ `Add{Property}(T item)`
- ❌ `Remove{Property}(T item)`
- ❌ `Replace{Property}(T oldItem, T newItem)`
- ❌ `Add{Property}Batch(IEnumerable<T> items)` / `Add{Property}s(...)`
- ❌ `Remove{Property}Batch(IEnumerable<T> items)` / `Remove{Property}s(...)`
- ❌ `Set{Property}(T value)` - for single-value properties
- ❌ `SaveChanges()`
- ❌ `SaveChangesAsync(CancellationToken ct)`
- ❌ `DiscardChanges()`
- ❌ `HasPendingChanges` property

### New API

- ✅ `IIncrementalSource<T> {Property}Source` - accessor property (generated)
- ✅ `IIncrementalValueSource<T> {Property}Source` - for single values (generated)
- ✅ `source.Push(T item)` - push data
- ✅ `source.Remove(T item)` - remove data
- ✅ `source.Replace(T oldItem, T newItem)` - replace data
- ✅ `source.Clear()` - clear all data
- ✅ `source.BeginBatch()` - batch operations
- ✅ `source.GetSnapshot()` - read current state

---

## Migration Strategy

### Phase 1: Dual API (Non-Breaking) - Version 1.x

Keep both mutation methods and sources for backwards compatibility:

```csharp
// Generated code supports both APIs

// Old API still works:
context.AddSourceFiles(file);
context.SaveChanges();

// New API also works:
context.SourceFilesSource.Push(file); // No SaveChanges needed

// Behind the scenes, mutation methods delegate to sources:
public void AddSourceFiles(SourceFile item)
{
    _sourceFilesSource.Push(item);
}

public void SaveChanges()
{
    // No-op now, changes already applied
    // Keep for backwards compatibility
    TriggerOutputs(); // Just trigger handlers
}
```

**Benefits:**
- No breaking changes
- Users can migrate gradually
- Both APIs work side-by-side

**Migration Period:** 6-12 months

### Phase 2: Deprecation Warnings - Version 1.x

Mark old mutation methods as obsolete:

```csharp
[Obsolete("Use SourceFilesSource.Push() instead. Mutation methods will be removed in 2.0.0")]
public void AddSourceFiles(SourceFile item)
{
    _sourceFilesSource.Push(item);
}

[Obsolete("SaveChanges is no longer needed with push-based sources. Will be removed in 2.0.0")]
public void SaveChanges()
{
    TriggerOutputs();
}
```

**Benefits:**
- Compiler warnings guide users
- Clear migration path
- Time to update code

**Deprecation Period:** 6 months minimum

### Phase 3: Breaking Change - Version 2.0.0

Remove all mutation methods:

```csharp
// Only source-backed properties remain:
public IIncrementalSource<SourceFile> SourceFilesSource { get; }
public IncrementalValuesProvider<SourceFile> SourceFiles => _sourceFilesSource.AsProvider();

// All mutation methods removed
```

**Benefits:**
- Clean API surface
- No maintenance burden
- Framework simplicity

---

## Testing Your Migration

### Unit Test Migration

**Before:**
```csharp
[Fact]
public void Compilation_WithNewFile_ProducesOutput()
{
    var context = new CompilerContext();
    var file = new SourceFile("test.cs", "content");

    context.AddSourceFiles(file);
    context.SaveChanges(); // Must remember!

    var result = context.SourceFiles.Values;
    Assert.Contains(file, result);
}
```

**After:**
```csharp
[Fact]
public void Compilation_WithNewFile_ProducesOutput()
{
    var context = new CompilerContext();
    var file = new SourceFile("test.cs", "content");

    context.SourceFilesSource.Push(file); // Automatic!

    var result = context.SourceFiles.Values;
    Assert.Contains(file, result);
}
```

### Integration Test Migration

**Before:**
```csharp
[Fact]
public async Task FileWatcher_OnChange_TriggersRecompilation()
{
    var context = new CompilerContext();
    var recompiled = false;

    context.RegisterOutput(context.SourceFiles, (files, ctx) =>
    {
        recompiled = true;
    });

    // Simulate file change
    var file = new SourceFile("test.cs", "content");
    context.AddSourceFiles(file);
    context.SaveChanges(); // Explicit trigger

    Assert.True(recompiled);
}
```

**After:**
```csharp
[Fact]
public async Task FileWatcher_OnChange_TriggersRecompilation()
{
    var context = new CompilerContext();
    var recompiled = false;

    context.RegisterOutput(context.SourceFiles, (files, ctx) =>
    {
        recompiled = true;
    });

    // Simulate file change
    var file = new SourceFile("test.cs", "content");
    context.SourceFilesSource.Push(file); // Automatic trigger!

    Assert.True(recompiled);
}
```

---

## Troubleshooting

### "I forgot to call SaveChanges and my tests pass"

✅ **This is expected!** With push-based sources, SaveChanges is no longer needed. Changes apply immediately.

### "How do I batch changes for performance?"

Use `BeginBatch()`:

```csharp
using (var batch = source.BeginBatch())
{
    foreach (var item in items)
    {
        batch.Push(item);
    }
    batch.Commit(); // Single invalidation
}
```

### "Can I still rollback changes?"

Yes, use batch operations and don't call `Commit()`:

```csharp
using (var batch = source.BeginBatch())
{
    batch.Push(item1);
    batch.Push(item2);
    batch.Rollback(); // Discard all changes
}
```

### "Output handlers are firing too often"

This might happen if you're pushing duplicate items. The framework uses equality comparison to detect actual changes:

```csharp
// Implement proper equality for your types:
public sealed record SourceFile(string Path, string Content)
{
    public bool Equals(SourceFile? other)
    {
        if (other is null) return false;
        return Path == other.Path && Content == other.Content;
    }
}
```

Or use batch operations to reduce invalidations.

---

## Getting Help

- **Documentation**: See `PUSH_BASED_ARCHITECTURE_RESEARCH.md` for design details
- **Examples**: Check `/examples/FileWatcherIntegration.cs` for complete examples
- **Issues**: Report problems at https://github.com/yourusername/guacamole/issues
- **Questions**: Ask in GitHub Discussions

---

## Summary

The push-based architecture provides a cleaner, more reactive API:

| Aspect | Before (Mutations) | After (Push-Based) |
|--------|-------------------|-------------------|
| **API Complexity** | High (10+ methods/property) | Low (1 source accessor) |
| **Transaction Management** | Manual (SaveChanges) | Automatic |
| **Reactive** | No (explicit commit) | Yes (immediate) |
| **Composability** | Limited | High (sources are first-class) |
| **Roslyn Similarity** | Low | High |
| **Error Prone** | Yes (forget SaveChanges) | No (automatic) |

**Migration is straightforward** - replace mutation method calls with source operations, and remove SaveChanges calls. That's it!
