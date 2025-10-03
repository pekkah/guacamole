
# Implementation Plan: Performance Baseline Benchmarks

**Branch**: `009-benchmarks-we-need` | **Date**: 2025-10-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/009-benchmarks-we-need/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → If not found: ERROR "No feature spec at {path}"
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Detect Project Type from file system structure or context (web=frontend+backend, mobile=app+api)
   → Set Structure Decision based on project type
3. Fill the Constitution Check section based on the content of the constitution document.
4. Evaluate Constitution Check section below
   → If violations exist: Document in Complexity Tracking
   → If no justification possible: ERROR "Simplify approach first"
   → Update Progress Tracking: Initial Constitution Check
5. Execute Phase 0 → research.md
   → If NEEDS CLARIFICATION remain: ERROR "Resolve unknowns"
6. Execute Phase 1 → contracts, data-model.md, quickstart.md, agent-specific template file (e.g., `CLAUDE.md` for Claude Code, `.github/copilot-instructions.md` for GitHub Copilot, `GEMINI.md` for Gemini CLI, `QWEN.md` for Qwen Code or `AGENTS.md` for opencode).
7. Re-evaluate Constitution Check section
   → If new violations: Refactor design, return to Phase 1
   → Update Progress Tracking: Post-Design Constitution Check
8. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
9. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Establish performance baseline benchmarks for the Guacamole incremental computation framework using BenchmarkDotNet. Create Guacamole.Benchmarks project under benchmarks/ folder to measure: (1) simple provider operations with registered outputs, (2) complex computation scenarios with ≥20 nodes and ≥5 dependency levels, and (3) query performance with caching enabled vs disabled. Benchmarks must be reproducible within ≤10% variance and report normalized measurements relative to reference hardware.

## Technical Context
**Language/Version**: C# 13 / .NET 10.0 RC1
**Primary Dependencies**: BenchmarkDotNet, Microsoft.Extensions.DependencyInjection, existing Guacamole.Core infrastructure
**Storage**: In-memory benchmark results (BenchmarkDotNet standard output formats)
**Testing**: XUnit v3 for benchmark validation, BenchmarkDotNet for performance measurement
**Target Platform**: Cross-platform (.NET 10.0 - Linux/Windows/macOS)
**Project Type**: Single (library benchmark project)
**Performance Goals**:
- Reproducibility: ≤10% variance between runs
- Simple provider operations: baseline measurement for future comparison
- Complex scenarios (≥20 nodes, ≥5 levels): establish O(changes) complexity baseline
- Cache impact: quantify performance difference cached vs uncached queries
**Constraints**:
- Hardware normalization required (CPU/memory specs relative to reference)
- BenchmarkDotNet handles JIT warm-up automatically
- Command-line executable without interactive input
**Scale/Scope**:
- 3-5 benchmark scenarios (simple, complex, cached)
- Complex scenario: ≥20 computation nodes with ≥5 dependency levels
- Baseline establishment (not exhaustive performance testing)

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Incremental Computation: N/A - Benchmarks measure the engine, not part of query execution
- [x] Query-Driven: N/A - Benchmarks use existing query infrastructure but don't add queries
- [x] Deterministic: PASS - Benchmarks use deterministic test scenarios with controlled inputs
- [x] Performance Target: PASS - This feature establishes the baseline for O(changes) verification
- [x] TDD Compliance: PASS - Benchmark validation tests with XUnit v3, BenchmarkDotNet for measurement
- [x] Modern C#: PASS - Uses .NET 10, BenchmarkDotNet attributes, existing DI infrastructure
- [x] Dependency Injection: PASS - Uses existing DI-registered services for benchmark scenarios
- [x] Library Architecture: **DEVIATION** - Benchmarks project includes console executable for BenchmarkDotNet runner (see Complexity Tracking)
- [x] Package API: PASS - Benchmarks not included in distributed NuGet packages per constitution

## Project Structure

### Documentation (this feature)
```
specs/[###-feature]/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
benchmarks/
└── Guacamole.Benchmarks/
    ├── Guacamole.Benchmarks.csproj   # Console app for BenchmarkDotNet
    ├── Program.cs                     # BenchmarkDotNet runner entry point
    ├── SimpleProviderBenchmarks.cs    # Basic provider operation benchmarks
    ├── ComplexGraphBenchmarks.cs      # Complex scenario (≥20 nodes, ≥5 levels)
    ├── CachingBenchmarks.cs           # Cached vs uncached query benchmarks
    └── Infrastructure/
        ├── BenchmarkConfig.cs         # Hardware normalization config
        └── TestScenarios/             # Reusable test graph builders
            ├── SimpleScenario.cs
            └── ComplexScenario.cs

src/
└── Guacamole.Core/                    # Existing - benchmark target
    └── [existing incremental engine]

tests/
└── Guacamole.Benchmarks.Tests/        # Benchmark validation tests
    ├── BenchmarkValidationTests.cs    # Verify benchmarks compile and run
    └── ScenarioBuilderTests.cs        # Test scenario construction logic
```

**Structure Decision**: Single benchmark project under existing `benchmarks/` folder (already present in repo). This is a developer tooling project, not part of the distributed library. Benchmarks use existing `Guacamole.Core` infrastructure to measure its performance characteristics. Validation tests ensure benchmarks remain functional as the engine evolves.

## Phase 0: Outline & Research
1. **Extract unknowns from Technical Context** above:
   - For each NEEDS CLARIFICATION → research task
   - For each dependency → best practices task
   - For each integration → patterns task

2. **Generate and dispatch research agents**:
   ```
   For each unknown in Technical Context:
     Task: "Research {unknown} for {feature context}"
   For each technology choice:
     Task: "Find best practices for {tech} in {domain}"
   ```

3. **Consolidate findings** in `research.md` using format:
   - Decision: [what was chosen]
   - Rationale: [why chosen]
   - Alternatives considered: [what else evaluated]

**Output**: research.md with all NEEDS CLARIFICATION resolved

## Phase 1: Design & Contracts
*Prerequisites: research.md complete*

1. **Extract entities from feature spec** → `data-model.md`:
   - Entity name, fields, relationships
   - Validation rules from requirements
   - State transitions if applicable

2. **Generate API contracts** from functional requirements:
   - For each user action → endpoint
   - Use standard REST/GraphQL patterns
   - Output OpenAPI/GraphQL schema to `/contracts/`

3. **Generate contract tests** from contracts:
   - One test file per endpoint
   - Assert request/response schemas
   - Tests must fail (no implementation yet)

4. **Extract test scenarios** from user stories:
   - Each story → integration test scenario
   - Quickstart test = story validation steps

5. **Update agent file incrementally** (O(1) operation):
   - Run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude`
     **IMPORTANT**: Execute it exactly as specified above. Do not add or remove any arguments.
   - If exists: Add only NEW tech from current plan
   - Preserve manual additions between markers
   - Update recent changes (keep last 3)
   - Keep under 150 lines for token efficiency
   - Output to repository root

**Output**: data-model.md, /contracts/*, failing tests, quickstart.md, agent-specific file

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
- Load `.specify/templates/tasks-template.md` as base
- Generate tasks from Phase 1 artifacts:
  - `research.md` → Infrastructure setup tasks
  - `data-model.md` → Entity/record type creation tasks
  - `contracts/benchmark-execution-contract.md` → Contract test tasks
  - `quickstart.md` → Integration validation tasks

**Task Categories**:

1. **Infrastructure Setup** (from research.md):
   - Create Guacamole.Benchmarks project [P]
   - Install BenchmarkDotNet NuGet package [P]
   - Configure HardwareBaseline settings
   - Create BenchmarkConfig for normalization

2. **Scenario Builders** (from data-model.md):
   - Implement ScenarioBuilder base [P]
   - Create SimpleScenario (5 nodes, linear)
   - Create ComplexScenario (21 nodes, 5 levels, tree)
   - Add XUnit tests for scenario validation

3. **Benchmark Classes** (from contracts):
   - Implement SimpleProviderBenchmarks [P]
   - Implement ComplexGraphBenchmarks [P]
   - Implement CachingBenchmarks (cold/warm comparison)
   - Add contract validation tests

4. **Result Processing** (from data-model.md):
   - Implement NormalizedExporter for hardware normalization
   - Create result validation logic (≤10% variance check)
   - Add result comparison utilities

5. **Integration Validation** (from quickstart.md):
   - Create BenchmarkValidationTests project
   - Implement end-to-end smoke tests
   - Add variance threshold enforcement tests
   - Create quickstart execution script

**Ordering Strategy**:
- TDD progression: Infrastructure → Tests → Implementation
- Dependency order:
  1. Project setup (enables all other tasks)
  2. Scenario builders (needed by benchmarks)
  3. Benchmark classes (use scenarios)
  4. Result processing (consumes benchmark output)
  5. Integration validation (end-to-end verification)
- Mark [P] for parallel execution where independent
- Follow constitutional incremental complexity: basic → edge → performance

**Estimated Output**: 20-25 numbered tasks in tasks.md

**Key Dependencies**:
- Guacamole.Core must exist and be stable (already implemented)
- Microsoft.Extensions.DependencyInjection infrastructure available
- XUnit v3 test framework configured
- .NET 10 RC1 SDK installed

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Console executable (Library Architecture principle) | BenchmarkDotNet requires console host for runner infrastructure | BenchmarkDotNet framework mandates console app entry point for benchmark execution. Library-only approach incompatible with BenchmarkDotNet's design. Constitution explicitly allows this: "Benchmarks and samples MUST NOT be included in distributed packages" - benchmarks are developer tooling, not shipped product |


## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [x] Phase 2: Task planning complete (/plan command - describe approach only)
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS (with justified deviation)
- [x] Post-Design Constitution Check: PASS (no new violations)
- [x] All NEEDS CLARIFICATION resolved (via /clarify)
- [x] Complexity deviations documented (console executable for BenchmarkDotNet)

---
*Based on Constitution v2.3.0 - See `.specify/memory/constitution.md`*
