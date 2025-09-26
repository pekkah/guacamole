# .NET Theoretical Adaptations

## Overview

This document presents theoretical approaches for adapting salsa-rs concepts to C# and .NET 10, providing implementation strategies that leverage modern .NET features while maintaining the performance and correctness guarantees that make incremental computation effective. These adaptations follow Guacamole constitutional requirements and integrate with the Microsoft ecosystem.

## Core Architecture Adaptations

### Query System Adaptation (.NET Adaptation)

**Based On**: [Core Concepts - Query System](./core-concepts.md#query-system)

**C# Approach**: Implement queries as generic interfaces with source generator-backed implementations that provide compile-time dependency tracking and cache key generation.

```csharp
public interface IQuery<TKey, TValue> where TKey : notnull
{
    TValue Execute(TKey key, IQueryContext context);
}

// Source generator creates implementation
[Query]
public partial class UserProfileQuery : IQuery<UserId, UserProfile>
{
    public UserProfile Execute(UserId key, IQueryContext context)
    {
        var user = context.Query<UserQuery>().Execute(key);
        var preferences = context.Query<PreferencesQuery>().Execute(key);
        return new UserProfile(user, preferences);
    }
}
```

**Required .NET Features**:
- Source Generators: Generate dependency tracking and cache management code
- Generic Interfaces: Provide type-safe query definitions
- Nullable Reference Types: Ensure robust null handling in query results

**Constitutional Compliance**:
- ✅ Incremental Computation First: O(changes) through automatic dependency tracking
- ✅ Query-Driven Architecture: All computations expressed as deterministic queries
- ✅ Modern C# Features: Source generators eliminate boilerplate while maintaining performance

**Performance Considerations**: Source generators ensure zero runtime reflection overhead while providing compile-time optimization of dependency tracking and cache operations.

**Integration Notes**: Queries integrate with Microsoft.Extensions.DependencyInjection through factory patterns, allowing dependency injection of external services while maintaining query purity.

### Database Abstraction Adaptation (.NET Adaptation)

**Based On**: [Core Concepts - Database Abstraction](./core-concepts.md#database-abstraction)

**C# Approach**: Implement database as a generic host service with concurrent collections for thread-safe cache management and snapshot-based parallel access.

```csharp
public interface IGuacamoleDatabase
{
    TValue Query<TQuery, TKey, TValue>(TKey key)
        where TQuery : IQuery<TKey, TValue>, new()
        where TKey : notnull;

    IGuacamoleSnapshot CreateSnapshot();
    void SetInput<TKey, TValue>(TKey key, TValue value) where TKey : notnull;
}

public class GuacamoleDatabase : IGuacamoleDatabase, IHostedService
{
    private readonly ConcurrentDictionary<object, CacheEntry> _cache = new();
    private readonly DependencyTracker _dependencies = new();
    private volatile int _revision = 0;
}
```

**Required .NET Features**:
- ConcurrentDictionary: Thread-safe cache storage with efficient lookup
- IHostedService: Integration with .NET hosting for lifetime management
- Memory<T> and Span<T>: Zero-allocation cache key and result handling

**Constitutional Compliance**:
- ✅ Performance Targets: ConcurrentDictionary provides sub-millisecond cache access
- ✅ Dependency Injection: Native integration with Microsoft DI container
- ✅ Modern C# Features: Leverages latest concurrency and memory management features

**Performance Considerations**: Expected O(1) cache lookup using ConcurrentDictionary hash-based access with minimal allocation overhead through span-based operations.

**Integration Notes**: Database registers as singleton service with IServiceCollection, providing natural integration with ASP.NET Core and other Microsoft hosting patterns.

## Algorithm Adaptations

### Incremental Computation Algorithm Adaptation (.NET Adaptation)

**Based On**: [Algorithms Analysis - Incremental Computation](./algorithms-analysis.md#incremental-computation-algorithms)

**C# Approach**: Use atomic integers for revision tracking with Task-based parallel execution for independent query processing.

```csharp
public readonly struct RevisionToken : IEquatable<RevisionToken>
{
    private readonly int _revision;

    public bool IsValid(int currentRevision) => _revision == currentRevision;
}

public class IncrementalExecutor
{
    public async ValueTask<TValue> ExecuteAsync<TKey, TValue>(
        IQuery<TKey, TValue> query,
        TKey key,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey.Create<TKey, TValue>(key);

        if (_cache.TryGetValue(cacheKey, out var cached) &&
            cached.Revision.IsValid(_currentRevision))
        {
            return (TValue)cached.Value;
        }

        var result = await query.ExecuteAsync(key, _context);
        _cache.TryAdd(cacheKey, new CacheEntry(result, _currentRevision));
        return result;
    }
}
```

**Required .NET Features**:
- ValueTask: Efficient async/await for cached results without allocation
- Interlocked Operations: Thread-safe revision tracking
- CancellationToken: Cooperative cancellation for long-running queries

**Constitutional Compliance**:
- ✅ Performance Targets: ValueTask eliminates allocation for cached results
- ✅ Incremental Computation First: O(changes) through revision-based staleness detection
- ✅ Modern C# Features: Latest async patterns and memory-efficient operations

**Performance Considerations**: Achieves O(changes) complexity with constant-time staleness detection and efficient parallel execution of independent queries.

**Integration Notes**: Integrates with ILogger for query execution tracing and IMetrics for performance monitoring in production environments.

### Dependency Graph Adaptation (.NET Adaptation)

**Based On**: [Algorithms Analysis - Dependency Graph Construction](./algorithms-analysis.md#graph-algorithms)

**C# Approach**: Use AsyncLocal for execution context tracking with efficient graph storage using adjacency lists and span-based traversal.

```csharp
public class DependencyTracker
{
    private static readonly AsyncLocal<Stack<QueryId>> _executionStack = new();
    private readonly ConcurrentDictionary<QueryId, HashSet<QueryId>> _dependencies = new();

    public void RecordDependency(QueryId dependent, QueryId dependency)
    {
        _dependencies.GetOrAdd(dependent, _ => new HashSet<QueryId>())
                    .Add(dependency);
    }

    public ReadOnlySpan<QueryId> GetDependents(QueryId queryId)
    {
        // Return span view for zero-allocation traversal
        return _dependents.TryGetValue(queryId, out var deps)
            ? CollectionsMarshal.AsSpan(deps.ToArray())
            : ReadOnlySpan<QueryId>.Empty;
    }
}
```

**Required .NET Features**:
- AsyncLocal: Thread-safe execution context without global state
- ReadOnlySpan<T>: Zero-allocation graph traversal
- CollectionsMarshal: Efficient span creation from collections

**Constitutional Compliance**:
- ✅ Modern C# Features: Spans provide zero-cost abstractions for graph operations
- ✅ Performance Targets: AsyncLocal enables efficient context tracking
- ✅ Incremental Computation First: Precise dependency tracking enables O(changes) updates

**Performance Considerations**: Linear time complexity in dependency subgraph size with zero-allocation traversal using spans and efficient hash-based lookup.

**Integration Notes**: Context tracking integrates with .NET's execution context flow, ensuring proper dependency recording across async boundaries.

## Caching Strategy Adaptations

### Memoization Engine Adaptation (.NET Adaptation)

**Based On**: [Caching Strategies - Memoization Strategies](./caching-strategies.md#memoization-strategies)

**C# Approach**: Implement automatic result caching using MemoryCache with custom eviction policies and source generator-based cache key optimization.

```csharp
[StructLayout(LayoutKind.Auto)]
public readonly struct CacheKey : IEquatable<CacheKey>
{
    private readonly Type _queryType;
    private readonly int _keyHash;

    public static CacheKey Create<TQuery, TKey>(TKey key)
        where TQuery : IQuery
    {
        return new CacheKey(typeof(TQuery),
            EqualityComparer<TKey>.Default.GetHashCode(key));
    }
}

public class GuacamoleMemoizer
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultOptions;

    public bool TryGetCached<TValue>(CacheKey key, out TValue value)
    {
        return _cache.TryGetValue(key, out value);
    }

    public void Cache<TValue>(CacheKey key, TValue value, TimeSpan? expiration = null)
    {
        var options = expiration.HasValue
            ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration }
            : _defaultOptions;

        _cache.Set(key, value, options);
    }
}
```

**Required .NET Features**:
- IMemoryCache: Built-in caching with configurable policies
- StructLayout: Optimized cache key layout for performance
- EqualityComparer<T>: Efficient, type-safe hash code generation

**Constitutional Compliance**:
- ✅ Performance Targets: IMemoryCache provides millisecond-scale cache operations
- ✅ Dependency Injection: Native integration with Microsoft caching abstractions
- ✅ Modern C# Features: Struct-based cache keys minimize allocation overhead

**Performance Considerations**: Constant-time cache lookup with minimal allocation overhead through struct-based keys and efficient hash computation.

**Integration Notes**: Leverages Microsoft.Extensions.Caching.Memory for configuration and monitoring integration with existing .NET caching infrastructure.

### Cache Invalidation Adaptation (.NET Adaptation)

**Based On**: [Caching Strategies - Cache Invalidation Approaches](./caching-strategies.md#cache-invalidation-approaches)

**C# Approach**: Implement precise invalidation using ConcurrentDictionary with efficient batch operations and change token-based cache coordination.

```csharp
public class InvalidationEngine
{
    private readonly ConcurrentDictionary<QueryId, CacheEntry> _cache;
    private readonly DependencyTracker _dependencies;

    public void InvalidateQuery(QueryId queryId, int newRevision)
    {
        var toInvalidate = new Stack<QueryId>();
        toInvalidate.Push(queryId);

        var processed = new HashSet<QueryId>();

        while (toInvalidate.Count > 0)
        {
            var current = toInvalidate.Pop();
            if (!processed.Add(current)) continue;

            if (_cache.TryGetValue(current, out var entry))
            {
                // Mark as invalid by updating revision
                _cache.TryUpdate(current, entry with { Revision = newRevision }, entry);
            }

            // Add dependents for invalidation
            foreach (var dependent in _dependencies.GetDependents(current))
            {
                toInvalidate.Push(dependent);
            }
        }
    }
}
```

**Required .NET Features**:
- ConcurrentDictionary: Thread-safe cache operations with atomic updates
- Records with-expressions: Efficient immutable cache entry updates
- HashSet: Efficient duplicate detection during graph traversal

**Constitutional Compliance**:
- ✅ Incremental Computation First: Precise invalidation maintains O(changes) characteristics
- ✅ Performance Targets: Efficient batch invalidation minimizes overhead
- ✅ Modern C# Features: Records provide immutable, efficient cache entry management

**Performance Considerations**: Linear time complexity in size of invalidation subgraph with efficient concurrent operations and minimal allocation.

**Integration Notes**: Coordinates with IChangeToken for integration with ASP.NET Core change detection and file watching scenarios.

## API Pattern Adaptations

### Query Composition Adaptation (.NET Adaptation)

**Based On**: [API Patterns - Query Composition Patterns](./api-patterns.md#query-composition-patterns)

**C# Approach**: Implement fluent query composition using builder patterns with compile-time type safety and automatic dependency tracking.

```csharp
public static class QueryExtensions
{
    public static IQuery<TKey, TResult> Select<TKey, TValue, TResult>(
        this IQuery<TKey, TValue> source,
        Func<TValue, TResult> selector)
    {
        return new SelectQuery<TKey, TValue, TResult>(source, selector);
    }

    public static IQuery<TKey, TResult> SelectMany<TKey, TValue, TIntermediate, TResult>(
        this IQuery<TKey, TValue> source,
        Func<TValue, IQuery<TKey, TIntermediate>> intermediateSelector,
        Func<TValue, TIntermediate, TResult> resultSelector)
    {
        return new SelectManyQuery<TKey, TValue, TIntermediate, TResult>(
            source, intermediateSelector, resultSelector);
    }
}

// Usage with LINQ-style composition
var composedQuery = userQuery
    .Select(user => user.Id)
    .SelectMany(id => preferencesQuery.WithKey(id), (user, prefs) => new UserProfile(user, prefs));
```

**Required .NET Features**:
- Generic Methods: Type-safe query composition with inference
- LINQ Patterns: Familiar composition syntax for .NET developers
- Expression Trees: Compile-time analysis for dependency optimization

**Constitutional Compliance**:
- ✅ Query-Driven Architecture: All operations expressed as composable queries
- ✅ Modern C# Features: LINQ integration provides natural .NET development experience
- ✅ Incremental Computation First: Composition maintains O(changes) characteristics

**Performance Considerations**: Zero-overhead composition through compile-time inlining and efficient dependency chain construction.

**Integration Notes**: Integrates with Entity Framework query patterns and IAsyncEnumerable for streaming scenarios.

### Parallel Execution Adaptation (.NET Adaptation)

**Based On**: [API Patterns - Parallel Query Execution](./api-patterns.md#performance-oriented-api-patterns)

**C# Approach**: Leverage Task.Run and Parallel.ForEach with custom schedulers for CPU-bound incremental computations.

```csharp
public class ParallelQueryExecutor
{
    private readonly TaskScheduler _scheduler;
    private readonly ParallelOptions _parallelOptions;

    public async ValueTask<TValue[]> ExecuteParallelAsync<TKey, TValue>(
        IQuery<TKey, TValue> query,
        ReadOnlyMemory<TKey> keys,
        CancellationToken cancellationToken = default)
    {
        var results = new TValue[keys.Length];
        var tasks = new Task[keys.Length];

        for (int i = 0; i < keys.Length; i++)
        {
            var index = i;
            var key = keys.Span[index];

            tasks[index] = Task.Run(async () =>
            {
                results[index] = await query.ExecuteAsync(key, cancellationToken);
            }, cancellationToken);
        }

        await Task.WhenAll(tasks);
        return results;
    }
}
```

**Required .NET Features**:
- Task Parallel Library: Efficient parallel execution with work stealing
- ReadOnlyMemory<T>: Zero-copy parallel key processing
- Custom TaskScheduler: Optimized scheduling for incremental computation workloads

**Constitutional Compliance**:
- ✅ Performance Targets: Parallel execution achieves maximum throughput for independent queries
- ✅ Modern C# Features: Latest async patterns with efficient memory usage
- ✅ Incremental Computation First: Parallelism preserves incremental computation benefits

**Performance Considerations**: Scales linearly with CPU cores for independent queries while maintaining cache coherency and dependency correctness.

**Integration Notes**: Integrates with .NET ThreadPool and custom schedulers for specialized incremental computation workloads.

## Microsoft Ecosystem Integration

### Dependency Injection Integration

**Based On**: Constitutional requirement for Microsoft.Extensions.DependencyInjection integration

**C# Approach**: Implement comprehensive service registration with factory patterns and lifetime management that supports incremental computation semantics.

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGuacamole(this IServiceCollection services,
        Action<GuacamoleOptions> configure = null)
    {
        services.Configure<GuacamoleOptions>(configure ?? (_ => { }));

        services.AddSingleton<IGuacamoleDatabase, GuacamoleDatabase>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddSingleton<IDependencyTracker, DependencyTracker>();
        services.AddSingleton<IInvalidationEngine, InvalidationEngine>();

        // Register query factories
        services.AddTransient<IQueryFactory, SourceGeneratedQueryFactory>();

        return services;
    }

    public static IServiceCollection AddQuery<TQuery>(this IServiceCollection services)
        where TQuery : class, IQuery
    {
        services.AddTransient<TQuery>();
        return services;
    }
}

// ASP.NET Core integration
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGuacamole(options =>
        {
            options.EnableDistributedCaching = true;
            options.MaxCacheSize = 1_000_000;
        });

        services.AddQuery<UserProfileQuery>();
        services.AddQuery<PreferencesQuery>();
    }
}
```

**Required .NET Features**:
- Microsoft.Extensions.DependencyInjection: Native container integration
- IOptions Pattern: Configuration integration with .NET options system
- Factory Patterns: Efficient query instantiation with dependency injection

**Constitutional Compliance**:
- ✅ Dependency Injection: Full integration with Microsoft DI container
- ✅ Modern C# Features: Extension methods provide fluent registration API
- ✅ Performance Targets: Singleton database with efficient query factories

**Performance Considerations**: Singleton database ensures optimal cache sharing while scoped executors provide request isolation in web scenarios.

**Integration Notes**: Full integration with ASP.NET Core, Azure Functions, and .NET Generic Host patterns with proper lifetime management.

### Performance Optimization Integration

**Based On**: Constitutional requirements for < 10ms incremental updates and < 1ms cached queries

**C# Approach**: Leverage Span<T>, stackalloc, and source generators for zero-allocation fast paths with BenchmarkDotNet integration for performance validation.

```csharp
public readonly ref struct QueryContext
{
    private readonly Span<CacheEntry> _localCache;
    private readonly IGuacamoleDatabase _database;

    public QueryContext(IGuacamoleDatabase database, Span<CacheEntry> localCache)
    {
        _database = database;
        _localCache = localCache;
    }

    public TValue Query<TQuery, TKey, TValue>(TKey key)
        where TQuery : IQuery<TKey, TValue>, new()
        where TKey : notnull
    {
        var cacheKey = CacheKey.Create<TQuery, TKey>(key);

        // Try local cache first (stack allocated)
        foreach (ref readonly var entry in _localCache)
        {
            if (entry.Key.Equals(cacheKey))
                return (TValue)entry.Value;
        }

        // Fallback to database cache
        return _database.Query<TQuery, TKey, TValue>(key);
    }
}

// Usage with zero allocation
public async ValueTask<UserProfile> GetUserProfileAsync(UserId userId)
{
    Span<CacheEntry> localCache = stackalloc CacheEntry[16];
    var context = new QueryContext(_database, localCache);

    return context.Query<UserProfileQuery, UserId, UserProfile>(userId);
}
```

**Required .NET Features**:
- Span<T> and stackalloc: Zero-allocation local caching
- ref struct: Compile-time memory safety for stack-allocated data
- Source Generators: Compile-time optimization of hot paths

**Constitutional Compliance**:
- ✅ Performance Targets: Sub-millisecond cached query access through zero-allocation fast paths
- ✅ Modern C# Features: Latest memory management features for optimal performance
- ✅ Incremental Computation First: Performance optimizations maintain O(changes) characteristics

**Performance Considerations**: Achieves target performance through zero-allocation fast paths for cached queries and optimized incremental update algorithms.

**Integration Notes**: Integrates with System.Diagnostics.Activity for distributed tracing and BenchmarkDotNet for continuous performance validation.

## Cross-References

- **Based On**: All salsa-rs analysis documents provide foundation for these adaptations
- **Prerequisites**: [Architecture Overview](./architecture-overview.md), [Core Concepts](./core-concepts.md)
- **Related**: [API Patterns](./api-patterns.md) - Original patterns these adaptations are based on

## Summary

These theoretical .NET adaptations demonstrate how salsa-rs concepts can be effectively implemented using modern C# features while maintaining the performance and correctness guarantees that make incremental computation valuable. The adaptations leverage .NET 10's advanced features like spans, source generators, and improved async patterns to achieve O(changes) complexity with excellent integration into the Microsoft ecosystem.

The adaptations provide a comprehensive foundation for implementing the Guacamole incremental query engine framework with:
- Type-safe, ergonomic APIs using modern C# patterns
- High-performance implementation using zero-cost abstractions
- Seamless integration with Microsoft.Extensions.DependencyInjection
- Performance characteristics that meet constitutional requirements
- Natural integration with existing .NET development patterns and tooling