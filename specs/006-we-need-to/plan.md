# Implementation Plan: Incremental Value Providers

**Branch**: `006-we-need-to` | **Date**: 2025-01-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/home/pekkah/p/guacamole/specs/006-we-need-to/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → ✅ COMPLETE: Loaded specification with comprehensive clarifications
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → ✅ COMPLETE: No unresolved NEEDS CLARIFICATION markers found
   → Detect Project Type from file system structure or context (web=frontend+backend, mobile=app+api)
   → ✅ COMPLETE: Single project type - core framework library
   → Set Structure Decision based on project type
3. Fill the Constitution Check section based on the content of the constitution document.
   → ✅ COMPLETE: Constitution v2.3.1 analysis complete
4. Evaluate Constitution Check section below
   → ✅ COMPLETE: All constitutional requirements aligned
   → Update Progress Tracking: Initial Constitution Check
5. Execute Phase 0 → research.md
   → ✅ COMPLETE: Research document generated
6. Execute Phase 1 → contracts, data-model.md, quickstart.md, CLAUDE.md
   → ✅ COMPLETE: All Phase 1 artifacts generated
7. Re-evaluate Constitution Check section
   → ✅ COMPLETE: No new violations detected
   → Update Progress Tracking: Post-Design Constitution Check
8. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
   → ✅ COMPLETE: Task generation strategy documented
9. STOP - Ready for /tasks command
   → ✅ READY: /tasks command can proceed
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Implement IncrementalValueProvider<T> and IncrementalValuesProvider<T> structs as the core incremental computation engine for the Guacamole framework. These immutable, thread-safe structures will enable efficient pipeline-based data transformations with automatic caching, LRU eviction, and comprehensive debugging support. Implementation follows strict TDD practices with unit tests, leverages .NET 10 features, and references Roslyn documentation for behavioral patterns while adapting for framework-specific needs.

## Technical Context
**Language/Version**: C# 13 / .NET 10 RC1
**Primary Dependencies**: Microsoft.Extensions.DependencyInjection, System.Collections.Immutable
**Storage**: In-memory caching with LRU eviction policy
**Testing**: XUnit v3 with incremental complexity progression (TDD)
**Target Platform**: .NET 10 library (cross-platform)
**Project Type**: Single framework library project
**Performance Goals**: <1ms cached access, <10ms incremental updates, O(changes) complexity
**Constraints**: <2x memory overhead, immutable design, thread-safe without synchronization
**Scale/Scope**: Core framework component supporting enterprise compiler infrastructure

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Incremental Computation: All providers designed for O(changes) complexity with automatic dependency tracking and memoization
- [x] Query-Driven: Pipeline transformations as composable queries with explicit inputs/outputs
- [x] Deterministic: Immutable structures with deterministic transformations and caching
- [x] Performance Target: Sub-millisecond cached access, <10ms incremental, LRU memory management
- [x] TDD Compliance: Unit tests with incremental complexity (basic → edge → incremental → performance)
- [x] Modern C#: .NET 10 features including immutable records, spans, source generators
- [x] Dependency Injection: Core framework library with DI service registration patterns
- [x] Library Architecture: Pure library with public API surface, no executables
- [x] Package API: Minimal public API with semantic versioning compliance

## Project Structure

### Documentation (this feature)
```
specs/006-we-need-to/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
src/
├── Guacamole.Core/                    # Core incremental computation engine
│   ├── Providers/                     # IncrementalValueProvider implementations
│   │   ├── IncrementalValueProvider.cs
│   │   ├── IncrementalValuesProvider.cs
│   │   └── Extensions/                # Extension methods for transformations
│   ├── Caching/                       # LRU cache implementation and interfaces
│   ├── Pipeline/                      # Pipeline execution and dependency tracking
│   ├── Diagnostics/                   # Visualization and metrics collection
│   └── ServiceRegistration/           # DI container extensions
└── Guacamole.SourceGenerators/        # Source generators for provider registration

tests/
├── Guacamole.Core.Tests/              # Unit tests with TDD progression
│   ├── Providers/                     # Provider behavior tests
│   ├── Caching/                       # Cache behavior and eviction tests
│   ├── Pipeline/                      # Pipeline composition tests
│   └── Integration/                   # End-to-end scenarios
└── Guacamole.Performance.Tests/       # BenchmarkDotNet performance validation
```

**Structure Decision**: Single project structure chosen as this is a core framework library. The main implementation resides in `src/Guacamole.Core/` with the provider implementations as the primary public API surface. Source generators in a separate package follow constitutional package distribution standards.

## Phase 0: Outline & Research

✅ **Research Complete** - Generated `research.md` with:

**Key Research Areas Covered**:
1. **Roslyn IIncrementalGenerator Patterns**: Analyzed Microsoft documentation for behavioral guidance
2. **Immutable Design Patterns**: Researched .NET 10 record types and immutable collection patterns
3. **LRU Cache Implementation**: Evaluated System.Collections.Concurrent and custom implementations
4. **Pipeline Composition**: Studied functional composition patterns in C#
5. **TDD Incremental Complexity**: Researched XUnit v3 patterns for incremental test development
6. **Performance Optimization**: Analyzed span usage and zero-allocation patterns

**Output**: research.md with all technical unknowns resolved

## Phase 1: Design & Contracts

✅ **Design Complete** - Generated all Phase 1 artifacts:

1. **✅ data-model.md**: Core entities and relationships
   - IncrementalValueProvider<T> and IncrementalValuesProvider<T> structures
   - Pipeline transformation operations and caching interfaces
   - Result<T, Error> type system for error handling

2. **✅ contracts/**: API interface contracts
   - Core provider interfaces and extension method contracts
   - Caching and eviction policy interfaces
   - Diagnostics and visualization contracts

3. **✅ quickstart.md**: Developer experience validation
   - Basic provider creation and transformation scenarios
   - Error handling and debugging workflows
   - Performance validation steps

4. **✅ CLAUDE.md**: Updated agent context
   - Added .NET 10 + Microsoft.Extensions.DependencyInjection dependency
   - Included incremental value provider feature context
   - Preserved existing manual additions

**Output**: Complete design documentation ready for task generation

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
- Load `.specify/templates/tasks-template.md` as base
- Generate tasks from TDD incremental complexity progression
- Core provider structure tasks [P] (parallel implementation)
- Unit test tasks following RED-GREEN-REFACTOR cycle
- Integration tests for pipeline composition
- Performance validation with BenchmarkDotNet

**Ordering Strategy**:
- TDD order: Unit tests before implementation
- Constitutional order: Basic → Edge → Incremental → Performance
- Dependency order: Core types → Extensions → Diagnostics → Integration
- Mark [P] for parallel execution (independent components)

**Estimated Output**: 20-25 numbered, ordered tasks following TDD progression

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)
**Phase 4**: Implementation (execute tasks.md following constitutional TDD principles)
**Phase 5**: Validation (run tests, execute quickstart.md, performance benchmarks)

## Complexity Tracking
*No constitutional violations detected - all requirements aligned with Guacamole principles*

No complexity deviations requiring justification.

## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [x] Phase 2: Task planning complete (/plan command - describe approach only)
- [x] Phase 3: Tasks generated (/tasks command) - 35 tasks ready for execution
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented (none required)

---
*Based on Constitution v2.3.1 - See `.specify/memory/constitution.md`*