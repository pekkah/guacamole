# Tasks: Performance Baseline Benchmarks

> **Reconciliation (2026-11-08, #45)**: The benchmarks were implemented in #7 (`benchmarks/Guacamole.Benchmarks`, `tests/Guacamole.Benchmarks.Tests`) but this checklist was never updated. They target `src/Guacamole` (net11.0) and use BenchmarkDotNet defaults: the custom `BenchmarkConfig`, hardware normalization and variance validation were not built, and two validation tests are placeholders. Status markers: see [specs/README.md](../README.md).

**Feature**: 009-benchmarks-we-need
**Input**: Design documents from `/specs/009-benchmarks-we-need/`
**Prerequisites**: plan.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → Tech stack: C# 13 / .NET 10.0 RC1, BenchmarkDotNet, XUnit v3
   → Structure: Single benchmark project under benchmarks/ folder
2. Load design documents:
   → data-model.md: BenchmarkScenario, ScenarioBuilder, BenchmarkResult, etc.
   → contracts/benchmark-execution-contract.md: 3 benchmark types + validation tests
   → research.md: BenchmarkDotNet configuration, hardware normalization
   → quickstart.md: Integration test scenarios
3. Generate tasks by category:
   → Setup: Create projects, install dependencies, configure solution
   → Tests: Contract tests, scenario builder tests, validation tests
   → Infrastructure: ScenarioBuilders, BenchmarkConfig, normalization
   → Benchmarks: Simple, Complex, Caching benchmark classes
   → Integration: End-to-end validation, quickstart verification
4. Apply TDD ordering:
   → Setup → Tests → Infrastructure → Benchmarks → Integration
   → Mark [P] for independent file tasks
5. Validate: All contracts tested, all entities implemented
6. SUCCESS - Ready for execution
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Paths are relative to repository root (`/home/pekkah/p/guacamole/`)

---

## Phase 3.1: Setup & Project Structure

- [x] **T001** Create `benchmarks/Guacamole.Benchmarks/` project directory structure
  - Create: `benchmarks/Guacamole.Benchmarks/`
  - Create: `benchmarks/Guacamole.Benchmarks/Infrastructure/`
  - Create: `benchmarks/Guacamole.Benchmarks/Infrastructure/TestScenarios/`
  - Create: `tests/Guacamole.Benchmarks.Tests/`

- [x] **T002** Initialize `Guacamole.Benchmarks.csproj` as console application — done: targets `net11.0` and references `src/Guacamole/Guacamole.csproj` (#14, #27)
  - File: `benchmarks/Guacamole.Benchmarks/Guacamole.Benchmarks.csproj`
  - Target: `net10.0`
  - Output: `Exe` (console app for BenchmarkDotNet)
  - Add project reference: `../../src/Guacamole.Core/Guacamole.Core.csproj`

- [x] **T003** [P] Install BenchmarkDotNet NuGet package
  - Project: `benchmarks/Guacamole.Benchmarks/Guacamole.Benchmarks.csproj`
  - Package: `BenchmarkDotNet` (latest stable)
  - Command: `dotnet add package BenchmarkDotNet`

- [x] **T004** [P] Initialize `Guacamole.Benchmarks.Tests.csproj` as test project
  - File: `tests/Guacamole.Benchmarks.Tests/Guacamole.Benchmarks.Tests.csproj`
  - Target: `net10.0`
  - Add package: `xunit` (v3)
  - Add package: `xunit.runner.visualstudio`
  - Add project reference: `../../benchmarks/Guacamole.Benchmarks/Guacamole.Benchmarks.csproj`

- [x] **T005** Add benchmark projects to solution
  - Solution: `guacamole.sln`
  - Add: `benchmarks/Guacamole.Benchmarks/Guacamole.Benchmarks.csproj`
  - Add: `tests/Guacamole.Benchmarks.Tests/Guacamole.Benchmarks.Tests.csproj`
  - Command: `dotnet sln add [project-path]`

---

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3

**CRITICAL: These tests MUST be written and MUST FAIL before ANY implementation**

### Scenario Builder Tests

- [x] **T006** [P] Create `ScenarioBuilderTests.cs` with simple scenario validation
  - File: `tests/Guacamole.Benchmarks.Tests/ScenarioBuilderTests.cs`
  - Test: `SimpleScenario_Creates5NodeLinearGraph()`
  - Assert: Graph has exactly 5 nodes
  - Assert: Graph has linear topology (each node has 1 dependency except root)
  - Assert: Max depth = 4 (5 nodes in chain)
  - **Expected**: Test FAILS (no SimpleScenario class exists yet)

- [x] **T007** [P] Add complex scenario validation test
  - File: `tests/Guacamole.Benchmarks.Tests/ScenarioBuilderTests.cs`
  - Test: `ComplexScenario_Creates21NodeTreeWithDepth5()`
  - Assert: Graph has ≥20 nodes (specification: 21)
  - Assert: Graph has ≥5 dependency levels
  - Assert: Scenario.IsComplex = true
  - **Expected**: Test FAILS (no ComplexScenario class exists yet)

- [x] **T008** [P] Add scenario determinism test
  - File: `tests/Guacamole.Benchmarks.Tests/ScenarioBuilderTests.cs`
  - Test: `ScenarioBuilder_ProducesDeterministicResults()`
  - Build same scenario twice with same seed
  - Assert: Both graphs have identical structure
  - Assert: Node IDs match between runs
  - **Expected**: Test FAILS (no ScenarioBuilder exists yet)

### Benchmark Validation Tests (Contract Tests)

- [ ] **T009** [P] Create `BenchmarkValidationTests.cs` with benchmark execution smoke test — open: `AllBenchmarksExecuteWithoutErrors` only checks via reflection that `Program.Main` exists; no dry run
  - File: `tests/Guacamole.Benchmarks.Tests/BenchmarkValidationTests.cs`
  - Test: `AllBenchmarks_CompileAndExecute()`
  - Mock: Call `Program.Main(new[] { "--filter", "*", "--job", "dry" })`
  - Assert: Exit code = 0 (dry run succeeds)
  - **Expected**: Test FAILS (no Program.cs or benchmark classes exist yet)

- [ ] **T010** [P] Add variance threshold validation test — open: `VarianceWithinAcceptableRange_PlaceholderTest` is a placeholder
  - File: `tests/Guacamole.Benchmarks.Tests/BenchmarkValidationTests.cs`
  - Test: `BenchmarkResults_MeetVarianceThreshold()`
  - Load mock benchmark results JSON
  - Calculate: variance = StandardDeviation / Mean
  - Assert: variance ≤ 0.10 for all results
  - **Expected**: Test FAILS (no result parsing logic exists yet)

- [ ] **T011** [P] Add caching benefit validation test — open: `CachingBenchmark_WarmFasterThanCold_PlaceholderTest` is a placeholder
  - File: `tests/Guacamole.Benchmarks.Tests/BenchmarkValidationTests.cs`
  - Test: `CachingBenchmark_WarmFasterThanCold()`
  - Mock cold cache time and warm cache time
  - Calculate improvement: ((cold - warm) / cold) * 100
  - Assert: improvement ≥ 50%
  - **Expected**: Test FAILS (no caching benchmark exists yet)

---

## Phase 3.3: Infrastructure Implementation (ONLY after tests failing)

### Data Model & Configuration

- [x] **T012** [P] Create `BenchmarkScenario.cs` record type
  - File: `benchmarks/Guacamole.Benchmarks/Infrastructure/BenchmarkScenario.cs`
  - Properties: Name, NodeCount, DepthLevels, GraphStructure (enum), IsComplex (derived)
  - Validation: NodeCount > 0, DepthLevels > 0
  - IsComplex logic: NodeCount ≥ 20 AND DepthLevels ≥ 5

- [ ] **T013** [P] Create `HardwareBaseline.cs` record type — open: not implemented
  - File: `benchmarks/Guacamole.Benchmarks/Infrastructure/HardwareBaseline.cs`
  - Properties: CpuModel, CpuScore, MemoryGB, OsVersion, LastUpdated
  - Validation: CpuScore > 0, MemoryGB > 0
  - Add default baseline constant (reference hardware)

- [ ] **T014** [P] Create `BenchmarkConfig.cs` with hardware normalization — open: not implemented; benchmarks use BenchmarkDotNet defaults plus `[MemoryDiagnoser]`
  - File: `benchmarks/Guacamole.Benchmarks/Infrastructure/BenchmarkConfig.cs`
  - Extend: `ManualConfig` from BenchmarkDotNet
  - Configure: `[MemoryDiagnoser]`, `[SimpleJob(warmupCount: 3, targetCount: 10)]`
  - Add: `HardwareBaseline Baseline` property
  - Add: Custom exporter for normalized results

### Scenario Builders

- [x] **T015** [P] Implement `SimpleScenario.cs` builder — done: returns `(IncrementalValueProvider<int>, BenchmarkScenario)`
  - File: `benchmarks/Guacamole.Benchmarks/Infrastructure/TestScenarios/SimpleScenario.cs`
  - Build: 5-node linear computation graph
  - Structure: Input → Compute1 → Compute2 → Compute3 → Output
  - Use: Existing Guacamole.Core types (ComputationGraph, IComputationNode)
  - Return: Tuple<ComputationGraph, BenchmarkScenario>
  - **Verify**: T006 test now passes

- [x] **T016** [P] Implement `ComplexScenario.cs` builder — done: returns `(IncrementalValueProvider<int>, BenchmarkScenario)`
  - File: `benchmarks/Guacamole.Benchmarks/Infrastructure/TestScenarios/ComplexScenario.cs`
  - Build: 21-node tree with 5 dependency levels
  - Structure:
    - Root: 5 input nodes
    - L1: 5 compute nodes (1 dependency each)
    - L2: 5 compute nodes (2 dependencies each)
    - L3: 3 compute nodes (3 dependencies each)
    - L4: 2 compute nodes (cross-dependencies)
    - L5: 1 output node (aggregation)
  - Use deterministic node IDs/names
  - Return: Tuple<ComputationGraph, BenchmarkScenario>
  - **Verify**: T007 test now passes

---

## Phase 3.4: Benchmark Implementation

### Core Benchmark Classes

- [x] **T017** Implement `Program.cs` BenchmarkDotNet runner — done: `Main` returns `void`; artifacts go to `benchmarks/reports/`
  - File: `benchmarks/Guacamole.Benchmarks/Program.cs`
  - Entry point: `static int Main(string[] args)`
  - Use: `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args)`
  - Return exit code: 0 for success, non-zero for failure
  - **Verify**: T009 test now passes (dry run succeeds)

- [x] **T018** [P] Implement `SimpleProviderBenchmarks.cs` — done: without `[Config(typeof(BenchmarkConfig))]`
  - File: `benchmarks/Guacamole.Benchmarks/SimpleProviderBenchmarks.cs`
  - Attribute: `[MemoryDiagnoser]`, `[Config(typeof(BenchmarkConfig))]`
  - Setup: `[GlobalSetup]` - Build simple scenario graph
  - Benchmark: `[Benchmark]` `MeasureBasicProviderExecution()`
  - Execute: Query from Input to Output node
  - Cleanup: `[GlobalCleanup]` - Dispose resources

- [x] **T019** [P] Implement `ComplexGraphBenchmarks.cs` — done: without `[Config(typeof(BenchmarkConfig))]`
  - File: `benchmarks/Guacamole.Benchmarks/ComplexGraphBenchmarks.cs`
  - Attribute: `[MemoryDiagnoser]`, `[Config(typeof(BenchmarkConfig))]`
  - Setup: `[GlobalSetup]` - Build complex scenario (21 nodes, 5 levels)
  - Benchmark: `[Benchmark]` `MeasureComplexScenarioExecution()`
  - Execute: Full graph traversal from roots to output
  - Assert: No stack overflow, all nodes execute

- [x] **T020** Implement `CachingBenchmarks.cs` with cold/warm comparison — done: the cold benchmark clears the cache inside the benchmark instead of `[IterationSetup]`
  - File: `benchmarks/Guacamole.Benchmarks/CachingBenchmarks.cs`
  - Attribute: `[MemoryDiagnoser]`, `[Config(typeof(BenchmarkConfig))]`
  - Setup: `[GlobalSetup]` - Build reusable graph
  - Iteration setup: `[IterationSetup]` - Clear cache for cold run
  - Benchmark 1: `[Benchmark]` `CompareCachedVsUncached_Cold()` - First execution (empty cache)
  - Benchmark 2: `[Benchmark]` `CompareCachedVsUncached_Warm()` - Second execution (populated cache)
  - Measure: Both execution times
  - Report: Improvement percentage in results
  - **Verify**: T011 test now passes (warm < cold)

---

## Phase 3.5: Result Processing & Normalization

- [ ] **T021** Implement `NormalizedExporter.cs` for hardware-adjusted results — open: not implemented
  - File: `benchmarks/Guacamole.Benchmarks/Infrastructure/NormalizedExporter.cs`
  - Implement: `IExporter` from BenchmarkDotNet
  - Extract: Current hardware CPU score from environment
  - Calculate: `normalized_time = raw_time * (baseline_score / current_score)`
  - Output: Append normalized column to results
  - Log: Normalization factor and baseline specs

- [ ] **T022** [P] Add result validation in `BenchmarkConfig.cs` — open: not implemented
  - File: `benchmarks/Guacamole.Benchmarks/Infrastructure/BenchmarkConfig.cs`
  - Add: Custom validator checking StandardDeviation / Mean ≤ 0.10
  - Warn: Log warning if variance exceeds threshold
  - Fail: Return non-zero exit code if critical benchmarks fail threshold
  - **Verify**: T010 test now passes

---

## Phase 3.6: Integration & Validation

- [x] ~~**T023** Create quickstart validation script~~ — superseded: the commands are documented in the README "Benchmarking" section; no script was added
  - File: `benchmarks/run-benchmarks.sh` (or `.ps1` for Windows)
  - Commands:
    1. `cd benchmarks/Guacamole.Benchmarks`
    2. `dotnet build --configuration Release`
    3. `dotnet run --configuration Release`
  - Check: Exit code = 0
  - Output: Link to results in `BenchmarkDotNet.Artifacts/results/`

- [x] **T024** Run all tests to verify TDD cycle complete — done: runs in CI as part of `dotnet test` (T010/T011 are placeholders)
  - Command: `dotnet test tests/Guacamole.Benchmarks.Tests/`
  - Expected: All tests pass (green)
  - Verify: T006-T011 all passing
  - Verify: Benchmark classes exist and functional

- [x] **T025** Execute benchmarks and validate baseline establishment — done: reports in `benchmarks/reports/results/` (HTML, CSV, Markdown); StdDev/Mean ≈ 1%, warm 195 ns vs cold 659 ns
  - Command: `dotnet run --project benchmarks/Guacamole.Benchmarks/ --configuration Release`
  - Verify: All 3 benchmark categories execute
  - Verify: Results show ≤10% variance
  - Verify: Warm cache faster than cold cache
  - Verify: HTML/JSON/Markdown reports generated

- [ ] **T026** Validate quickstart.md manual steps — open: `quickstart.md` still points at `BenchmarkDotNet.Artifacts/results/` and JSON reports; `Program.cs` writes to `benchmarks/reports/`
  - Follow: `specs/009-benchmarks-we-need/quickstart.md`
  - Execute: Steps 1-7 as documented
  - Verify: All acceptance scenarios validated
  - Verify: Results match expected output format
  - Document: Any deviations or improvements needed

---

## Phase 3.7: Polish & Documentation

- [x] **T027** [P] Add XML documentation comments to public APIs — done: benchmark classes and scenario types have XML summaries (`Program.cs` does not)
  - Files: All benchmark classes, ScenarioBuilders, BenchmarkConfig
  - Document: Purpose, parameters, expected behavior
  - Document: Variance thresholds, normalization approach

- [x] **T028** [P] Update solution README with benchmark section — done: README "Benchmarking" section (commands only)
  - File: `README.md` (repository root)
  - Add: "Running Benchmarks" section
  - Link: quickstart.md for detailed instructions
  - Include: Sample output and interpretation guide

- [ ] **T029** Run `dotnet format` on all new code — open: `dotnet format --verify-no-changes` reports line-ending (and a few whitespace) differences repo-wide: `.editorconfig` says `end_of_line = crlf` while `.gitattributes` checks out LF
  - Command: `dotnet format benchmarks/Guacamole.Benchmarks/`
  - Command: `dotnet format tests/Guacamole.Benchmarks.Tests/`
  - Verify: Code style matches constitution requirements

- [x] **T030** Commit baseline results to git (optional) — done: `benchmarks/reports/results/` instead of `benchmarks/baseline-results.json`
  - File: `benchmarks/baseline-results.json`
  - Content: Initial benchmark run results
  - Purpose: Historical comparison for future regression detection
  - Note: Future enhancement, may be deferred

---

## Dependencies

### Critical Path (Must Complete in Order)
```
T001 → T002 → T003 → T004 → T005 (Setup)
  ↓
T006, T007, T008 (Scenario tests - must fail first)
  ↓
T009, T010, T011 (Benchmark validation tests - must fail first)
  ↓
T012, T013, T014 (Data models & config)
  ↓
T015, T016 (Scenario builders - makes T006-T008 pass)
  ↓
T017 (Program.cs - makes T009 pass)
  ↓
T018, T019, T020 (Benchmark implementations - makes T010-T011 pass)
  ↓
T021, T022 (Result processing)
  ↓
T023, T024, T025, T026 (Integration validation)
  ↓
T027, T028, T029, T030 (Polish)
```

### Parallel Execution Opportunities

**Group 1: Setup (after T001)**
```bash
# Can run in parallel after directory structure exists
dotnet task T002 &  # Create main csproj
dotnet task T004 &  # Create test csproj
wait
```

**Group 2: Package Installation (after T002, T004)**
```bash
# Independent package installations
dotnet task T003 &  # Install BenchmarkDotNet
wait
dotnet task T005   # Add to solution
```

**Group 3: Test Creation (after T005)**
```bash
# Independent test files
dotnet task T006 &  # SimpleScenario test
dotnet task T007 &  # ComplexScenario test
dotnet task T008 &  # Determinism test
dotnet task T009 &  # Smoke test
dotnet task T010 &  # Variance test
dotnet task T011 &  # Caching test
wait
```

**Group 4: Data Models (after tests written)**
```bash
# Independent record types
dotnet task T012 &  # BenchmarkScenario
dotnet task T013 &  # HardwareBaseline
dotnet task T014 &  # BenchmarkConfig
wait
```

**Group 5: Scenario Builders (after T012-T014)**
```bash
# Independent builder files
dotnet task T015 &  # SimpleScenario
dotnet task T016 &  # ComplexScenario
wait
```

**Group 6: Benchmarks (after T017)**
```bash
# Independent benchmark classes
dotnet task T018 &  # SimpleProviderBenchmarks
dotnet task T019 &  # ComplexGraphBenchmarks
# T020 runs after T018, T019 (reuses infrastructure)
wait
dotnet task T020    # CachingBenchmarks
```

**Group 7: Polish (after all implementation)**
```bash
# Independent documentation tasks
dotnet task T027 &  # XML docs
dotnet task T028 &  # README
wait
dotnet task T029    # Format (must be last before commit)
```

---

## Validation Checklist

Before marking feature complete, verify:

- [x] All tests in T006-T011 pass (green) — done: green, but T010/T011 are placeholders
- [x] All benchmark classes compile and execute
- [ ] Variance threshold ≤10% enforced and validated — open: see T010/T022
- [x] Warm cache demonstrably faster than cold cache (improvement ≥50%) — done: 195 ns vs 659 ns (≈70%)
- [ ] Hardware normalization calculates and reports correctly — open: see T013/T014/T021
- [ ] quickstart.md steps execute successfully — open: see T026
- [x] Solution builds without errors: `dotnet build`
- [x] All tests pass: `dotnet test`
- [x] Benchmarks run: `dotnet run --project benchmarks/Guacamole.Benchmarks/ -c Release`
- [x] Results exported to multiple formats (HTML, JSON, Markdown) — done: HTML, CSV and Markdown (no JSON)
- [ ] Code formatted per constitution: `dotnet format` — open: see T029

---

## Constitutional Compliance Notes

### TDD Progression (Principle V)
- **T006-T011**: Tests MUST be written first and MUST fail
- **T012-T022**: Implementation makes tests pass incrementally
- Progression: Basic (T015-T016) → Complex (T019) → Performance (T020) → Validation (T023-T026)

### Performance as Correctness (Principle IV)
- **T010**: Variance threshold enforcement (≤10%)
- **T022**: Automated validation in BenchmarkConfig
- **T025**: Baseline establishment verifies O(changes) starting point

### Modern C# (Principle VI)
- **T012-T013**: Record types for immutable data
- **T014**: BenchmarkDotNet attributes (.NET 10 features)
- **T015-T016**: Uses existing Guacamole.Core DI infrastructure

### Library Architecture Exception
- **T002**: Console executable justified for BenchmarkDotNet requirement
- **Documented**: Complexity tracking in plan.md
- **Compliant**: Not included in distributed NuGet packages

---

**Total Tasks**: 30
**Estimated Effort**: 15-20 hours (with TDD discipline)
**Parallel Opportunities**: 16 tasks marked [P] can run concurrently in groups
**Critical Path Length**: 12 sequential task groups
