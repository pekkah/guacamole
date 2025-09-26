# Core Concepts

## Overview

This document analyzes the fundamental concepts that make salsa-rs an effective incremental computation framework. These concepts form the conceptual building blocks that enable efficient, responsive programs that adapt automatically to changing inputs while maintaining correctness guarantees.

## Query System

### Query

**Purpose**: Serve as the fundamental unit of computation that maps inputs to outputs in a trackable, cacheable way.

**How It Works**: A query represents a deterministic computation that takes some input (key) and produces an output (value). Queries are the basic building blocks of salsa-rs programs, where all computation is expressed as K -> V mappings that can be cached and tracked for dependencies.

**Key Characteristics**:
- Deterministic: Same inputs always produce same outputs
- Pure: No side effects or external dependencies
- Cacheable: Results can be memoized for performance
- Trackable: Dependencies automatically recorded during execution

**Rust Implementation Notes**: Queries are typically defined using procedural macros and implemented as methods on database traits, leveraging Rust's type system for compile-time correctness.

### Input Query

**Purpose**: Represent the mutable base inputs to the system that can be changed to trigger incremental updates.

**How It Works**: Input queries store the fundamental data that drives all other computations. They are the only queries that can be mutated - all other queries are derived from inputs through pure functions. When inputs change, the framework automatically identifies and invalidates affected derived queries.

**Key Characteristics**:
- Mutable: Can be updated to reflect changing external state
- Root dependencies: All other queries ultimately depend on inputs
- Change triggers: Modifications to inputs initiate incremental updates
- Explicit interface: Clear API for external systems to provide data

**Rust Implementation Notes**: Input queries are implemented with special accessor methods that handle both reading current values and updating them while maintaining dependency tracking.

### Derived Query

**Purpose**: Express computations that transform inputs and other derived values into new results, forming the computational graph.

**How It Works**: Derived queries are pure functions that compute values based on inputs and other derived queries. They automatically become dependencies in the computational graph when they access other queries. Results are cached until their dependencies change.

**Key Characteristics**:
- Pure functions: No side effects, deterministic results
- Automatic dependency tracking: Framework records accessed queries
- Cached results: Computed values stored until invalidation
- Composable: Can depend on other derived queries to build complex computations

**Rust Implementation Notes**: Implemented as pure functions with automatic memoization and dependency tracking through Rust's trait system and procedural macros.

## Dependency Management

### Dependency Graph

**Purpose**: Represent the relationships between queries to enable precise incremental updates and efficient cache invalidation.

**How It Works**: As queries execute and access other queries, the framework automatically builds a dependency graph that captures which queries depend on which others. This graph is used to determine what needs to be recomputed when inputs change.

**Key Characteristics**:
- Dynamic construction: Built during query execution based on actual access patterns
- Precise relationships: Captures exact dependencies, not over-approximations
- Change propagation: Used to identify affected queries when inputs change
- Memory efficient: Only tracks actively used dependencies

**Rust Implementation Notes**: Leverages Rust's borrowing system to safely track dependencies without introducing memory leaks or cycles.

### Automatic Dependency Tracking

**Purpose**: Eliminate manual dependency management by automatically discovering and recording query relationships during execution.

**How It Works**: When a query executes and accesses other queries, the framework transparently records these accesses as dependencies. This happens automatically without requiring explicit dependency declarations from the programmer.

**Key Characteristics**:
- Transparent operation: No manual dependency declarations required
- Runtime discovery: Dependencies discovered during actual execution
- Accurate tracking: Only real dependencies recorded, not potential ones
- Zero maintenance: Dependencies automatically updated as code changes

**Rust Implementation Notes**: Uses Rust's thread-local storage and borrowing patterns to safely track query access during execution without runtime overhead.

## Incremental Computation

### Memoization

**Purpose**: Cache computation results to avoid redundant work while ensuring cache consistency with dependency changes.

**How It Works**: When a query is first executed, its result is cached along with information about which other queries it depended on. Future accesses return the cached result immediately, unless the query has been invalidated due to dependency changes.

**Key Characteristics**:
- Automatic caching: Results stored without explicit cache management
- Dependency-aware: Cache invalidation tied to dependency changes
- Memory efficient: Only caches results that may be reused
- Consistent: Cache guarantees reflect current input state

**Rust Implementation Notes**: Benefits from Rust's zero-cost abstractions to provide efficient cache key generation and lookup without runtime overhead.

### Invalidation

**Purpose**: Efficiently identify and mark stale cached results when inputs change, enabling precise incremental updates.

**How It Works**: When an input query changes, the framework uses the dependency graph to identify all derived queries that transitively depend on that input. These queries are marked as invalid, causing them to be recomputed the next time they're accessed.

**Key Characteristics**:
- Precise scope: Only invalidates actually affected queries
- Lazy recomputation: Invalid queries recomputed only when accessed
- Transitive propagation: Changes propagate through entire dependency chain
- Batch efficiency: Multiple input changes can be batched for efficient invalidation

**Rust Implementation Notes**: Uses Rust's ownership system to ensure memory safety during invalidation without requiring garbage collection.

### Incremental Update

**Purpose**: Achieve O(changes) complexity by recomputing only the minimal set of computations affected by input changes.

**How It Works**: Rather than recomputing everything when inputs change, the system uses dependency tracking and precise invalidation to identify exactly which computations need to be updated. Only these computations are performed, while all unaffected cached results remain valid.

**Key Characteristics**:
- Minimal recomputation: Only affected computations updated
- O(changes) complexity: Performance scales with changes, not total program size
- Correctness guarantees: Results always reflect current input state
- Automatic optimization: Framework handles all incremental update logic

**Rust Implementation Notes**: Leverages Rust's compile-time optimizations and zero-cost abstractions to ensure incremental updates have minimal overhead.

## Database Abstraction

### Database

**Purpose**: Provide a unified interface for managing inputs, cached results, and dependency metadata across the entire computation system.

**How It Works**: The database serves as the central coordinator that stores all program state including input values, cached query results, and dependency tracking information. All queries operate through the database interface to ensure consistent state management.

**Key Characteristics**:
- Central coordination: Single interface for all program state
- Type safety: Rust's type system ensures correct query access patterns
- Thread safety: Supports safe concurrent access through snapshots
- Extensible: Custom database traits can add domain-specific functionality

**Rust Implementation Notes**: Implemented using Rust's trait system with procedural macro support for generating efficient database implementations.

### Snapshot

**Purpose**: Enable safe concurrent access to the database by providing immutable views of the current state.

**How It Works**: Snapshots create immutable views of the database that multiple threads can access concurrently without interfering with each other or with writers. Each snapshot provides a consistent view of the database state at the time it was created.

**Key Characteristics**:
- Immutable views: Snapshots cannot modify database state
- Concurrent safety: Multiple threads can use snapshots simultaneously
- Consistent state: Each snapshot reflects a specific point in time
- Zero-copy: Snapshots don't duplicate data, just provide safe access

**Rust Implementation Notes**: Uses Rust's ownership system to statically guarantee that snapshots only provide read access, eliminating data races at compile time.

## Cross-References

- **Prerequisites**: [Architecture Overview](./architecture-overview.md) - High-level architectural understanding
- **Related**: [Algorithms Analysis](./algorithms-analysis.md) - Implementation details of incremental algorithms
- **Related**: [API Patterns](./api-patterns.md) - How these concepts are exposed through APIs
- **Adaptations**: [.NET Adaptations](./dotnet-adaptations.md) - Theoretical C# implementations of these concepts

## Summary

These core concepts demonstrate how salsa-rs achieves efficient incremental computation through a carefully designed system of queries, automatic dependency tracking, and precise invalidation. The combination of pure functional programming principles with automatic optimization creates a framework that is both easy to use and highly performant.

For Guacamole development, these concepts provide:
- Clear mental models for incremental computation design
- Proven patterns for expressing computations as queries
- Automatic optimization strategies that can be adapted to .NET
- Dependency management approaches suitable for C# implementation