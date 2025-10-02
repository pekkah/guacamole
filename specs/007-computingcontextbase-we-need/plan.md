
# Implementation Plan: ComputingContextBase

**Branch**: `007-computingcontextbase-we-need` | **Date**: 2025-09-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/home/pekkah/p/guacamole/specs/007-computingcontextbase-we-need/spec.md`

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
ComputingContextBase provides an Entity Framework-style base class that users inherit to create application contexts for incremental computation. Users define properties as IncrementalValueProvider<T> types, and incremental source generators automatically generate the entire context implementation including strongly-typed mutation methods, change tracking, and computation nodes with zero runtime reflection or boxing. The feature enables generic change application from file watchers, UI interactions, or APIs with automatic dependency tracking and O(changes) incremental recomputation through compile-time generated infrastructure.

## Technical Context
**Language/Version**: C# 13 / .NET 10.0 RC1
**Primary Dependencies**: Microsoft.Extensions.DependencyInjection, Microsoft.CodeAnalysis.CSharp (source generators), existing Guacamole.Core infrastructure
**Storage**: In-memory computation graph with LRU caching
**Testing**: XUnit v3 with Verify.SourceGenerators for snapshot testing
**Target Platform**: .NET 10.0 library package (cross-platform)
**Project Type**: Single project - incremental computation framework library
**Performance Goals**: <1ms cached access, <10ms incremental updates, O(changes) complexity, zero boxing/reflection overhead
**Constraints**: <2x memory overhead, thread-safe multiple readers/single writer, fail-fast error handling, compile-time code generation
**Scale/Scope**: Framework component for compiler infrastructure with thousands of incremental value providers

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Incremental Computation: ComputingContextBase integrates with existing IncrementalValueProvider infrastructure for automatic dependency tracking and O(changes) recomputation
- [x] Query-Driven: Context properties are IncrementalValueProvider queries with explicit input/derived distinction and dependency composition
- [x] Deterministic: Generated mutation methods and change tracking operate deterministically with fail-fast error handling
- [x] Performance Target: O(changes) complexity maintained through existing computation graph, <1ms cached/<10ms incremental targets specified
- [x] TDD Compliance: Development will follow incremental complexity TDD with XUnit v3 progression (basic → edge → incremental → performance → chaos)
- [x] Modern C#: Leverages .NET 10 incremental source generators, Microsoft.CodeAnalysis.CSharp, and existing record-based infrastructure
- [x] Dependency Injection: All services integrate with Microsoft.Extensions.DependencyInjection, context lifecycle managed through DI
- [x] Library Architecture: Framework library component with service registration extensions, no executables
- [x] Package API: Minimal public API (base class + attributes), integrates with existing Guacamole package structure

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
src/
├── Guacamole/                    # User-facing library (higher-level)
│   ├── ComputingContextBase.cs   # Minimal base class for user contexts
│   ├── ComputingContextAttribute.cs  # [ComputingContext] attribute
│   ├── Exceptions/               # Generated exception base types
│   └── Extensions/               # DI service registration
├── Guacamole.Core/              # Low-level infrastructure (existing)
│   ├── Providers/               # IncrementalValueProvider (existing)
│   ├── Pipeline/                # ComputationGraph (existing)
│   └── Nodes/                   # New: Base computation node types
└── Guacamole.SourceGenerators/  # Incremental source generators
    ├── ComputingContextGenerator.cs  # Main context analyzer & generator
    ├── PropertyAnalyzer.cs      # Property classification logic
    ├── CodeGenerators/          # Specific generators:
    │   ├── MutationMethodGenerator.cs    # Add/Remove/Replace methods
    │   ├── ChangeTypeGenerator.cs        # Strongly-typed change records
    │   ├── ComputationNodeGenerator.cs   # Property-specific nodes
    │   ├── SaveChangesGenerator.cs       # Optimized commit logic
    │   └── PropertyGenerator.cs          # Provider property implementations
    └── Templates/               # Code generation templates & builders

tests/
├── unit/
│   ├── Guacamole.Tests/         # Base class and generated code tests
│   ├── Guacamole.Core.Tests/    # Core infrastructure tests (existing)
│   └── Guacamole.SourceGenerators.Tests/  # Generator tests with Verify snapshots
└── integration/
    └── end-to-end context scenarios with performance validation

benchmarks/
└── ComputingContext.Benchmarks/
    ├── ZeroBoxingBenchmarks.cs           # Zero boxing/reflection validation
    ├── IncrementalUpdateBenchmarks.cs    # O(changes) complexity validation
    └── MemoryEfficiencyBenchmarks.cs     # <2x memory overhead validation
```

**Structure Decision**: Source generation-first architecture where generators produce the entire context implementation. Minimal runtime infrastructure with maximum compile-time code generation. ComputingContextBase becomes a lightweight base with all functionality generated. No runtime interfaces to avoid virtual calls and boxing.

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

**Output**: ✅ research.md with all NEEDS CLARIFICATION resolved

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

**Output**: ✅ data-model.md, ✅ /contracts/* (5 contract files), ✅ quickstart.md, ✅ CLAUDE.md updated

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
- Load `.specify/templates/tasks-template.md` as base
- Generate tasks from Phase 1 design docs prioritizing source generation infrastructure
- Source generator infrastructure tasks (foundational) [P]
- Generated code unit tests with Verify snapshots [P]
- Integration tests validating zero-boxing/reflection [P]
- Performance benchmarks validating O(changes) complexity
- End-to-end user scenarios from quickstart

**Ordering Strategy**:
- TDD order: Tests before implementation with incremental complexity
- Source generation first: Generator infrastructure → Generated code tests → Integration
- Dependency order: Core generators → Specific generators → Integration → Performance
- Mark [P] for parallel execution where generators are independent

**Key Task Categories**:
1. **Generator Infrastructure** (5-7 tasks): Property analysis, code generation engine, templates
2. **Generated Code Validation** (8-10 tasks): Unit tests with Verify snapshots per generator
3. **Performance Validation** (3-4 tasks): Zero-boxing benchmarks, O(changes) complexity tests
4. **Integration Scenarios** (6-8 tasks): End-to-end compiler context, file watcher scenarios
5. **Documentation** (2-3 tasks): Generated code documentation, performance characteristics

**Estimated Output**: 25-32 numbered, ordered tasks in tasks.md with heavy emphasis on source generation validation

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
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |


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
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented (none required)

---
*Based on Constitution v2.3.3 - See `.specify/memory/constitution.md`*
