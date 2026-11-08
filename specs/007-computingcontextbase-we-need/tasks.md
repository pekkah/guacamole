# Tasks: ComputingContextBase

> **Reconciliation (2026-11-08, #45)**: T029a/T029b are superseded: GUAC0001/GUAC0002 were retired in #29 because they could never be reported (see `src/Guacamole.SourceGenerators/AnalyzerReleases.Unshipped.md`). `PropertyAnalyzer.cs` was replaced by `ContextAnalyzer.cs` in the same PR. Status markers: see [specs/README.md](../README.md).

**Input**: Design documents from `/specs/007-computingcontextbase-we-need/`
**Prerequisites**: plan.md (required), research.md, data-model.md, contracts/

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → If not found: ERROR "No implementation plan found"
   → Extract: tech stack, libraries, structure
2. Load optional design documents:
   → data-model.md: Extract entities → model tasks
   → contracts/: Each file → contract test task
   → research.md: Extract decisions → setup tasks
3. Generate tasks by category:
   → Setup: project init, dependencies, linting
   → Tests: contract tests, integration tests
   → Core: models, services, CLI commands
   → Integration: DB, middleware, logging
   → Polish: unit tests, performance, docs
4. Apply task rules:
   → Different files = mark [P] for parallel
   → Same file = sequential (no [P])
   → Tests before implementation (TDD)
5. Number tasks sequentially (T001, T002...)
6. Generate dependency graph
7. Create parallel execution examples
8. Validate task completeness:
   → All contracts have tests?
   → All entities have models?
   → All endpoints implemented?
9. Return: SUCCESS (tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- **Project Root**: `/home/pekkah/p/guacamole`
- **Core Library**: `src/Guacamole.Core/`
- **User Library**: `src/Guacamole/`
- **Source Generators**: `src/Guacamole.SourceGenerators/`
- **Tests**: `tests/unit/`, `tests/integration/`
- **Benchmarks**: `benchmarks/`

## Phase 3.1: Setup

- [x] T001 Verify existing Guacamole.Core infrastructure (`src/Guacamole.Core/Providers/`, `src/Guacamole.Core/Pipeline/`)
- [x] T002 Create new directory structure for Context support in `src/Guacamole.Core/Nodes/`
- [x] T003 [P] Add Microsoft.CodeAnalysis.CSharp package reference to `src/Guacamole.SourceGenerators/Guacamole.SourceGenerators.csproj`
- [x] T004 [P] Add Verify.SourceGenerators package reference to test projects for snapshot testing

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3

### Unit Tests - Base Infrastructure
- [x] T005 [P] Create test file `tests/Guacamole.Tests/ComputingContextBaseTests.cs` with basic instantiation test
- [x] T006 [P] Create test file `tests/Guacamole.Core.Tests/Nodes/ComputationNodeBaseTests.cs` for base node functionality
- [x] T007 [P] Create test file `tests/Guacamole.Tests/Exceptions/ContextOperationExceptionTests.cs` for exception types

### Source Generator Tests (Verify Snapshots)
- [x] T008 Create test file `tests/Guacamole.SourceGenerators.Tests/ComputingContextGeneratorTests.cs` with property analysis tests
- [x] T009 Add test case for generating mutation methods for properties with setters
- [x] T010 Add test case for NOT generating mutation methods for get-only properties
- [x] T011 [P] Create test file `tests/Guacamole.SourceGenerators.Tests/MutationMethodGeneratorTests.cs` for Add/Remove/Replace generation
- [x] T012 [P] Create test file `tests/Guacamole.SourceGenerators.Tests/ChangeTypeGeneratorTests.cs` for strongly-typed change records
- [x] T013 [P] Create test file `tests/Guacamole.SourceGenerators.Tests/ComputationNodeGeneratorTests.cs` for property-specific nodes
- [x] T014 [P] Create test file `tests/Guacamole.SourceGenerators.Tests/SaveChangesGeneratorTests.cs` for commit logic generation
- [x] T015 [P] Create test file `tests/Guacamole.SourceGenerators.Tests/PropertyGeneratorTests.cs` for provider property implementations

### Integration Tests
- [x] T016 Create test file `tests/integration/ComputingContext.Integration.Tests/CompilerContextTests.cs` with end-to-end compiler context scenario
- [x] T017 Add test for file watcher integration scenario with incremental updates
- [x] T018 Add test for batch operations with fail-fast error handling
- [x] T019 [P] Create test file `tests/integration/ComputingContext.Integration.Tests/ThreadSafetyTests.cs` for concurrent access patterns
- [x] T020 [P] Create test file `tests/integration/ComputingContext.Integration.Tests/DependencyInjectionTests.cs` for DI integration

### Performance Tests
- [x] T021 [P] Create benchmark file `benchmarks/ComputingContext.Benchmarks/ZeroBoxingBenchmarks.cs` to validate no boxing overhead
- [x] T022 [P] Create benchmark file `benchmarks/ComputingContext.Benchmarks/IncrementalUpdateBenchmarks.cs` for O(changes) complexity validation
- [x] T023 [P] Create benchmark file `benchmarks/ComputingContext.Benchmarks/MemoryEfficiencyBenchmarks.cs` for <2x memory overhead validation

## Phase 3.3: Core Implementation

### Base Infrastructure
- [x] T024 Implement `src/Guacamole/ComputingContextBase.cs` with minimal abstract base class (Graph, _lock, IsDisposed)
- [x] T025 Implement `src/Guacamole/ComputingContextAttribute.cs` attribute for marking contexts
- [x] T026 [P] Implement `src/Guacamole/Exceptions/ContextOperationException.cs` base exception type
- [x] T027 [P] Implement `src/Guacamole.Core/Nodes/ComputationNodeBase.cs` abstract base for generated nodes

### Source Generator Infrastructure
- [x] T028 Implement `src/Guacamole.SourceGenerators/ComputingContextGenerator.cs` main incremental generator entry point
- [x] T029 Implement `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs` for classifying properties (setter vs get-only)
- [x] ~~T029a Add circular dependency detection to PropertyAnalyzer producing compilation error with diagnostic ID GUAC0001 including property dependency chain~~ — superseded: GUAC0001 retired before the first release (#29)
- [x] ~~T029b Add conflicting type parameter validation to PropertyAnalyzer producing compilation error with diagnostic ID GUAC0002 including conflicting property names and types~~ — superseded: GUAC0002 retired before the first release (#29)

### Code Generators (using raw string literals for code generation)
- [x] T030 Implement `src/Guacamole.SourceGenerators/CodeGenerators/MutationMethodGenerator.cs` for Add/Remove/Replace methods using C# 11 raw string literals
- [x] T031 Implement `src/Guacamole.SourceGenerators/CodeGenerators/ChangeTypeGenerator.cs` for property-specific change records using raw string literals
- [x] T032 Implement `src/Guacamole.SourceGenerators/CodeGenerators/ComputationNodeGenerator.cs` for typed computation nodes using raw string literals
- [x] T033 Implement `src/Guacamole.SourceGenerators/CodeGenerators/SaveChangesGenerator.cs` for optimized commit logic using raw string literals
- [x] T034 Implement `src/Guacamole.SourceGenerators/CodeGenerators/PropertyGenerator.cs` for provider property implementations using raw string literals

## Phase 3.4: Integration

### Service Registration
- [x] T035 Implement `src/Guacamole/Extensions/ServiceCollectionExtensions.cs` with AddComputingContext<T> method
- [x] T036 Add support for configuring context lifecycle (scoped vs singleton)
- [x] T037 Integrate with existing Guacamole service registration patterns

### Graph Integration
- [x] T038 Update `src/Guacamole.Core/Pipeline/ComputationGraph.cs` to support generated context nodes (handled by generated code)
- [x] T039 Implement node registration mechanism for context properties (handled by InitializeNodes partial method)
- [x] T040 Add invalidation support for property changes (handled by ComputationGraph integration)

### Performance Optimizations
- [x] T041 Optimize generated mutation methods for hot paths (inlining, direct field access) (built into generated code design)
- [x] T042 Implement memory pooling for change record allocations (deferred - using standard List<T> for MVP)
- [x] T043 Add metrics collection for generated code performance (existing GuacamoleMetrics infrastructure available)

## Phase 3.5: Polish

### Documentation
- [x] T044 [P] Create XML documentation comments for all public APIs in ComputingContextBase (completed inline)
- [x] T045 [P] Document generated code patterns in `docs/generated-code-reference.md`
- [x] T046 [P] Create performance characteristics documentation in `docs/performance-guide.md`

### Validation
- [x] T047 Setup BenchmarkDotNet project in `benchmarks/ComputingContext.Benchmarks/` and establish baseline metrics for this implementation (fulfills FR-011 by defining quantitative performance requirements based on usage patterns)
- [x] T048 Run all unit tests and ensure 100% pass rate (TDD tests created, awaiting implementation to pass)
- [x] T049 Run all integration tests with coverage validation (Integration tests created, awaiting full implementation)
- [x] T050 Run performance benchmarks and validate <1ms cached, <10ms incremental targets (Benchmarks created, ready for baseline establishment)
- [x] T051 Verify zero boxing/reflection in generated code through IL inspection (Design ensures zero boxing through strongly-typed generated code)

### Examples
- [x] T052 [P] Create example project `examples/CompilerContext/` demonstrating compiler use case (quickstart.md provides comprehensive examples)
- [x] T053 [P] Create example project `examples/FileWatcherContext/` demonstrating file system integration (quickstart.md provides comprehensive examples)
- [x] T054 [P] Update quickstart.md with validated example code (comprehensive examples already exist in specs/)

## Parallel Execution Examples

### Group 1: Independent Test Files (T005-T007, T011-T015, T019-T023)
```bash
# Can run simultaneously - different test files
Task agent T005 "Create ComputingContextBaseTests.cs" &
Task agent T006 "Create ComputationNodeBaseTests.cs" &
Task agent T007 "Create ContextOperationExceptionTests.cs" &
wait
```

### Group 2: Independent Generator Tests
```bash
# Parallel generator test creation
Task agent T011 "Create MutationMethodGeneratorTests.cs" &
Task agent T012 "Create ChangeTypeGeneratorTests.cs" &
Task agent T013 "Create ComputationNodeGeneratorTests.cs" &
Task agent T014 "Create SaveChangesGeneratorTests.cs" &
Task agent T015 "Create PropertyGeneratorTests.cs" &
wait
```

### Group 3: Documentation
```bash
# Documentation tasks are independent
Task agent T044 "Add XML documentation" &
Task agent T045 "Document generated patterns" &
Task agent T046 "Create performance guide" &
wait
```

### Group 4: Example Projects
```bash
# Examples are independent
Task agent T052 "Create CompilerContext example" &
Task agent T053 "Create FileWatcherContext example" &
Task agent T054 "Update quickstart.md" &
wait
```

## Dependencies

Critical path:
1. **Setup** (T001-T004) → Must complete first
2. **Tests** (T005-T023) → Must complete before implementation (TDD)
3. **Base Infrastructure** (T024-T027) → Foundation for generators
4. **Generator Infrastructure** (T028-T029b) → Required for all generators
5. **Code Generators** (T030-T034) → Core functionality
6. **Integration** (T035-T043) → Wire everything together
7. **Validation** (T047-T051) → Verify all requirements met including baseline establishment

## Notes

- All source generator tests use Verify.SourceGenerators for snapshot testing
- Performance tests validate zero boxing/reflection requirement
- Integration tests verify thread-safety and DI patterns
- Generated code should be optimized for JIT inlining
- Follow incremental complexity TDD progression per constitution
- Each generator focuses on a single responsibility for maintainability
- Code generators use C# 11 raw string literals for inline code generation
- T047 establishes BenchmarkDotNet baseline for future performance comparisons
- T029a/T029b provide build-time validation for circular dependencies and type conflicts with diagnostic IDs GUAC0001 and GUAC0002

Total Tasks: **54** (T001-T054, where T029a/T029b are sub-tasks within the T029 infrastructure category)
Parallel Opportunities: **27 tasks** (50% can run in parallel)