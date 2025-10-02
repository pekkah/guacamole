# Data Model: ComputingContextBase

## Core Entities

### ComputingContextBase
**Purpose**: Minimal abstract base class for user-defined incremental computation contexts
**Location**: `src/Guacamole/ComputingContextBase.cs`

**Fields** (minimal runtime state):
- `Graph: ComputationGraph` - Shared computation graph for dependency management
- `_lock: ReaderWriterLockSlim` - Thread synchronization for multiple readers/single writer
- `IsDisposed: bool` - Disposal state tracking

**Generated Fields** (per user context):
- `_propertyNameChanges: List<PropertyNameChange>` - Property-specific change lists (no boxing)
- `_propertyNameNode: PropertyNameNode` - Property-specific computation nodes (strongly typed)
- `_currentPropertyName: ActualType` - Current state accessors for duplicate checking

**Relationships**:
- Source generator extends with property-specific infrastructure
- Integrates with `ComputationGraph` from Guacamole.Core through generated nodes
- Contains generated `IncrementalValueProvider<T>` property implementations

**Lifecycle**:
1. **Construction**: Initialize computation graph, thread lock, generated property nodes
2. **Property Registration**: Source generator wires properties to strongly-typed computation nodes
3. **Mutation Phase**: Generated Add/Remove/Replace methods track changes in property-specific lists
4. **Commit Phase**: Generated SaveChanges() applies tracked changes with optimized loops and triggers recomputation
5. **Disposal**: Release locks, clear generated change lists

**Validation Rules**:
- Properties with setters must be input sources (generate mutation methods)
- Properties without setters must be derived computations (read-only)
- Duplicate items in Add operations must throw exceptions
- Remove operations on non-existent items must return false

### Generated Change Tracking (No Interface)
**Purpose**: Property-specific change tracking with zero boxing overhead
**Location**: Generated in user context partial class

**Generated Fields Per Property**:
- `_propertyNameChanges: List<PropertyNameChange>` - Strongly-typed change list
- `HasPendingPropertyNameChanges: bool` - Optimized change detection

**Generated Methods Per Property**:
- `ClearPropertyNameChanges()` - Reset specific property tracking
- `ApplyPropertyNameChanges(CancellationToken)` - Apply specific property changes

**Performance Characteristics**:
- Zero boxing: All change records are strongly typed
- Zero virtual calls: Direct list operations, no interface overhead
- Memory efficient: Separate lists avoid object[] arrays
- JIT optimized: Concrete types enable better code generation

**State Transitions**:
```
Empty → Generated Tracking → Generated Validation → Applied → Empty
  ↑                                                    ↓
  └──────────────── Error/Rollback ────────────────────┘
```

### Generated Change Records (No Interface)
**Purpose**: Strongly-typed change records with zero boxing
**Location**: Generated per property in user context

**Generated Per Property Type**:
```csharp
// For Project property (IncrementalValueProvider<Project>)
public abstract record ProjectChange(DateTimeOffset Timestamp)
{
    public sealed record Set(Project NewValue, DateTimeOffset Timestamp) : ProjectChange(Timestamp);
}

// For SourceFiles property (IncrementalValuesProvider<SourceFile>)
public abstract record SourceFileChange(SourceFile File, DateTimeOffset Timestamp)
{
    public sealed record Add(SourceFile File, DateTimeOffset Timestamp) : SourceFileChange(File, Timestamp);
    public sealed record Remove(SourceFile File, DateTimeOffset Timestamp) : SourceFileChange(File, Timestamp);
    public sealed record Replace(SourceFile OldFile, SourceFile NewFile, DateTimeOffset Timestamp)
        : SourceFileChange(NewFile, Timestamp)
    {
        public SourceFile OldFile { get; } = OldFile;
    }
}
```

**Performance Benefits**:
- Zero boxing: All fields strongly typed to actual property types
- Pattern matching optimized: Compiler generates optimal switch expressions
- Memory layout: Records stored directly in typed lists
- Cache efficiency: Related changes stored contiguously

### Generated Context Properties
**Pattern**: User-defined properties in inherited context classes

**Input Source Properties** (with setters):
```csharp
public IncrementalValueProvider<Project> Project { get; set; }
public IncrementalValuesProvider<SourceFile> SourceFiles { get; set; }
```
- Generate mutation methods: `SetProject()`, `AddSourceFile()`, `RemoveSourceFile()`, `ReplaceSourceFile()`
- Connected to computation graph as input nodes
- Trigger dependency invalidation on changes

**Derived Computation Properties** (get-only):
```csharp
public IncrementalValuesProvider<SyntaxTree> ParsedTrees { get; }
public IncrementalValuesProvider<TypeDeclaration> Types { get; }
```
- No mutation methods generated
- Computed from input sources through transformations
- Automatically recomputed when dependencies change

## Computation Graph Integration

### Context as Computation Node
**Interface**: `IComputingContextNode : IComputationNode`
**Purpose**: Integrate context into existing computation graph infrastructure

**Integration Points**:
- Context becomes a node in the computation graph
- Properties become child nodes with dependency relationships
- Change tracking triggers graph invalidation
- Commit operations update graph state

### Property Node Mapping
**Pattern**: Each property maps to computation node in graph

**Input Source Nodes**:
- Mutable state managed by context
- Changes trigger invalidation of dependent nodes
- Cache keys include property name + item identity

**Derived Computation Nodes**:
- Read-only transformations of input sources
- Automatic recomputation when inputs change
- Leverage existing pipeline extension methods

## Threading and Concurrency

### Lock Coordination
**Pattern**: Multiple readers, single writer with ReaderWriterLockSlim

**Read Operations** (concurrent):
- Property access through IncrementalValueProvider
- Query operations on derived computations
- Cache lookups and dependency traversal

**Write Operations** (exclusive):
- Add/Remove/Replace mutation methods
- SaveChanges() commit operations
- Change tracker state modifications

### Deadlock Prevention
**Strategy**: Consistent lock ordering and timeout handling
- Context lock acquired before computation graph locks
- Timeouts on all lock acquisitions
- Cancellation token support for long operations

## Error Handling

### Exception Types
**ContextOperationException**: Domain-specific errors
- `OperationType: string` - Add/Remove/Replace/Commit
- `PropertyName: string` - Target property
- `FailedItem: object?` - Item that caused failure
- `OperationIndex: int` - Position in batch operation

**Integration**: Wraps into ComputationError from Guacamole.Core

### Failure Scenarios
1. **Duplicate Add**: Throw exception immediately (fail-fast)
2. **Remove Non-existent**: Return false, continue processing
3. **Batch Failure**: Stop at first error, preserve partial state, detailed diagnostics
4. **Thread Contention**: Timeout with clear error message
5. **Graph Cycle**: Detected at commit time, rollback changes

## Memory Management

### Lifecycle Management
- Context implements `IDisposable`
- Change tracker cleared after successful commit
- Weak references to prevent memory leaks
- Integration with existing LRU cache eviction

### Performance Characteristics
- **Memory Overhead**: <2x baseline (tracked in metrics)
- **Cache Access**: <1ms for cached values
- **Incremental Updates**: <10ms for change propagation
- **Batch Operations**: O(changes) complexity, not O(total)