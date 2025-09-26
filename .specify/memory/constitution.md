<!--
Sync Impact Report
==================
Version change: 2.2.2 → 2.3.0 (minor update - added C# coding practices standards)
Modified principles: None
Added sections:
- C# Coding Practices (comprehensive naming conventions and file organization)
Modified sections:
- Technical Guidelines (reorganized with new C# Coding Practices subsection)
Removed sections: None
Templates requiring updates:
- ✅ plan-template.md (version reference updated)
- ✅ spec-template.md (consistent, no updates needed)
- ✅ tasks-template.md (consistent, no updates needed)
Follow-up TODOs: None
-->

# Guacamole Constitution

## Core Principles

### I. Incremental Computation First
Every computation MUST be designed for incremental re-execution. Queries
MUST track their dependencies automatically. When inputs change, only
affected computations MUST re-execute. The engine MUST provide transparent
memoization and invalidation. This is the core value proposition of an
incremental query engine and drives all architectural decisions.

### II. Query-Driven Architecture
The system MUST be built around queries as first-class citizens. All
computations MUST be expressible as queries with explicit inputs and
outputs. Query composition MUST be the primary mechanism for building
complex computations. Side effects MUST be isolated and tracked. This
ensures predictable behavior and enables the incremental computation model.

### III. Deterministic Execution
All queries MUST be deterministic - same inputs MUST always produce same
outputs. Non-deterministic operations (timestamps, random values, I/O)
MUST be explicitly marked and handled through the dependency system.
Query execution order MUST not affect results. This enables reliable
caching and incremental computation.

### IV. Performance as Correctness
Performance characteristics ARE correctness requirements. The engine MUST
maintain O(changes) complexity for incremental updates, not O(dataset).
Memory usage MUST be bounded and predictable. Cache invalidation MUST
be precise - no over-invalidation or under-invalidation. Performance
regressions MUST fail CI builds just like functional regressions.

### V. Test-Driven Development with Incremental Complexity
Every feature MUST be developed using strict Test-Driven Development with
XUnit v3. Development MUST follow incremental complexity: start with the
simplest possible test case, make it pass, then incrementally add complexity.
Tests MUST progress from basic functionality to edge cases to performance
characteristics. Each test MUST validate correctness, incrementality,
performance bounds, and memory usage. The Red-Green-Refactor cycle is
mandatory with refactoring only after all tests pass.

### VI. Modern C# Features
The codebase MUST leverage .NET 10 and latest C# language features for
performance and expressiveness. Required features include: source generators
for query registration, spans and memory pools for zero-allocation paths,
async streams for incremental results, record types for immutable query
inputs/outputs, and pattern matching for query dispatch. Preview features
MAY be used with explicit justification.

## Framework Dependencies

### Dependency Injection Architecture
- All services MUST use Microsoft.Extensions.DependencyInjection
- Query engines MUST be registered as scoped or singleton services
- Query definitions MUST be registered automatically via service collection extensions
- No service location patterns - all dependencies MUST be constructor-injected
- Lifetime management MUST follow DI container scopes

### Testing Framework Standards
- All tests MUST use XUnit v3 as the primary testing framework
- Test fixtures MUST implement IAsyncLifetime for async setup/teardown
- Theory tests MUST be used for parameterized incrementality tests
- Test collections MUST isolate query engine instances to prevent cross-test interference
- Performance tests MUST use BenchmarkDotNet for accurate measurement
- Test development MUST follow incremental complexity progression
- Each feature MUST have test categories: unit → integration → performance → chaos

### Configuration and Options
- Configuration MUST use Microsoft.Extensions.Options pattern
- Query engine options MUST be validated at registration time
- Hot reload of configuration MUST NOT affect active queries
- Configuration sources MUST be injectable for testing scenarios

## Incremental Query Engine Standards

### Query Design Requirements
- Every query MUST declare its dependencies explicitly
- Queries MUST be pure functions (no side effects)
- Query results MUST be immutable
- Dependencies MUST form a directed acyclic graph (no cycles)
- Queries MUST support cancellation and timeout

### Caching and Invalidation
- Cache keys MUST include all query inputs
- Invalidation MUST be precise (no false positives/negatives)
- Cache eviction MUST follow LRU or similar predictable policy
- Memory pressure MUST trigger graceful degradation
- Cache statistics MUST be observable

### Debugging and Observability
- Query execution MUST be traceable
- Dependency graphs MUST be visualizable
- Incremental vs full recomputation MUST be measurable
- Cache hit/miss rates MUST be monitored
- Query timing MUST include breakdown by phase

## Package Distribution Standards

### NuGet Package Requirements
- Packages MUST be published to nuget.org as libraries (no executables)
- Package ID MUST be "Guacamole" with appropriate suffixes for sub-packages
- Semantic versioning MUST be strictly followed (MAJOR.MINOR.PATCH)
- Pre-release versions MUST use standard suffixes (-alpha, -beta, -rc)
- Package metadata MUST include: description, project URL, license, tags

### API Surface Management
- Public API MUST be minimal and well-documented
- Breaking changes MUST increment MAJOR version
- New features MUST increment MINOR version
- Bug fixes MUST increment PATCH version
- Internal APIs MUST be marked with [InternalsVisibleTo] only for test assemblies
- Experimental features MUST use [Experimental] attribute with diagnostic ID

### Package Structure
- Core package: Guacamole (engine core, Microsoft.Extensions.DependencyInjection only)
- Extension packages: Guacamole.* (analyzers, visualizers, integrations)
- Source generators MUST be in separate Guacamole.Generators package
- Benchmarks and samples MUST NOT be included in distributed packages
- Debug symbols MUST be published to symbol server
- No executable assemblies or console applications

### Dependency Management
- Core package MUST depend only on Microsoft.Extensions.DependencyInjection
- Target framework MUST be .NET 10.0 only
- Dependencies in extension packages MUST use lowest compatible version
- Transitive dependencies MUST be reviewed for security and licensing
- Package MUST include THIRD-PARTY-NOTICES for all dependencies

## Development Standards

### Branching Strategy
- No direct commits to master/main branch
- Feature branches for new work: `feature/[name]` or `[ticket-id]-[name]`
- Pull requests required for all merges
- Code review mandatory before merge
- Resolve all PR comments before merging

### Testing Requirements with Incremental Complexity
- Unit test coverage minimum: 90% (higher bar for engine code)
- All tests MUST use XUnit v3 framework
- Test development MUST follow incremental complexity progression:
  1. **Basic functionality**: Happy path with simplest inputs
  2. **Edge cases**: Boundary conditions and error scenarios
  3. **Incrementality**: Verify only affected computations re-execute
  4. **Performance**: Validate O(changes) complexity and memory bounds
  5. **Chaos**: Random inputs and failure injection
- Each test category MUST pass before proceeding to the next
- Performance benchmarks with regression detection
- Memory leak detection in CI
- Integration tests comparing against salsa-rs behavior

### Security Practices
- No secrets or credentials in code
- Use configuration and secure vaults for sensitive data
- Input validation on all external data
- Prevent query amplification attacks
- Resource limits on query execution

## Technical Guidelines

### C# Coding Practices
- **Naming Conventions**:
  - Classes, methods, properties, events MUST use PascalCase (e.g., `QueryEngine`, `ExecuteAsync`)
  - Interfaces MUST start with 'I' followed by PascalCase (e.g., `IQueryCache`)
  - Private fields MUST use underscore prefix with camelCase (e.g., `_queryCache`, `_isInitialized`)
  - Local variables and parameters MUST use camelCase (e.g., `queryResult`, `cancellationToken`)
  - Constants MUST use PascalCase (e.g., `DefaultTimeout`)
  - Async methods MUST end with 'Async' suffix (e.g., `ExecuteQueryAsync`)

- **File Organization**:
  - File names MUST match class names exactly using PascalCase (e.g., `QueryEngine.cs`)
  - One public type per file (classes, interfaces, enums)
  - Nested types are allowed within the primary type's file
  - Test files MUST end with 'Tests' suffix (e.g., `QueryEngineTests.cs`)

- **Code Structure**:
  - Using statements MUST be inside namespace declaration
  - Properties MUST use expression-bodied syntax when appropriate
  - Methods MUST use expression-bodied syntax only for single expressions
  - Braces MUST be on new lines (Allman style)
  - No trailing whitespace
  - File MUST end with single newline

### .NET Specific Standards
- Target .NET 10.0 only (upgrade to RTM when available)
- Enable nullable reference types everywhere
- Use ValueTask for hot paths
- Implement IAsyncEnumerable for streaming results
- Follow Microsoft's High-Performance .NET guidelines
- Use source generators over reflection
- All services MUST support dependency injection patterns

### Performance Considerations
- Query execution: <1ms for cached, <10ms for incremental
- Memory overhead: <2x input data size
- Zero allocations in hot paths
- Benchmark against salsa-rs for comparable operations
- Profile-guided optimization for critical paths

### Library Architecture
- No main() methods or executable entry points
- All functionality exposed through public APIs
- Extension methods for service registration
- Fluent configuration APIs where appropriate
- Thread-safe by default for concurrent query execution

## Governance

The Constitution supersedes all other development practices and standards.
Any amendments require:
1. Documentation of the proposed change
2. Team review and approval
3. Update to all affected templates and processes
4. Version increment following semantic versioning
5. Performance impact assessment for engine changes

All pull requests MUST verify constitutional compliance. Complexity beyond
these principles MUST be justified in writing. Violations without
justification will result in PR rejection.

**Version**: 2.3.0 | **Ratified**: 2025-09-26 | **Last Amended**: 2025-09-26