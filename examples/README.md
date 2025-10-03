# Guacamole Examples

This directory contains example implementations demonstrating the Guacamole incremental computation framework.

## Property Convention (v0.1.0+)

**Important**: Starting with v0.1.0, the property generation convention has changed:

- **Get-only properties** (`{ get; }`) → Auto-managed (mutation methods generated)
- **Properties with setters** (`{ get; set; }`) → User-managed (no auto-generation)

All examples in this directory use the **new convention**.

## Examples

### 1. CompilerContext

**Location**: `examples/CompilerContext/`

**Purpose**: Demonstrates a simple compiler context with incremental source file processing.

**Key Features**:
- Single-value property (`IncrementalValueProvider<Project>`)
- Collection property (`IncrementalValuesProvider<SourceFile>`)
- Auto-generated mutation methods: `SetProject()`, `AddSourceFiles()`, `ReplaceSourceFiles()`
- Output handler registration and triggering
- Change tracking with `HasPendingChanges`

**Run**:
```bash
dotnet run --project examples/CompilerContext
```

**Expected Output**:
- Demonstrates setting a project
- Adding source files
- Tracking pending changes
- Registering output handlers
- File replacement with incremental updates

### 2. FileWatcherContext

**Location**: `examples/FileWatcherContext/`

**Purpose**: Demonstrates file system watching with incremental computation.

**Key Features**:
- Collection property for tracking files (`IncrementalValuesProvider<TrackedFile>`)
- Auto-generated mutation methods: `AddFiles()`, `RemoveFiles()`, `ReplaceFiles()`
- File modification simulation
- Output handlers for change notifications
- Demonstrates record-based equality comparison

**Run**:
```bash
dotnet run --project examples/FileWatcherContext
```

**Expected Output**:
- Adding tracked files to the context
- Output handler triggering on changes
- File modification via Replace operation
- File removal
- Change tracking and notifications

## What Gets Auto-Generated?

For **get-only properties**, the source generator creates:

### Single-Value Properties (`IncrementalValueProvider<T>`)
- `Set{PropertyName}(T value)` - Sets the value

### Collection Properties (`IncrementalValuesProvider<T>`)
- `Add{PropertyName}(T item)` - Adds a single item (throws on duplicate)
- `Remove{PropertyName}(T item)` - Removes an item (returns false if not found)
- `Replace{PropertyName}(T oldItem, T newItem)` - Replaces an item
- `Add{PropertyName}s(IEnumerable<T> items)` - Batch add
- `Remove{PropertyName}s(IEnumerable<T> items)` - Batch remove

### Change Tracking Infrastructure
- `SaveChanges()` / `SaveChangesAsync()` - Commits pending changes
- `DiscardChanges()` - Discards pending changes
- `HasPendingChanges` - Boolean property indicating pending changes
- Strongly-typed change record types (zero boxing)
- Computation nodes for graph integration
- Output handler support

## Building Examples

Build all examples:
```bash
dotnet build examples/CompilerContext
dotnet build examples/FileWatcherContext
```

Or build from the solution root:
```bash
dotnet build
```

## Migration from v0.0.x

If you're migrating from an earlier version:

1. **Change property declarations**:
   ```csharp
   // OLD (v0.0.x)
   public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; set; }

   // NEW (v0.1.0+)
   public partial IncrementalValuesProvider<SourceFile> SourceFiles { get; }
   ```

2. **All mutation methods remain the same** - no code changes needed

See [Migration Guide](../docs/migration/010-property-convention-reversal.md) for detailed instructions.

## Additional Resources

- [Generated Code Reference](../docs/generated-code-reference.md) - Detailed documentation of generated code
- [Performance Guide](../docs/performance-guide.md) - Performance characteristics and optimization tips
- [Migration Guide](../docs/migration/010-property-convention-reversal.md) - Migration instructions for v0.1.0
