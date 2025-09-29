# Data Model: Query API Framework

## Core Entities

### Query
**Purpose**: Represents a computational unit that produces results from inputs
**Attributes**:
- `Name`: string - Unique identifier for the query (derived from class name)
- `InputType`: Type - Type of input parameter (inferred from ExecuteAsync method signature, nullable for parameterless queries)
- `OutputType`: Type - Type of result produced (inferred from ExecuteAsync return type)
- `Dependencies`: List<Type> - Other query types this query depends on (auto-discovered by source generator)

**Implementation Pattern**:
```csharp
[Query]
public class CalculateLengthQuery
{
    public async Task<Result<int>> ExecuteAsync(
        QueryContext context,
        string input,
        CancellationToken cancellationToken = default)
    {
        return Result.Success(input?.Length ?? 0);
    }
}
```

**Validation Rules**:
- Must be decorated with [Query] attribute for discovery
- Must have exactly one public ExecuteAsync method
- Method signature: `Task<Result<TOutput>> ExecuteAsync(QueryContext context, [TInput input,] CancellationToken cancellationToken = default)`
- Input parameter optional for parameterless queries
- InputType and OutputType inferred from method signature
- Dependencies auto-discovered by analyzing QueryContext usage in method body

**State Transitions**:
- Registered → Ready → Executing → Completed/Failed
- Cache invalidation triggers return to Ready state

### QueryContext
**Purpose**: Runtime environment providing access to other queries and framework services
**Attributes**:
- `QueryRegistry`: IQueryRegistry - Registry of available queries
- `CacheManager`: ICacheManager - Query result caching
- `DependencyTracker`: IDependencyTracker - Tracks query dependencies
- `ServiceProvider`: IServiceProvider - DI container for query dependencies

**Validation Rules**:
- Must be scoped per logical operation
- Thread-safe for concurrent query access
- Must track all query invocations for dependency analysis

### QueryResult<T>
**Purpose**: Wrapper for query execution results with success/failure states
**Attributes**:
- `IsSuccess`: bool - Indicates successful execution
- `Value`: T - Result value (only valid when IsSuccess = true)
- `Error`: QueryError - Error information (only valid when IsSuccess = false)
- `ExecutionTime`: TimeSpan - Time taken to execute query
- `WasCached`: bool - Whether result came from cache

**Validation Rules**:
- Either Value or Error must be set, never both
- ExecutionTime must be non-negative
- Immutable once created

### QueryError
**Purpose**: Represents errors that occur during query execution
**Attributes**:
- `Code`: string - Error code for categorization
- `Message`: string - Human-readable error description
- `InnerError`: QueryError - Nested error from dependencies
- `QueryName`: string - Name of query that failed

**Validation Rules**:
- Code must follow standard error code format
- Message must be non-empty
- QueryName must reference valid registered query

### QueryInput<T>
**Purpose**: Typed input parameter for query execution
**Attributes**:
- `Value`: T - The input value
- `Hash`: int - Stable hash for caching (computed from Value)

**Validation Rules**:
- Value must be immutable or deeply cloned
- Hash must be deterministic and stable across runs
- Implements IEquatable<QueryInput<T>> for cache key comparison

## Relationships

```
QueryContext 1 -----> * Query (contains registry of)
Query 1 -----> * Query (depends on via Dependencies)
QueryContext 1 -----> 1 CacheManager
QueryContext 1 -----> 1 DependencyTracker
Query 1 -----> 0..1 QueryInput<T> (receives as parameter)
Query 1 -----> 1 QueryResult<T> (produces as result)
QueryResult<T> 1 -----> 0..1 QueryError (contains on failure)
```

## Cache Keys

**Composite Key Structure**:
```
CacheKey = Hash(QueryType, InputHash, DependencyVersions)
```

**Components**:
- `QueryType`: Stable type identifier
- `InputHash`: Hash of input parameters (0 for parameterless)
- `DependencyVersions`: Hash of all dependency cache versions

**Invalidation Rules**:
- Direct invalidation when input changes
- Transitive invalidation when dependencies change
- Version-based invalidation for efficient propagation

## Performance Constraints

### Memory Management
- Query results: Maximum 100MB per result
- Cache size: LRU eviction with 1GB default limit
- Dependency graph: Maximum 1000 nodes per context

### Execution Timing
- Cached queries: <1ms response time
- Incremental recomputation: <10ms for single change
- Full recomputation: <100ms for small datasets

### Concurrency
- QueryContext: Thread-safe read operations
- Query execution: Parallel execution of independent queries
- Cache access: Lock-free reads, synchronized writes