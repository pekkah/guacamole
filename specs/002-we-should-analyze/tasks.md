# Tasks: Salsa-rs Analysis and .NET Adaptation

> **Reconciliation (2026-11-08, #45)**: All documents exist in `docs/salsa-rs-analysis/` (78f0228); research, adaptation and navigation tasks are reflected in their content and internal links resolve. Status markers: see [specs/README.md](../README.md).

**Input**: Design documents from `/home/pekkah/p/guacamole/specs/002-we-should-analyze/`
**Prerequisites**: plan.md (required), research.md, data-model.md, contracts/, quickstart.md

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → If not found: ERROR "No implementation plan found"
   → Extract: documentation structure, analysis scope, output format
2. Load optional design documents:
   → data-model.md: Extract documentation entities → analysis tasks
   → contracts/: API reference patterns → theoretical adaptation tasks
   → research.md: Extract research decisions → web research tasks
   → quickstart.md: Extract user scenarios → navigation tasks
3. Generate tasks by category:
   → Setup: documentation structure, directory creation
   → Research: web research on salsa-rs concepts and architecture
   → Analysis: conceptual documentation for each major area
   → Adaptations: theoretical .NET mappings and patterns
   → Navigation: cross-references, validation, completion
4. Apply task rules:
   → Different files = mark [P] for parallel
   → Same file = sequential (no [P])
   → Research before analysis (dependency order)
5. Number tasks sequentially (T001, T002...)
6. Generate dependency graph
7. Create parallel execution examples
8. Validate task completeness:
   → All salsa-rs concepts analyzed?
   → All .NET adaptations documented?
   → Cross-references complete?
9. Return: SUCCESS (tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- **Documentation output**: `docs/salsa-rs-analysis/` at repository root
- **Planning artifacts**: `specs/002-we-should-analyze/` for design documents
- Paths shown below reference the pure documentation structure from plan.md

## Phase 3.1: Setup
- [x] T001 Create documentation structure in `docs/salsa-rs-analysis/` with planned document files
- [x] T002 Initialize README.md in `docs/salsa-rs-analysis/` with navigation and overview structure
- [x] T003 [P] Configure markdown linting validation for documentation consistency

## Phase 3.2: Research Foundation ⚠️ MUST COMPLETE BEFORE 3.3
**CRITICAL: Research tasks provide foundation for all analysis documentation**
- [x] T004 [P] Research salsa-rs core architecture and incremental computation principles using web sources — done: findings captured in `docs/salsa-rs-analysis/` (78f0228)
- [x] T005 [P] Research salsa-rs query system, derived queries, and dependency tracking mechanisms — done: findings captured in `docs/salsa-rs-analysis/` (78f0228)
- [x] T006 [P] Research salsa-rs caching strategies, memoization, and invalidation algorithms — done: findings captured in `docs/salsa-rs-analysis/` (78f0228)
- [x] T007 [P] Research salsa-rs API design patterns, trait system, and ergonomic interfaces — done: findings captured in `docs/salsa-rs-analysis/` (78f0228)
- [x] T008 [P] Research Rust-specific optimizations: ownership model, zero-cost abstractions, macro system — done: findings captured in `docs/salsa-rs-analysis/` (78f0228)

## Phase 3.3: Core Analysis Documentation (ONLY after research complete)
- [x] T009 Create `docs/salsa-rs-analysis/architecture-overview.md` with high-level salsa-rs architecture analysis
- [x] T010 Create `docs/salsa-rs-analysis/core-concepts.md` with fundamental concepts following data model structure
- [x] T011 Create `docs/salsa-rs-analysis/algorithms-analysis.md` with incremental computation algorithms and O(changes) patterns
- [x] T012 Create `docs/salsa-rs-analysis/caching-strategies.md` with memoization and invalidation approaches
- [x] T013 Create `docs/salsa-rs-analysis/api-patterns.md` with interface design and query composition patterns

## Phase 3.4: .NET Theoretical Adaptations
- [x] T014 Create `docs/salsa-rs-analysis/dotnet-adaptations.md` with theoretical C# mappings following constitutional requirements
- [x] T015 Document theoretical integration patterns with Microsoft.Extensions.DependencyInjection in adaptations — done: DI section in `docs/salsa-rs-analysis/dotnet-adaptations.md`
- [x] T016 Document theoretical performance optimizations using spans, source generators, and value types in adaptations — done: spans/source generators/value types in `dotnet-adaptations.md`

## Phase 3.5: Navigation and Validation
- [x] T017 [P] Add cross-references and navigation links between all analysis documents following data model patterns
- [x] T018 [P] Validate constitutional compliance of all theoretical .NET adaptations against project requirements — done: "Constitutional Compliance" section in `docs/salsa-rs-analysis/README.md`
- [x] T019 [P] Update `docs/salsa-rs-analysis/README.md` with complete navigation, reading paths, and document summaries
- [x] T020 [P] Validate documentation completeness, accuracy, and cross-reference integrity across all analysis documents — done: all relative links between the analysis documents resolve

## Dependencies
- Setup (T001-T003) before all other tasks
- Research (T004-T008) before analysis documentation (T009-T013)
- Core analysis (T009-T013) before .NET adaptations (T014-T016)
- All analysis complete before navigation and validation (T017-T020)

## Parallel Example
```
# Launch T004-T008 together (research foundation):
Task: "Research salsa-rs core architecture and incremental computation principles using web sources"
Task: "Research salsa-rs query system, derived queries, and dependency tracking mechanisms"
Task: "Research salsa-rs caching strategies, memoization, and invalidation algorithms"
Task: "Research salsa-rs API design patterns, trait system, and ergonomic interfaces"
Task: "Research Rust-specific optimizations: ownership model, zero-cost abstractions, macro system"

# Launch T017-T020 together (final validation):
Task: "Add cross-references and navigation links between all analysis documents following data model patterns"
Task: "Validate constitutional compliance of all theoretical .NET adaptations against project requirements"
Task: "Update docs/salsa-rs-analysis/README.md with complete navigation, reading paths, and document summaries"
Task: "Validate documentation completeness, accuracy, and cross-reference integrity across all analysis documents"
```

## Notes
- [P] tasks = different documentation files, no dependencies between them
- All documentation tasks focus on conceptual analysis without executable implementations
- Research tasks use web sources to understand salsa-rs architecture and principles
- .NET adaptation tasks create theoretical mappings following constitutional requirements
- Cross-reference validation ensures navigable and complete documentation set

## Task Generation Rules
*Applied during main() execution*

1. **From Research Plan**:
   - Each research area → web research task [P]
   - Each salsa-rs concept area → analysis documentation task

2. **From Data Model**:
   - Each documentation entity → creation task [P] where no dependencies
   - Document relationships → cross-reference tasks

3. **From Plan Structure**:
   - Each planned output document → documentation creation task
   - Constitutional requirements → validation tasks [P]

4. **Ordering**:
   - Setup → Research → Analysis → Adaptations → Navigation
   - Dependencies prevent parallel execution where logical order required

## Validation Checklist
*GATE: Checked by main() before returning*

- [x] All planned documentation files have creation tasks
- [x] Research foundation tasks cover all major salsa-rs areas
- [x] Constitutional compliance validation included for .NET adaptations
- [x] Cross-reference and navigation tasks ensure documentation completeness
- [x] Parallel tasks truly independent (different files, no logical dependencies)
- [x] Each task specifies exact file path in docs/salsa-rs-analysis/
- [x] No task depends on executable code or implementations
- [x] All tasks produce markdown documentation artifacts only