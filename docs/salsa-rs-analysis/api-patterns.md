# API Patterns

## Overview

This document analyzes the interface design and query composition patterns that make salsa-rs both powerful and ergonomic. These patterns demonstrate how incremental computation frameworks can provide intuitive APIs while maintaining the performance and correctness guarantees necessary for responsive systems.

## Interface Design Patterns

### Trait-Based Query Definition

**Purpose**: Provide a type-safe, ergonomic way to define queries while leveraging Rust's trait system for compile-time optimization.

**How It Works**: Queries are defined as methods on database traits, with procedural macros generating the necessary implementation code for dependency tracking, caching, and invalidation. The trait system ensures type safety while enabling flexible composition.

**Key Characteristics**:
- Type-safe query definitions with compile-time verification
- Procedural macro generation reduces boilerplate code
- Trait composition enables modular database design
- Clear separation between query interface and implementation

**Rust Implementation Notes**: Uses #[salsa::db] and related procedural macros to generate efficient implementation code while maintaining clean, readable interface definitions.

### Database Builder Pattern

**Purpose**: Provide a fluent, configurable interface for constructing database instances with appropriate query implementations.

**How It Works**: Uses the builder pattern to configure database options, register query implementations, and construct fully initialized database instances. The builder ensures all required components are properly configured before database creation.

**Key Characteristics**:
- Fluent interface for intuitive database configuration
- Compile-time verification of required components
- Extensible design supports custom configuration options
- Clear error messages for misconfigured databases

**Rust Implementation Notes**: Leverages Rust's type system to encode configuration state at the type level, ensuring correctness without runtime checks.

### Snapshot Creation API

**Purpose**: Provide safe, efficient access to database state for concurrent operations while maintaining consistency guarantees.

**How It Works**: Exposes snapshot creation through simple method calls that return immutable views of the database. Snapshots provide the same query interface as the main database but with read-only access guarantees.

**Key Characteristics**:
- Simple API for creating immutable snapshots
- Same query interface available on snapshots
- Thread-safe concurrent access without explicit synchronization
- Zero-copy snapshot creation for efficiency

**Rust Implementation Notes**: Uses Rust's ownership system to statically guarantee snapshot immutability while providing efficient shared access to underlying data.

## Query Composition Patterns

### Functional Query Composition

**Purpose**: Enable building complex computations by composing simpler queries in a functional programming style.

**How It Works**: Queries can call other queries freely, with the framework automatically tracking dependencies. This allows breaking complex computations into smaller, reusable components that can be combined in various ways.

**Key Characteristics**:
- Natural functional composition of query components
- Automatic dependency tracking across composition boundaries
- Reusable query components for modular design
- Clear data flow through composed query chains

**Rust Implementation Notes**: Benefits from Rust's zero-cost abstractions to ensure composed queries have no performance overhead compared to monolithic implementations.

### Conditional Query Execution

**Purpose**: Support computations that depend on input values to determine which other queries to execute, enabling efficient handling of conditional logic.

**How It Works**: Queries can conditionally execute other queries based on runtime values. The dependency tracking system automatically handles the varying dependency patterns that result from conditional execution.

**Key Characteristics**:
- Runtime conditional logic with automatic dependency tracking
- Efficient handling of rarely-executed code paths
- Dynamic dependency graphs that adapt to control flow
- Precise invalidation even with complex conditional dependencies

**Rust Implementation Notes**: Uses Rust's pattern matching and control flow constructs while maintaining dependency tracking through all execution paths.

### Query Parameterization

**Purpose**: Enable creation of generic, reusable queries that can be specialized for different input types or configuration parameters.

**How It Works**: Queries can be parameterized using Rust's generic type system, allowing the same query logic to work with different data types while maintaining type safety and performance.

**Key Characteristics**:
- Generic queries work with multiple data types
- Compile-time specialization for optimal performance
- Type-safe parameterization prevents runtime errors
- Reusable query patterns across different domains

**Rust Implementation Notes**: Uses monomorphization to generate specialized versions of generic queries for each used type combination, eliminating runtime overhead.

## Ergonomic API Patterns

### Automatic Derivation

**Purpose**: Reduce boilerplate code by automatically generating common query implementations based on data structure definitions.

**How It Works**: Derive macros analyze data structure definitions and generate appropriate query implementations for common patterns like field access, collection operations, and structural transformations.

**Key Characteristics**:
- Automatic generation of common query patterns
- Reduced boilerplate and maintenance burden
- Consistent implementation patterns across codebase
- Customizable derivation for domain-specific needs

**Rust Implementation Notes**: Uses procedural macros to analyze type definitions at compile time and generate efficient, specialized query implementations.

### Macro-Based Query DSL

**Purpose**: Provide domain-specific language constructs that make query definition more natural and less verbose.

**How It Works**: Macro-rules macros provide higher-level constructs for common query patterns, allowing more natural expression of incremental computations while generating efficient underlying implementations.

**Key Characteristics**:
- Domain-specific syntax for common patterns
- Reduced verbosity in query definitions
- Compile-time expansion to efficient implementations
- Extensible macro system for custom patterns

**Rust Implementation Notes**: Uses macro hygiene and compile-time expansion to ensure generated code is efficient and maintains all safety guarantees.

### Error Handling Integration

**Purpose**: Integrate incremental computation with Rust's error handling patterns to provide robust error management across query boundaries.

**How It Works**: Queries can return Result types, with the framework automatically handling error propagation through dependency chains. Failed queries are properly invalidated and can be retried when their dependencies change.

**Key Characteristics**:
- Natural integration with Rust's Result-based error handling
- Automatic error propagation through query dependencies
- Proper invalidation of failed computations
- Support for error recovery through dependency updates

**Rust Implementation Notes**: Uses Rust's ? operator and Result combinators while maintaining dependency tracking and cache consistency even in error cases.

## Performance-Oriented API Patterns

### Zero-Copy Query Results

**Purpose**: Minimize memory allocation and copying by providing direct access to cached query results when possible.

**How It Works**: Query APIs are designed to return references to cached data when lifetime constraints allow, avoiding unnecessary copying while maintaining memory safety through Rust's borrowing system.

**Key Characteristics**:
- Direct references to cached data when safe
- Eliminated unnecessary memory allocations
- Maintained memory safety through compile-time checking
- Optimal performance for read-heavy workloads

**Rust Implementation Notes**: Uses Rust's lifetime system to statically guarantee safety of zero-copy access patterns while providing maximum performance.

### Batch Operation APIs

**Purpose**: Enable efficient processing of multiple related operations while minimizing incremental computation overhead.

**How It Works**: Provides APIs for batching multiple input updates or query executions, allowing the framework to optimize dependency tracking and cache management across the entire batch.

**Key Characteristics**:
- Efficient batching of related operations
- Optimized dependency tracking for batch scenarios
- Reduced overhead compared to individual operations
- Maintains correctness guarantees across batch boundaries

**Rust Implementation Notes**: Uses iterator abstractions and batch-aware algorithms to maximize efficiency while preserving incremental computation benefits.

### Parallel Query Execution

**Purpose**: Leverage multi-core processors by executing independent queries concurrently while maintaining consistency.

**How It Works**: Provides APIs for parallel query execution that automatically identify independence opportunities and execute queries concurrently when safe to do so.

**Key Characteristics**:
- Automatic parallelization of independent queries
- Thread-safe execution with consistency guarantees
- Efficient work distribution across available cores
- Maintained sequential semantics despite parallel execution

**Rust Implementation Notes**: Uses Rust's Send/Sync traits and ownership system to ensure thread safety while maximizing parallel execution opportunities.

## Integration Patterns

### External System Integration

**Purpose**: Provide clean interfaces for integrating incremental computation with external systems and data sources.

**How It Works**: Defines clear boundaries between incremental computation and external systems, with well-defined input query patterns for incorporating external data while maintaining incremental computation benefits.

**Key Characteristics**:
- Clear integration boundaries with external systems
- Maintained incremental computation benefits despite external dependencies
- Flexible patterns for various external data sources
- Robust handling of external system failures

**Rust Implementation Notes**: Uses Rust's trait system to provide clean abstractions over external systems while maintaining type safety and performance.

### Testing and Debugging Support

**Purpose**: Provide APIs and patterns that facilitate testing incremental computation logic and debugging performance issues.

**How It Works**: Exposes debugging information about dependency graphs, cache state, and computation paths while providing test-friendly APIs for controlling incremental computation behavior.

**Key Characteristics**:
- Comprehensive debugging information exposure
- Test-friendly APIs for controlled incremental computation
- Performance profiling integration points
- Clear visibility into framework internal state

**Rust Implementation Notes**: Uses conditional compilation and feature flags to provide debugging capabilities without impacting production performance.

## Cross-References

- **Prerequisites**: [Core Concepts](./core-concepts.md) - Understanding of queries and dependencies
- **Related**: [Architecture Overview](./architecture-overview.md) - High-level API design principles
- **Related**: [Caching Strategies](./caching-strategies.md) - Cache management through APIs
- **Adaptations**: [.NET Adaptations](./dotnet-adaptations.md) - Theoretical C# API implementations

## Summary

These API patterns demonstrate how salsa-rs achieves both power and usability through careful interface design that leverages Rust's type system and zero-cost abstractions. The combination of trait-based composition, functional query patterns, and performance-oriented APIs provides a robust foundation for building ergonomic incremental computation systems.

For Guacamole development, these patterns provide:
- Interface design principles adaptable to C# generics and interfaces
- Composition patterns suitable for .NET dependency injection
- Performance optimization strategies compatible with .NET spans and value types
- Ergonomic API approaches that can be implemented using C# source generators