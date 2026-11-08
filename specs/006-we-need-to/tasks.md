# Tasks: Incremental Value Providers

> **Reconciliation (2026-11-08, #45)**: `src/Guacamole.Core` and `tests/Guacamole.Core.Tests` were merged into `src/Guacamole` and `tests/Guacamole.Tests` (#14), so the implementation lives at the paths given in the notes. Performance work moved to `benchmarks/Guacamole.Benchmarks` (spec 009); there is no `tests/Guacamole.Performance.Tests` project. Status markers: see [specs/README.md](../README.md).
>
> **Update (#53)**: T031-T033 are covered by `IncrementalUpdateBenchmarks`, `CollectionChangeBenchmarks` and `MemoryOverheadBenchmarks` in `benchmarks/Guacamole.Benchmarks`; results and interpretation are in [docs/performance-guide.md](../../docs/performance-guide.md#benchmark-results).

**Input**: Design documents from `/home/pekkah/p/guacamole/specs/006-we-need-to/`
**Prerequisites**: plan.md (✅), research.md (✅), data-model.md (✅), contracts/ (✅)

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → ✅ COMPLETE: Loaded C# 13/.NET 10 RC1, TDD, immutable design
2. Load optional design documents:
   → ✅ data-model.md: 8 core entities extracted → model tasks
   → ✅ contracts/: 2 contract files → test tasks
   → ✅ research.md: TDD progression, performance targets
3. Generate tasks by category:
   → Setup: project structure, dependencies, tooling
   → Tests: contract tests, unit tests (TDD RED-GREEN-REFACTOR)
   → Core: providers, caching, pipeline, diagnostics
   → Integration: service registration, end-to-end validation
   → Polish: performance tests, documentation
4. Apply task rules:
   → Different files = mark [P] for parallel execution
   → Same file = sequential (no [P])
   → Tests before implementation (constitutional TDD)
5. Number tasks sequentially (T001, T002...)
6. Generate dependency graph following constitutional complexity: Basic → Edge → Incremental → Performance
7. Create parallel execution examples
8. SUCCESS: 25 tasks ready for constitutional TDD execution
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
Single project structure from plan.md:
- **Core Library**: `src/Guacamole.Core/` (main implementation)
- **Source Generators**: `src/Guacamole.SourceGenerators/` (parallel package)
- **Unit Tests**: `tests/Guacamole.Core.Tests/` (TDD progression)
- **Performance Tests**: `tests/Guacamole.Performance.Tests/` (benchmarks)

## Phase 3.1: Setup & Infrastructure
- [x] T001 Create project structure per plan.md with src/Guacamole.Core/ and tests/Guacamole.Core.Tests/
- [x] T002 Initialize C# 13/.NET 10 RC1 projects with Microsoft.Extensions.DependencyInjection and System.Collections.Immutable dependencies
- [x] T003 [P] Configure XUnit v3 testing framework in tests/Guacamole.Core.Tests/Guacamole.Core.Tests.csproj
- [x] T004 [P] Configure BenchmarkDotNet in tests/Guacamole.Performance.Tests/Guacamole.Performance.Tests.csproj
- [x] T005 [P] Setup EditorConfig and linting rules for C# 13 conventions

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3
**CRITICAL: These tests MUST be written and MUST FAIL before ANY implementation**

### Core Entity Tests (Basic → Edge → Incremental → Performance)
- [x] T006 [P] Basic IncrementalValueProvider<T> struct tests in tests/Guacamole.Core.Tests/Providers/IncrementalValueProviderTests.cs
- [x] T007 [P] Basic IncrementalValuesProvider<T> struct tests in tests/Guacamole.Core.Tests/Providers/IncrementalValuesProviderTests.cs
- [x] T008 [P] ComputationGraph basic functionality tests in tests/Guacamole.Core.Tests/Pipeline/ComputationGraphTests.cs
- [x] T009 [P] Result<TValue, TError> discriminated union tests in tests/Guacamole.Core.Tests/Common/ResultTests.cs
- [x] T010 [P] LRUCache<TKey, TValue> basic tests in tests/Guacamole.Core.Tests/Caching/LRUCacheTests.cs

### Contract Tests (API Compliance)
- [x] T011 [P] Core providers contract tests from contracts/core-providers.yaml in tests/Guacamole.Core.Tests/Contracts/CoreProvidersContractTests.cs
- [x] T012 [P] Caching interfaces contract tests from contracts/caching-interfaces.yaml in tests/Guacamole.Core.Tests/Contracts/CachingContractTests.cs

### Extension Method Tests
- [x] T013 [P] IncrementalValueProvider extension methods tests (Select, Where, Combine) in tests/Guacamole.Core.Tests/Providers/Extensions/ValueProviderExtensionTests.cs
- [x] T014 [P] IncrementalValuesProvider extension methods tests (SelectMany, Collect) in tests/Guacamole.Core.Tests/Providers/Extensions/ValuesProviderExtensionTests.cs
- [x] T015 [P] Diagnostic extensions tests (WithTrackingName, WithTags) in tests/Guacamole.Core.Tests/Diagnostics/DiagnosticExtensionTests.cs

## Phase 3.3: Core Implementation (ONLY after tests are failing)

### Core Entities
- [x] T016 [P] Result<TValue, TError> discriminated union in src/Guacamole.Core/Common/Result.cs — done: `src/Guacamole/Result.cs`
- [x] T017 [P] ProviderMetadata structure in src/Guacamole.Core/Diagnostics/ProviderMetadata.cs — done: `src/Guacamole/ProviderMetadata.cs`
- [x] T018 [P] IComputationNode interface hierarchy in src/Guacamole.Core/Pipeline/IComputationNode.cs — done: `src/Guacamole/Pipeline/IComputationNode.cs`
- [x] T019 LRUCache<TKey, TValue> implementation in src/Guacamole.Core/Caching/LRUCache.cs — done: `src/Guacamole/Caching/LRUCache.cs`
- [x] T020 ComputationGraph with dependency tracking in src/Guacamole.Core/Pipeline/ComputationGraph.cs — done: `src/Guacamole/Pipeline/ComputationGraph.cs`

### Provider Structures
- [x] T021 IncrementalValueProvider<T> readonly record struct in src/Guacamole.Core/Providers/IncrementalValueProvider.cs — done: `src/Guacamole/IncrementalValueProvider.cs`
- [x] T022 IncrementalValuesProvider<T> readonly record struct in src/Guacamole.Core/Providers/IncrementalValuesProvider.cs — done: `src/Guacamole/IncrementalValueProvider.cs`

### Extension Methods (Pipeline Transformations)
- [x] T023 IncrementalValueProvider extension methods (Select, Where, Combine) in src/Guacamole.Core/Providers/Extensions/IncrementalValueProviderExtensions.cs — done: `src/Guacamole/IncrementalValueProviderExtensions.cs`
- [x] T024 IncrementalValuesProvider extension methods (SelectMany, Collect) in src/Guacamole.Core/Providers/Extensions/IncrementalValuesProviderExtensions.cs — done: `src/Guacamole/IncrementalValuesProviderExtensions.cs`
- [x] T025 Diagnostic extensions (WithTrackingName, WithTags) in src/Guacamole.Core/Diagnostics/DiagnosticExtensions.cs — done: `src/Guacamole/DiagnosticProviderExtensions.cs`

## Phase 3.4: Integration & Service Registration
- [x] T026 ServiceRegistration extensions for DI container in src/Guacamole.Core/ServiceRegistration/ServiceCollectionExtensions.cs — done: `src/Guacamole/ServiceCollectionExtensions.cs` (`AddGuacamoleCore`)
- [x] T027 [P] Integration test: Basic provider pipeline in tests/Guacamole.Core.Tests/Integration/BasicPipelineIntegrationTests.cs — done: `tests/Guacamole.Tests/Providers/IncrementalComputationEngineTests.cs`
- [x] T028 [P] Integration test: Error handling through pipeline in tests/Guacamole.Core.Tests/Integration/ErrorHandlingIntegrationTests.cs — done: `tests/Guacamole.Tests/Providers/CollectionOperatorErrorTests.cs`, `WhereValueSemanticsTests.cs`
- [x] T029 [P] Integration test: Cache invalidation and dependency tracking in tests/Guacamole.Core.Tests/Integration/CacheInvalidationIntegrationTests.cs — done: `tests/Guacamole.Tests/CacheInvalidationTests.cs`, `SourceChangeCachingTests.cs`

## Phase 3.5: Performance & Polish
- [x] T030 [P] Performance tests: <1ms cached access validation in tests/Guacamole.Performance.Tests/CachedAccessBenchmarks.cs — done: `benchmarks/Guacamole.Benchmarks/CachingBenchmarks.cs` (warm ≈ 0.2 µs in `docs/benchmarks/2025-10-baseline.md`)
- [x] T031 [P] Performance tests: <10ms incremental updates validation in tests/Guacamole.Performance.Tests/IncrementalUpdateBenchmarks.cs — done: `benchmarks/Guacamole.Benchmarks/IncrementalUpdateBenchmarks.cs` (`OneSourceChanged`, 16-4,096 sources); one change and read took 12 µs-2.6 ms in a short in-process run, under 10 ms (#53)
- [x] T032 [P] Performance tests: O(changes) complexity validation in tests/Guacamole.Performance.Tests/ComplexityBenchmarks.cs — done: `IncrementalUpdateBenchmarks` (graph size) and `CollectionChangeBenchmarks` (K of N items), plus call-count tests in `tests/Guacamole.Benchmarks.Tests/IncrementalScenarioTests.cs`. Result: recomputation is O(changes) (one leaf lambda per change, K item lambdas for K changes), but wall time is not: verifying the unchanged stages walks the whole graph, and collection stages walk all N items (#53)
- [x] T033 [P] Performance tests: <2x memory overhead validation in tests/Guacamole.Performance.Tests/MemoryOverheadBenchmarks.cs — done: `benchmarks/Guacamole.Benchmarks/MemoryOverheadBenchmarks.cs` compares building and reading a context against computing the results directly. The <2x target is not met: building allocates ~190-240x the direct baseline, and the context retains ~150 B per item versus 8 B per item for the direct results array (#53)
- [x] T034 [P] Update agent context in CLAUDE.md with implementation completion — done: `CLAUDE.md` "Active Technologies" describes the runtime
- [x] ~~T035 Run quickstart.md manual validation scenarios~~ — superseded: this spec has no `quickstart.md`; usage is covered by `docs/quickstart.md` and `examples/` (built in CI)

## Dependencies
**Constitutional TDD Order: Tests → Implementation → Integration → Performance**
- Phase 3.2 (T006-T015) MUST complete before Phase 3.3 (T016-T025)
- T016 (Result) blocks T017-T025 (all other implementations need Result pattern)
- T018 (IComputationNode) blocks T020 (ComputationGraph needs interfaces)
- T019 (LRUCache) and T020 (ComputationGraph) block T021-T022 (providers need caching/graph)
- T021-T022 (core providers) block T023-T025 (extensions need base structures)
- Implementation (T016-T025) before integration (T026-T029)
- Integration before performance (T030-T035)

## Parallel Example
```bash
# Launch Phase 3.2 test creation together (RED phase):
Task: "Basic IncrementalValueProvider<T> struct tests in tests/Guacamole.Core.Tests/Providers/IncrementalValueProviderTests.cs"
Task: "Basic IncrementalValuesProvider<T> struct tests in tests/Guacamole.Core.Tests/Providers/IncrementalValuesProviderTests.cs"
Task: "ComputationGraph basic functionality tests in tests/Guacamole.Core.Tests/Pipeline/ComputationGraphTests.cs"
Task: "Result<TValue, TError> discriminated union tests in tests/Guacamole.Core.Tests/Common/ResultTests.cs"
Task: "LRUCache<TKey, TValue> basic tests in tests/Guacamole.Core.Tests/Caching/LRUCacheTests.cs"

# Launch Phase 3.5 performance validation together:
Task: "Performance tests: <1ms cached access validation in tests/Guacamole.Performance.Tests/CachedAccessBenchmarks.cs"
Task: "Performance tests: <10ms incremental updates validation in tests/Guacamole.Performance.Tests/IncrementalUpdateBenchmarks.cs"
Task: "Performance tests: O(changes) complexity validation in tests/Guacamole.Performance.Tests/ComplexityBenchmarks.cs"
Task: "Performance tests: <2x memory overhead validation in tests/Guacamole.Performance.Tests/MemoryOverheadBenchmarks.cs"
```

## Notes
- **[P] tasks**: Different files, independent implementation, can run in parallel
- **TDD Order**: All tests MUST be written and failing before any implementation
- **Constitutional Compliance**: Follow Basic → Edge → Incremental → Performance complexity progression
- **Immutable Design**: All providers use readonly record structs with value equality
- **Performance Targets**: <1ms cached, <10ms incremental, O(changes) complexity, <2x memory
- **Error Handling**: Result<T, Error> pattern throughout, no exceptions in transformations
- **Thread Safety**: Lock-free immutable design, no synchronization primitives

## Task Generation Rules
*Applied during main() execution*

1. **From Data Model** (8 core entities):
   - IncrementalValueProvider<T> → model + tests [P]
   - IncrementalValuesProvider<T> → model + tests [P]
   - ComputationGraph → implementation + tests [P]
   - Result<TValue, TError> → discriminated union + tests [P]
   - ProviderMetadata → metadata structure + tests [P]
   - IComputationNode → interface hierarchy + tests [P]
   - LRUCache<TKey, TValue> → cache implementation + tests [P]
   - Extension Methods → 3 separate extension files + tests [P]

2. **From Contracts** (2 contract files):
   - core-providers.yaml → contract compliance tests [P]
   - caching-interfaces.yaml → caching contract tests [P]

3. **From Research** (TDD progression):
   - Basic → Edge → Incremental → Performance → Chaos testing
   - XUnit v3 with Theory tests for parameterized scenarios
   - BenchmarkDotNet for constitutional performance validation

4. **Ordering** (Constitutional TDD):
   - Setup → Tests → Models → Extensions → Integration → Performance
   - Dependencies enforce sequential execution within parallel groups

## Validation Checklist
*GATE: Checked by main() before returning*

- [x] All entities from data-model.md have corresponding tests and implementations
- [x] All contracts from contracts/ have test coverage
- [x] All tests come before implementation (constitutional TDD requirement)
- [x] Parallel tasks truly independent (different files, no shared state)
- [x] Each task specifies exact file path for implementation
- [x] No task modifies same file as another [P] task
- [x] Performance targets explicitly tested: <1ms cached, <10ms incremental, O(changes), <2x memory
- [x] Constitutional complexity progression: Basic → Edge → Incremental → Performance
- [x] Immutable design patterns enforced throughout (readonly record structs)
- [x] Result<T, Error> pattern used for all error handling (no exceptions)