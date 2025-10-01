# Research: ComputingContextBase Implementation

## Technical Decisions

### Source Generator Architecture
**Decision**: Use incremental source generators with Microsoft.CodeAnalysis.CSharp
**Rationale**:
- Constitutional requirement for incremental source generators (not legacy)
- Existing Guacamole.SourceGenerators infrastructure to extend
- Incremental generators provide better performance for IDE scenarios
- Required for automatic method generation based on property analysis

**Alternatives considered**:
- Legacy source generators (rejected: constitutional violation)
- Reflection-based approach (rejected: performance, startup cost)
- Manual code generation (rejected: poor developer experience)

### Property Classification Strategy
**Decision**: Use setter presence to distinguish input sources from derived computations
**Rationale**:
- Clear, intuitive syntax for developers
- Compile-time enforced distinction
- Aligns with existing C# property patterns
- Simplifies source generator logic

**Alternatives considered**:
- Attribute-based marking (rejected: more verbose, easy to forget)
- Naming conventions (rejected: fragile, error-prone)
- Separate base classes (rejected: complex inheritance hierarchy)

### Change Tracking Implementation
**Decision**: Generated strongly-typed change tracking with zero boxing
**Rationale**:
- Source generator creates property-specific change lists (e.g., `List<SourceFileChange>`)
- No runtime interfaces or boxing - all types known at compile time
- Batching multiple operations before commit with optimal memory layout
- Fail-fast error handling with generated exception types
- JIT optimization friendly with inlined operations

**Alternatives considered**:
- Interface-based tracking (rejected: virtual calls, boxing overhead)
- Generic change tracking (rejected: boxing value types)
- Event-based tracking (rejected: complex subscription management)
- Immutable snapshots (rejected: memory overhead for large contexts)

### Thread Safety Model
**Decision**: Multiple readers, single writer with ReaderWriterLockSlim
**Rationale**:
- Clarification specified this exact model
- Balances performance with safety
- Allows concurrent reads for query operations
- Single writer ensures consistency for mutations

**Alternatives considered**:
- Immutable snapshots (rejected: memory overhead)
- Full concurrent access (rejected: complexity, consistency issues)
- Single-threaded only (rejected: limits scalability)

### Integration with Computation Graph
**Decision**: Generated property-specific computation nodes with direct graph integration
**Rationale**:
- Source generator creates typed nodes for each property (e.g., `ProjectNode`, `SourceFilesCollectionNode`)
- No interface overhead - direct concrete implementations
- Leverages existing dependency tracking with zero allocation paths
- Maintains O(changes) complexity guarantees through generated optimizations
- Preserves constitutional incremental computation principles

**Alternatives considered**:
- Generic IComputationNode interface (rejected: virtual calls, boxing)
- Separate tracking system (rejected: duplication, performance loss)
- Manual dependency management (rejected: error-prone, violates principles)
- External coordination (rejected: breaks encapsulation)

## Implementation Patterns

### Error Handling Strategy
**Pattern**: Generated property-specific exception types with fail-fast behavior
**Rationale**:
- Source generator creates typed exceptions (e.g., `SourceFileOperationException`)
- Zero boxing overhead for error context information
- Clarification specified fail-fast behavior with proper error diagnostics
- JIT can optimize exception paths for specific property types
**Integration**: Inherit from base ComputationError infrastructure from Guacamole.Core

### Testing Strategy
**Pattern**: Incremental complexity TDD with XUnit v3
**Progression**:
1. Basic: Simple context with single property
2. Edge: Error conditions, duplicates, missing items
3. Incremental: Verify only changed dependencies recompute
4. Performance: O(changes) complexity validation
5. Chaos: Random mutations, stress testing

**Tools**: Verify.SourceGenerators for snapshot testing of generated code

### Performance Optimization
**Pattern**: Source generation-first zero-allocation architecture
**Techniques**:
- Generated concrete types eliminate boxing/unboxing completely
- Property-specific change lists avoid generic collections overhead
- Inlined mutation methods with direct type operations
- Generated SaveChanges with optimal batch processing loops
- Zero reflection - all property access generated at compile time
- Memory layout optimized per property type (value types stored directly)
- JIT-friendly code patterns with predictable branch paths
- Integrate with existing LRU cache and metrics infrastructure

## Dependencies Analysis

### Required Dependencies
- **Microsoft.Extensions.DependencyInjection**: Constitutional requirement, existing usage
- **Microsoft.CodeAnalysis.CSharp**: Source generator infrastructure
- **System.Collections.Immutable**: Existing Guacamole.Core dependency
- **XUnit v3**: Constitutional testing requirement

### Integration Points
- **Guacamole.Core.Providers**: IncrementalValueProvider<T> types
- **Guacamole.Core.Pipeline**: ComputationGraph and IComputationNode
- **Guacamole.Core.Common**: Result<T, Error> patterns
- **Guacamole.Core.Caching**: LRU cache infrastructure

### Package Architecture
- **Guacamole**: User-facing ComputingContextBase class
- **Guacamole.Core**: Low-level change tracking interfaces
- **Guacamole.SourceGenerators**: Context analysis and code generation
- No cross-references from SourceGenerators to main packages (constitutional requirement)

## Constitutional Compliance

### Incremental Computation First
✅ Context integrates with existing IncrementalValueProvider infrastructure
✅ Change tracking triggers only affected provider recomputation
✅ O(changes) complexity maintained through computation graph

### Query-Driven Architecture
✅ Context properties are IncrementalValueProvider queries
✅ Clear input source vs derived computation distinction
✅ Automatic dependency tracking through property composition

### Deterministic Execution
✅ Generated mutation methods are deterministic
✅ Change tracking operations have predictable outcomes
✅ No hidden state or non-deterministic behavior

### Performance as Correctness
✅ Specific performance targets: <1ms cached, <10ms incremental
✅ Memory overhead constraint: <2x baseline
✅ Integration with existing performance monitoring

### TDD with Incremental Complexity
✅ Planned progression from basic to chaos testing
✅ XUnit v3 usage as required
✅ Verify.SourceGenerators for generator testing

### Modern C# Features
✅ Incremental source generators (not legacy)
✅ Integration with existing record-based infrastructure
✅ Leverage .NET 10 features and patterns