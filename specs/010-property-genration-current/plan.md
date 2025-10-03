
# Implementation Plan: Reverse Property Generation Logic

**Branch**: `010-property-genration-current` | **Date**: 2025-10-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/home/pekkah/p/guacamole/specs/010-property-genration-current/spec.md`

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

**IMPORTANT**: The /plan command STOPS at step 8. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary

This feature reverses the current source generator logic for property classification in ComputingContext classes. Currently, properties with setters trigger automatic mutation method generation (treating them as "input" properties), while get-only properties are left as user-managed derived computations. This is counterintuitive - developers expect get-only properties to be automatically managed by the framework, and properties with setters to signal "I'll handle this myself."

The change affects PropertyAnalyzer.cs logic, all code generation components (mutation methods, change tracking, computation nodes), test snapshots, and documentation. This is a breaking change requiring documentation-only migration (no automated tooling).

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 RC1
**Primary Dependencies**: Microsoft.CodeAnalysis.CSharp (Roslyn source generators), Microsoft.Extensions.DependencyInjection, System.Collections.Immutable
**Storage**: N/A (source generator - compile-time code generation)
**Testing**: XUnit v3, Verify.SourceGenerators (snapshot testing)
**Target Platform**: .NET 10.0 class libraries (source generator runs at compile-time)
**Project Type**: Single (library-only, no web/mobile components)
**Performance Goals**: Compile-time generation must remain <100ms per context class, generated code performance equivalent to current implementation
**Constraints**: Breaking change, must update all 13 test snapshots, must maintain generated code quality/performance
**Scale/Scope**: Affects 1 analyzer class (PropertyAnalyzer.cs), 5 code generators, ~13 snapshot tests, 2 documentation files

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Incremental Computation: Not applicable (source generator itself, not query logic)
- [x] Query-Driven: Not applicable (build-time code generation, not runtime queries)
- [x] Deterministic: Source generator is deterministic (same input → same output)
- [x] Performance Target: Compile-time performance maintained (existing generator perf is acceptable)
- [x] TDD Compliance: All tests using XUnit v3, snapshot tests via Verify library, incremental TDD approach required
- [x] Modern C#: Leverages .NET 10 source generators, C# 13 partial properties feature
- [x] Dependency Injection: Generated code uses Microsoft.Extensions.DependencyInjection
- [x] Library Architecture: Pure library, no executables, source generator outputs library code only
- [x] Package API: This is internal breaking change in generation logic, public API (attributes/base classes) unchanged

## Project Structure

### Documentation (this feature)
```
specs/010-property-genration-current/
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
├── Guacamole/                      # Main library
│   ├── ComputingContextBase.cs
│   └── Attributes/
├── Guacamole.Core/                 # Core computation engine
│   ├── Providers/
│   └── Pipeline/
└── Guacamole.SourceGenerators/     # Source generators (THIS FEATURE)
    ├── PropertyAnalyzer.cs          # ** PRIMARY CHANGE **
    ├── ComputingContextGenerator.cs
    └── CodeGenerators/
        ├── PropertyGenerator.cs
        ├── MutationMethodGenerator.cs
        ├── ChangeTypeGenerator.cs
        ├── ComputationNodeGenerator.cs
        └── SaveChangesGenerator.cs

tests/
├── Guacamole.Tests/                # Integration tests
├── Guacamole.Core.Tests/           # Core library tests
└── Guacamole.SourceGenerators.Tests/  # ** PRIMARY TEST CHANGES **
    ├── PropertyAnalyzerTests.cs (if exists)
    ├── ComputingContextGeneratorTests.cs
    ├── PropertyGeneratorTests.cs
    ├── MutationMethodGeneratorTests.cs
    ├── ChangeTypeGeneratorTests.cs
    ├── ComputationNodeGeneratorTests.cs
    ├── SaveChangesGeneratorTests.cs
    └── Snapshots/                   # ** UPDATE ALL SNAPSHOTS **

docs/
├── generated-code-reference.md      # ** UPDATE EXAMPLES **
└── migration/                       # ** NEW **
    └── 010-property-convention-reversal.md

examples/
├── CompilerContext/                 # ** UPDATE EXAMPLES **
└── FileWatcherContext/             # ** UPDATE EXAMPLES **
```

**Structure Decision**: Single library project structure. This feature modifies source generator logic in `Guacamole.SourceGenerators` project, impacting generated code for contexts in the main `Guacamole` library. Tests are in `Guacamole.SourceGenerators.Tests` using snapshot testing to verify generated code changes.

## Phase 0: Outline & Research

All technical context is known - no NEEDS CLARIFICATION markers. Research focuses on existing generator patterns and test update strategy.

### Research Tasks

1. **Analyze Current PropertyAnalyzer Logic**
   - Locate and document the exact condition in PropertyAnalyzer.cs (line 66-73) that classifies properties
   - Map which generators consume InputProperties vs DerivedProperties lists
   - Identify all call sites and dependencies

2. **Inventory Affected Generators**
   - PropertyGenerator: How it uses property classification
   - MutationMethodGenerator: Generated only for InputProperties (currently setter-based)
   - ChangeTypeGenerator: Change records for InputProperties
   - ComputationNodeGenerator: Different node types for input vs derived
   - SaveChangesGenerator: SaveChanges logic for InputProperties

3. **Snapshot Test Update Strategy**
   - Document Verify.SourceGenerators workflow (verify snapshots, approve changes)
   - List all affected snapshot files (estimate ~13 based on test count)
   - Determine approval process (automated via test run, manual review)

4. **Breaking Change Documentation Requirements**
   - Migration guide template and content requirements
   - CHANGELOG.md format and versioning impact (likely MAJOR version bump)
   - docs/generated-code-reference.md example updates needed

**Output**: research.md with decisions on implementation approach, test strategy, and documentation requirements

## Phase 1: Design & Contracts

*Prerequisites: research.md complete*

### Data Model

This feature has minimal data model - it's a logic inversion in property classification:

**Current State** (to be removed):
- `property.SetMethod != null` → InputProperty → generates mutation methods
- `property.SetMethod == null` → DerivedProperty → no generation

**New State** (to be implemented):
- `property.SetMethod != null` → UserManagedProperty → no generation
- `property.SetMethod == null` → AutoManagedProperty → generates mutation methods

**Key Entity**: PropertyInfo class (already exists)
- Fields: Name, ProviderType, ItemType, HasSetter, IsCollection
- **Semantic Change**: HasSetter meaning inverts (was "generate code", becomes "skip generation")

### Contracts

Source generators don't have REST/GraphQL contracts. Instead, contracts are:

1. **Input Contract**: Partial property declarations in user code
   ```csharp
   [ComputingContext]
   public partial class MyContext : ComputingContextBase {
       public partial IncrementalValueProvider<string> AutoProperty { get; }  // Auto-managed (NEW)
       public partial IncrementalValueProvider<int> UserProperty { get; set; }  // User-managed (NEW)
   }
   ```

2. **Output Contract**: Generated code structure
   - Auto-managed (get-only) properties generate: mutation methods, change tracking, nodes
   - User-managed (with setter) properties generate: property implementation only (no mutations)

3. **Test Contract**: Snapshot files validate generated code structure
   - Each snapshot file = contract for specific generation scenario
   - Snapshots must be updated to reflect new behavior

### Quickstart Validation

**Quickstart Test Scenario** (validates end-to-end behavior):

1. Create a new test context with both property types:
   ```csharp
   [ComputingContext]
   partial class TestContext : ComputingContextBase {
       public partial IncrementalValueProvider<string> AutoName { get; }  // Should get mutations
       public partial IncrementalValueProvider<int> ManualCount { get; set; }  // Should NOT get mutations
   }
   ```

2. Verify generated code:
   - `SetName()` method exists (auto-managed)
   - No `SetCount()` method (user-managed)
   - `AutoName` property has computation node
   - `ManualCount` property has basic implementation only

3. Verify existing tests fail initially (proving behavior changed)
4. Update snapshots and verify tests pass

### Agent File Update

Run: `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude`

Add to CLAUDE.md:
- Active technology: Source generator property classification logic reversal
- Recent change: Property generation convention inverted (get-only = auto, setter = manual)

**Output**: data-model.md, contracts/ (test contracts as C# snippets), quickstart.md, updated CLAUDE.md

## Phase 2: Task Planning Approach

*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:

1. **Research & Analysis Tasks**:
   - T001: Analyze PropertyAnalyzer.cs current logic [Research]
   - T002: Document all affected generators [Research]
   - T003: Inventory snapshot test files [Research]

2. **Core Logic Change Tasks** (TDD order):
   - T004: Write failing test for new PropertyAnalyzer behavior [Test]
   - T005: Invert SetMethod != null condition in PropertyAnalyzer.cs (lines 66-73) [Impl]
   - T006: Verify PropertyAnalyzer tests pass [Validation]

3. **Generator Update Tasks** (per generator, TDD):
   - T007-T011: Update each generator's tests for new classification [Test]
   - T012-T016: Verify each generator handles classification correctly [Impl]

4. **Snapshot Update Tasks** (parallel after logic change):
   - T017: Run all snapshot tests, capture failures [Test]
   - T018: Review generated code changes in snapshots [Review]
   - T019: Approve all snapshot updates via Verify [Test]
   - T020: Verify all generator tests pass [Validation]

5. **Documentation Tasks** [P]:
   - T021: Create migration guide (docs/migration/010-property-convention-reversal.md) [Docs]
   - T022: Update docs/generated-code-reference.md examples [Docs]
   - T023: Update CHANGELOG.md with breaking change entry [Docs]

6. **Example Update Tasks** [P]:
   - T024: Update examples/CompilerContext with new convention [Impl]
   - T025: Update examples/FileWatcherContext with new convention [Impl]
   - T026: Verify examples build and run [Validation]

7. **Final Validation**:
   - T027: Run full test suite (all 153 tests) [Validation]
   - T028: Build all projects [Validation]
   - T029: Review all documentation updates [Review]

**Ordering Strategy**:
- Research first (T001-T003)
- Core logic TDD (T004-T006) - BLOCKING for all other code changes
- Generator updates can partially parallel (T007-T016) - use same test pattern
- Snapshot updates sequential (T017-T020) - depends on core logic
- Documentation parallel [P] (T021-T023) - independent
- Examples parallel [P] (T024-T026) - independent
- Final validation sequential (T027-T029)

**Estimated Output**: ~29 numbered, ordered tasks in tasks.md with [P] markers for parallelizable work

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation

*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)
**Phase 4**: Implementation (execute tasks.md following TDD and constitutional principles)
**Phase 5**: Validation (all tests pass, examples build, documentation complete, migration guide reviewed)

## Complexity Tracking

*No constitutional violations - this is a pure logic inversion within an existing source generator. All existing constitutional compliance is maintained.*

## Progress Tracking

*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command) ✓
- [x] Phase 1: Design complete (/plan command) ✓
- [x] Phase 2: Task planning complete (/plan command - describe approach only) ✓
- [x] Phase 3: Tasks generated (/tasks command) ✓
- [ ] Phase 4: Implementation complete ← NEXT STEP: Execute tasks.md
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented (none)

---
*Based on Constitution v2.4.0 - See `.specify/memory/constitution.md`*
