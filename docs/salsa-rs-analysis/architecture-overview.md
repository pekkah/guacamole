# Architecture Overview

## Overview

This document provides a high-level analysis of salsa-rs architecture, examining how it achieves efficient incremental computation through its query-driven design. Salsa-rs is a generic framework for on-demand, incrementalized computation that serves as the foundation for systems like rust-analyzer, demonstrating proven patterns for building responsive, adaptive programs.

## Core Architectural Principles

### Query-Driven Architecture

**Purpose**: Enable programs to express all computations as deterministic queries that can be cached, tracked, and incrementally updated.

**How It Works**: Salsa-rs treats programs as collections of queries where each query functions as a K -> V mapping from some key of type K to a value of type V. The system provides two fundamental query varieties:

- **Inputs**: Base inputs to your system that can be modified at any time
- **Functions**: Pure functions with no side effects that transform inputs into derived values

**Key Characteristics**:
- All computations expressed as deterministic queries
- Clear separation between mutable inputs and pure derived computations
- Dependency relationships automatically tracked by the framework
- Results cached to avoid redundant computation

**Rust Implementation Notes**: Uses Rust's trait system extensively with procedural macros like #[salsa::db] to define database traits and #[salsa::accumulator] for accumulated values.

### On-Demand Computation

**Purpose**: Compute values only when needed, avoiding unnecessary work for unused results.

**How It Works**: Queries are executed lazily - results are only computed when explicitly requested. The framework tracks which queries have been executed and what their dependencies are, building a dynamic dependency graph during execution.

**Key Characteristics**:
- Lazy evaluation ensures minimal computational work
- Dependency graph built dynamically as queries execute
- Unused query branches remain uncomputed
- Memory usage scales with actually-needed computations

**Rust Implementation Notes**: Leverages Rust's ownership system to manage query lifetimes and ensure memory safety without garbage collection overhead.

### Incremental Invalidation

**Purpose**: Achieve O(changes) complexity by recomputing only the minimal set of affected queries when inputs change.

**How It Works**: When inputs change, the framework uses the dependency graph to identify which cached results are now stale. Only queries that transitively depend on changed inputs are marked for recomputation. The next time a stale query is accessed, it's recomputed using the current inputs.

**Key Characteristics**:
- Precise invalidation based on actual dependencies
- Automatic propagation of changes through dependency graph
- Stale detection happens at query access time
- Recomputation preserves all still-valid cached results

**Rust Implementation Notes**: Uses Rust's type system to ensure thread-safety during invalidation through snapshot mechanisms and careful borrowing patterns.

## System Components

### Database Abstraction

**Purpose**: Provide a unified interface for storing inputs, cached results, and dependency metadata.

**How It Works**: The database serves as the central coordinator that stores input values, cached query results, and dependency tracking information. All queries operate through the database interface, ensuring consistent state management.

**Key Characteristics**:
- Single source of truth for all program state
- Thread-safe through snapshot mechanisms
- Supports parallel query execution
- Handles both mutable inputs and immutable cached results

**Rust Implementation Notes**: Implemented using the Database trait with procedural macro support for generating database implementations.

### Memoization Engine

**Purpose**: Cache query results to avoid redundant computation while maintaining consistency guarantees.

**How It Works**: Results from pure functions are automatically cached using their input parameters as keys. The memoization system integrates with dependency tracking to ensure cached results are invalidated when their dependencies change.

**Key Characteristics**:
- Automatic caching of pure function results
- Integrated with dependency tracking system
- Memory-efficient through selective cache invalidation
- Supports complex dependency relationships

**Rust Implementation Notes**: Benefits from Rust's zero-cost abstractions and compile-time optimizations for cache key generation and lookup.

### Dependency Tracking

**Purpose**: Automatically build and maintain the dependency graph that enables precise incremental updates.

**How It Works**: As queries execute, the framework automatically records which other queries they depend on. This creates a dynamic dependency graph that reflects the actual computation patterns of the program.

**Key Characteristics**:
- Automatic dependency discovery during query execution
- Dynamic graph construction based on actual computation paths
- Supports complex, conditional dependencies
- Enables precise change propagation

**Rust Implementation Notes**: Uses Rust's borrowing system to ensure safe concurrent access to dependency information.

## Concurrency Model

### Snapshot-Based Parallelism

**Purpose**: Enable safe concurrent query execution without compromising incremental computation guarantees.

**How It Works**: The ParallelDatabase functionality provides snapshot capabilities that allow multiple threads to safely read from the database concurrently. Snapshots provide immutable views of the database state, preventing race conditions during parallel query execution.

**Key Characteristics**:
- Immutable snapshots for safe concurrent access
- No blocking between reader threads
- Consistent view of database state per snapshot
- Writer isolation through controlled mutation points

**Rust Implementation Notes**: Leverages Rust's ownership system to statically guarantee that snapshots only provide immutable access, eliminating data races at compile time.

## Cross-References

- **Prerequisites**: Understanding of incremental computation concepts
- **Related**: [Core Concepts](./core-concepts.md) - Essential salsa-rs concepts
- **Related**: [API Patterns](./api-patterns.md) - Interface design principles
- **Adaptations**: [.NET Adaptations](./dotnet-adaptations.md) - Theoretical C# implementations

## Summary

Salsa-rs architecture demonstrates how query-driven design, on-demand computation, and precise incremental invalidation can be combined to achieve responsive, efficient programs that scale with the rate of change rather than total program size. The architecture's emphasis on pure functions, automatic dependency tracking, and snapshot-based concurrency provides a proven foundation for building incremental computation systems.

For Guacamole development, this architecture provides:
- Clear patterns for expressing computations as deterministic queries
- Proven approaches for achieving O(changes) complexity
- Robust concurrency models suitable for .NET adaptation
- Design principles that align with constitutional requirements for incremental-first development