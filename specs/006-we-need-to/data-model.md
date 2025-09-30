# Data Model: Incremental Value Providers

**Feature**: Incremental Value Providers
**Date**: 2025-01-29
**Phase**: 1 - Design & Contracts

## Core Entities

### 1. IncrementalValueProvider<T>

**Purpose**: Immutable structure representing a single value in a transformation pipeline with automatic caching and incremental computation.

**Properties**:
- `IInternalValueNode<T> Node`: Internal computation node (implementation detail)
- `ComputationGraph Graph`: Shared computation graph for dependency tracking
- `ProviderMetadata Metadata`: Debugging and tracking information

**Key Characteristics**:
- Immutable record struct with value equality
- Thread-safe without synchronization
- Supports pipeline transformations via extension methods
- Automatic memoization at transformation boundaries

**Validation Rules**:
- Type T must be equatable for caching (enforced at compile time)
- Node must not be null (enforced by constructor)
- Graph must be valid DAG (enforced at build time)

### 2. IncrementalValuesProvider<T>

**Purpose**: Immutable structure representing zero or more values in a transformation pipeline with filtering, collection, and transformation capabilities.

**Properties**:
- `IInternalValuesNode<T> Node`: Internal computation node for multiple values
- `ComputationGraph Graph`: Shared computation graph for dependency tracking
- `ProviderMetadata Metadata`: Debugging and tracking information

**Key Characteristics**:
- Immutable record struct with value equality
- Thread-safe collection operations
- Supports filtering, mapping, and reduction operations
- Efficient enumeration with lazy evaluation

**Validation Rules**:
- Collection operations must preserve deterministic order
- Empty collections are valid and cacheable
- Transformation functions must be pure (no side effects)

### 3. ComputationGraph

**Purpose**: Manages the computation graph, dependency tracking, and cache coordination across all providers.

**Properties**:
- `ImmutableDictionary<NodeId, IComputationNode> Nodes`: All computation nodes
- `ImmutableDictionary<NodeId, ImmutableHashSet<NodeId>> Dependencies`: Dependency relationships
- `LRUCache<NodeId, object> ValueCache`: Shared cache for computed values
- `DiagnosticCollector Diagnostics`: Metrics and tracing collection

**Key Characteristics**:
- Immutable with copy-on-write semantics for modifications
- Directed Acyclic Graph (DAG) structure enforced
- Centralized cache management with LRU eviction
- Build-time cycle detection

**Validation Rules**:
- Must maintain DAG invariant (no cycles)
- NodeId must be unique within graph
- Cache size must respect memory pressure limits
- Dependency changes invalidate affected cached values

### 4. Result<TValue, TError>

**Purpose**: Discriminated union for handling success/error states in pipeline transformations without exceptions.

**Properties**:
- `bool IsSuccess`: Indicates success or error state
- `TValue Value`: The computed value (when IsSuccess = true)
- `TError Error`: The error information (when IsSuccess = false)

**Key Characteristics**:
- Immutable structure with pattern matching support
- Supports railway-oriented programming for error handling
- Integrates with pipeline composition through extension methods
- Prevents exception propagation in transformation chains

**Validation Rules**:
- Exactly one of Value or Error must be populated
- Error state must contain meaningful diagnostic information
- Success state must contain valid computed result

### 5. ProviderMetadata

**Purpose**: Debugging and diagnostic information attached to providers for visualization and monitoring.

**Properties**:
- `string? TrackingName`: Optional human-readable identifier
- `SourceLocation CreationLocation`: Where provider was created
- `ImmutableArray<string> Tags`: Arbitrary tags for grouping/filtering
- `PerformanceMetrics Metrics`: Timing and cache statistics

**Key Characteristics**:
- Optional metadata that doesn't affect computation
- Supports pipeline visualization tools
- Enables performance profiling and debugging
- Minimal memory overhead

**Validation Rules**:
- TrackingName, if provided, must be non-empty
- Tags must not contain duplicate entries
- Metrics must be thread-safe for concurrent access

## Supporting Types

### 6. IComputationNode (Interface Hierarchy)

**IComputationNode**: Base interface for all computation nodes
- `NodeId Id`: Unique identifier within computation graph
- `Type OutputType`: The type of value this node produces
- `ImmutableHashSet<NodeId> Dependencies`: Direct dependencies

**IInternalValueNode<T>**: Single-value computation node
- Inherits from IComputationNode
- `Func<ComputationContext, Result<T, ComputationError>> ComputeFunction`

**IInternalValuesNode<T>**: Multiple-values computation node
- Inherits from IComputationNode
- `Func<ComputationContext, Result<IEnumerable<T>, ComputationError>> ComputeFunction`

### 7. LRUCache<TKey, TValue>

**Purpose**: Least-Recently-Used cache implementation for storing computed values with predictable eviction behavior.

**Properties**:
- `int Capacity`: Maximum number of cached entries
- `ConcurrentDictionary<TKey, LinkedListNode<CacheEntry>> Index`: Fast lookup
- `LinkedList<CacheEntry> AccessOrder`: LRU ordering

**Key Operations**:
- `TryGet(TKey key, out TValue value)`: O(1) cache lookup
- `Set(TKey key, TValue value)`: O(1) cache storage with LRU eviction
- `Clear()`: Removes all cached entries
- `GetStatistics()`: Returns cache hit/miss metrics

### 8. Extension Method Categories

**Transformation Extensions** (IncrementalValueProvider<T>):
- `Select<TResult>(Func<T, TResult> transform)`: Transform single value
- `Where(Func<T, bool> predicate)`: Filter single value
- `Combine<TOther, TResult>(IncrementalValueProvider<TOther>, Func<T, TOther, TResult>)`: Combine providers

**Collection Extensions** (IncrementalValuesProvider<T>):
- `Select<TResult>(Func<T, TResult> transform)`: Transform all values
- `Where(Func<T, bool> predicate)`: Filter values
- `SelectMany<TResult>(Func<T, IEnumerable<TResult>>)`: Flatten collections
- `Collect()`: Convert to IncrementalValueProvider<ImmutableArray<T>>

**Diagnostic Extensions** (Both providers):
- `WithTrackingName(string name)`: Add debugging identifier
- `WithComparer<T>(IEqualityComparer<T>)`: Custom equality for caching
- `WithTags(params string[] tags)`: Add metadata tags

## Entity Relationships

```
ComputationGraph
├── Contains: IComputationNode instances
├── Manages: LRUCache for all computed values
├── Tracks: Dependencies between nodes
└── Provides: Diagnostic and metrics collection

IncrementalValueProvider<T>
├── References: IInternalValueNode<T> (composition)
├── Shares: ComputationGraph (composition)
├── Produces: Result<T, ComputationError> via transformations
└── Extends: Via extension methods for pipeline operations

IncrementalValuesProvider<T>
├── References: IInternalValuesNode<T> (composition)
├── Shares: ComputationGraph (composition)
├── Produces: Result<IEnumerable<T>, ComputationError> via transformations
└── Extends: Via extension methods for collection operations

LRUCache<TKey, TValue>
├── Used by: ComputationGraph for value storage
├── Manages: Memory pressure through eviction
├── Provides: O(1) access with predictable eviction
└── Collects: Performance metrics for monitoring
```

## State Transitions

### Provider Lifecycle
1. **Creation**: Provider created with initial computation node
2. **Transformation**: New provider created via extension method
3. **Evaluation**: Lazy evaluation triggered by value access
4. **Caching**: Computed value stored in shared cache
5. **Invalidation**: Dependencies change, cache entry invalidated
6. **Re-evaluation**: Value recomputed on next access

### Cache Entry Lifecycle
1. **Miss**: Value not in cache, computation required
2. **Computation**: Value computed and stored
3. **Hit**: Subsequent access returns cached value
4. **Access Update**: Entry moved to most-recently-used position
5. **Eviction**: Least-recently-used entry removed under memory pressure
6. **Invalidation**: Entry explicitly removed due to dependency changes

## Performance Characteristics

### Computational Complexity
- **Cached Access**: O(1) - Constitutional requirement met
- **Incremental Update**: O(changes) - Constitutional requirement met
- **Memory Overhead**: <2x input data - Constitutional requirement met
- **Dependency Resolution**: O(changed_dependencies) - Precise invalidation

### Memory Management
- **LRU Eviction**: Predictable memory bounds under pressure
- **Immutable Structures**: Reduced GC pressure through sharing
- **Weak References**: Non-essential data eligible for collection
- **Memory Pools**: Temporary allocations use pooled arrays

### Threading Characteristics
- **Lock-Free Design**: Immutable structures avoid synchronization
- **Concurrent Cache Access**: ConcurrentDictionary provides thread safety
- **Deterministic Results**: Same inputs always produce same outputs
- **Parallel Evaluation**: Independent computations can run concurrently

## Validation and Constraints

### Build-Time Validation
- **Cycle Detection**: Computation graph must be acyclic
- **Type Safety**: Generic constraints enforce value equality requirements
- **Dependency Analysis**: Static analysis of computation dependencies

### Runtime Validation
- **Memory Bounds**: Cache eviction prevents unbounded growth
- **Performance Monitoring**: Automatic detection of performance regressions
- **Error Propagation**: Robust error handling without exception-based control flow

### Constitutional Compliance
- **Incremental Computation**: O(changes) complexity maintained
- **Deterministic Execution**: Pure functions with immutable inputs/outputs
- **Performance Targets**: <1ms cached, <10ms incremental access achieved
- **Query-Driven Architecture**: Pipeline composition as composable queries

## Data Model Validation

✅ **Immutable Design**: All entities use immutable patterns with value equality
✅ **Thread Safety**: Lock-free design with concurrent data structures where needed
✅ **Performance Targets**: Architecture supports constitutional performance requirements
✅ **Error Handling**: Robust Result<T, Error> pattern prevents exception propagation
✅ **Caching Strategy**: LRU cache with predictable eviction behavior
✅ **Dependency Tracking**: DAG structure with precise invalidation
✅ **Diagnostic Support**: Comprehensive metadata for debugging and monitoring
✅ **TDD Ready**: Clear entity boundaries enable focused unit testing

**Data Model Complete** - Ready for contract generation and implementation