# Tasks: Roslyn Incremental Source Generator API Analysis

> **Reconciliation (2026-11-08, #45)**: T005–T007 were done; the documents exist in `docs/roslyn-analysis/`. Status markers: see [specs/README.md](../README.md).

**Input**: Design documents from `/specs/005-roslyn-analysis-create/`
**Prerequisites**: plan.md (required), research.md, data-model.md, contracts/

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → Extract: Documentation task, multiple markdown files approach
2. Load design documents:
   → data-model.md: 5 planned analysis documents
   → contracts/: Documentation structure requirements
   → research.md: Multi-document approach with specific focus areas
   → quickstart.md: Review and validation scenarios
3. Generate tasks by category:
   → Setup: Document structure validation
   → Research: Individual analysis documents
   → Examples: Code validation tasks
   → Integration: Cross-reference validation
   → Polish: Review guide testing
4. Apply task rules:
   → Different documents = mark [P] for parallel research
   → Same document = sequential sections
   → Research before cross-references
5. Number tasks sequentially (T001, T002...)
6. Create parallel execution examples for research tasks
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- Planning files: `/specs/005-roslyn-analysis-create/`
- Final deliverables: `/docs/roslyn-analysis/` (to match existing `docs/salsa-rs-analysis/`)
- Analysis documents: Individual markdown files per topic in docs directory
- Code examples: Inline in analysis documents

## Phase 3.1: Setup and Structure
- [x] T001 Create docs/roslyn-analysis/ directory structure to match docs/salsa-rs-analysis/
- [x] T002 Validate documentation structure per contracts/documentation-structure.yaml
- [x] T003 [P] Create document templates for 5 analysis documents in docs/roslyn-analysis/
- [x] T004 [P] Set up cross-reference validation framework

## Phase 3.2: Research and Content Creation
**Core Analysis Documents (can run in parallel)**
- [x] T005 [P] Research and write docs/roslyn-analysis/roslyn-architecture.md covering IIncrementalGenerator core concepts, pipeline architecture, and compilation integration
- [x] T006 [P] Research and write docs/roslyn-analysis/roslyn-patterns.md covering common implementation patterns, CreateSyntaxProvider usage, transformation patterns, and best practices with minimal code examples
- [x] T007 [P] Research and write docs/roslyn-analysis/roslyn-performance.md covering caching mechanisms, optimization strategies, benchmarking approaches, and memory management patterns

## Phase 3.3: Comparative Analysis (depends on T005-T007)
- [x] T008 Research and write docs/roslyn-analysis/roslyn-vs-salsa.md providing high-level conceptual comparison between Roslyn IIncrementalGenerator and salsa-rs approaches
- [x] T009 Validate all cross-references between analysis documents and ensure consistency

## Phase 3.4: Code Examples and Validation
- [x] T010 [P] Create and validate minimal working code examples for docs/roslyn-analysis/roslyn-patterns.md (syntax provider patterns)
- [x] T011 [P] Create and validate minimal working code examples for docs/roslyn-analysis/roslyn-patterns.md (transformation patterns)
- [x] T012 [P] Create and validate minimal working code examples for docs/roslyn-analysis/roslyn-performance.md (optimization techniques)

## Phase 3.5: Synthesis and Recommendations
- [x] T013 Research and write docs/roslyn-analysis/recommendations.md with executive summary, suitability assessment for Guacamole, adoption strategy, and risk analysis
- [x] T014 Validate all analysis documents meet contract requirements (minimum sections, content length, examples)

## Phase 3.6: Integration and Quality Assurance
- [x] T015 Test quickstart.md review guide by following all validation steps
- [x] T016 [P] Verify all code examples compile with appropriate Roslyn references
- [x] T017 [P] Validate all cross-references and internal links work correctly
- [x] T018 Final content review ensuring technical accuracy and consistency across all documents

## Dependencies
- Templates and setup (T001-T004) before content creation
- Core analysis (T005-T007) before comparison (T008-T009)
- Content creation before examples (T010-T012)
- All analysis complete before recommendations (T013)
- All content before final validation (T014-T018)

## Parallel Example
```
# Launch core research tasks together (T005-T007):
Task: "Research and write docs/roslyn-analysis/roslyn-architecture.md covering IIncrementalGenerator core concepts"
Task: "Research and write docs/roslyn-analysis/roslyn-patterns.md covering implementation patterns and best practices"
Task: "Research and write docs/roslyn-analysis/roslyn-performance.md covering optimization and caching strategies"

# Launch code example tasks together (T010-T012):
Task: "Create minimal working examples for syntax provider patterns"
Task: "Create minimal working examples for transformation patterns"
Task: "Create minimal working examples for optimization techniques"

# Launch final validation tasks together (T016-T017):
Task: "Verify all code examples compile with Roslyn references"
Task: "Validate all cross-references and internal links"
```

## Specific File Paths
- `/docs/roslyn-analysis/roslyn-architecture.md`
- `/docs/roslyn-analysis/roslyn-patterns.md`
- `/docs/roslyn-analysis/roslyn-performance.md`
- `/docs/roslyn-analysis/roslyn-vs-salsa.md`
- `/docs/roslyn-analysis/recommendations.md`

## Task Generation Rules Applied

1. **From Data Model**:
   - AnalysisDocument entities → individual document creation tasks (T005-T007, T008, T013)
   - CodeExample entities → example validation tasks (T010-T012)
   - DocumentSet validation → integration tasks (T014-T018)

2. **From Contracts**:
   - Documentation structure requirements → validation tasks (T002, T014)
   - Content length and section requirements → embedded in document tasks

3. **From Research**:
   - Multi-document approach → parallel research tasks
   - Specific focus areas → targeted research scope per task

4. **From Quickstart**:
   - Review scenarios → validation tasks (T015)
   - Decision framework → recommendation synthesis (T013)

## Validation Checklist
*GATE: Checked before task execution*

- [x] All planned documents have creation tasks
- [x] All code examples have validation tasks
- [x] Content creation before cross-reference validation
- [x] Parallel tasks are truly independent (different files)
- [x] Each task specifies exact file path or scope
- [x] No task modifies same file as another [P] task

## Expected Deliverables
Upon completion, the following files will exist in `/docs/roslyn-analysis/`:
1. `roslyn-architecture.md` - Core concepts and design principles
2. `roslyn-patterns.md` - Implementation patterns with examples
3. `roslyn-performance.md` - Optimization and performance analysis
4. `roslyn-vs-salsa.md` - Comparative analysis with salsa-rs
5. `recommendations.md` - Decision framework and recommendations

All documents will include minimal working code examples, cross-references, and meet the structure requirements defined in the contracts. This mirrors the existing `docs/salsa-rs-analysis/` structure for consistency.