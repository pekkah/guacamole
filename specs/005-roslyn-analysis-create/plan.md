
# Implementation Plan: Roslyn Incremental Source Generator API Analysis

**Branch**: `005-roslyn-analysis-create` | **Date**: 2025-09-29 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/005-roslyn-analysis-create/spec.md`

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
Creating a comprehensive analysis of Roslyn's IIncrementalGenerator API patterns to evaluate whether Guacamole should adopt Roslyn's incremental source generator approach instead of salsa-rs patterns. The analysis will focus on core pipeline patterns and performance optimization techniques, delivered as multiple markdown documents with minimal working examples.

## Technical Context
**Language/Version**: N/A (documentation/analysis task)
**Primary Dependencies**: N/A (analysis of Roslyn API patterns)
**Storage**: N/A (markdown documents only)
**Testing**: N/A (analysis deliverables)
**Target Platform**: Documentation (markdown files)
**Project Type**: Documentation/Research
**Performance Goals**: N/A (analysis task)
**Constraints**: Focus on IIncrementalGenerator API, high-level comparison with salsa-rs
**Scale/Scope**: Multiple markdown files covering architecture, patterns, performance, comparison

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Incremental Computation: Analysis focuses on Roslyn's incremental computation patterns
- [x] Query-Driven: Analysis evaluates query-driven patterns in Roslyn's API
- [x] Deterministic: Documentation task - no non-deterministic operations
- [x] Performance Target: Analysis includes performance characteristics evaluation
- [N/A] TDD Compliance: Documentation task - no code implementation
- [x] Modern C#: Analysis covers modern C# source generator features
- [x] Dependency Injection: Analysis considers DI integration patterns
- [N/A] Library Architecture: Documentation deliverable only
- [N/A] Package API: Documentation task - no API surface

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
# N/A - Documentation task only
# Final deliverables are markdown files in docs/roslyn-analysis/
# Planning artifacts remain in specs/005-roslyn-analysis-create/
```

**Structure Decision**: Documentation-only feature. Planning artifacts remain in specs directory, but final analysis documents are delivered to docs/roslyn-analysis/ to match existing docs/salsa-rs-analysis/ structure. No source code changes required.

## Phase 0: Outline & Research
1. **Extract unknowns from Technical Context** above:
   - No NEEDS CLARIFICATION items (documentation task)
   - Research focus: Roslyn IIncrementalGenerator API patterns
   - Key areas: Pipeline patterns, caching strategies, performance optimization

2. **Generate and dispatch research agents**:
   ```
   Task 1: Research Roslyn IIncrementalGenerator core pipeline patterns
   Task 2: Research Roslyn caching and incremental update strategies
   Task 3: Research Roslyn performance optimization techniques
   Task 4: Research comparison points between Roslyn and salsa-rs approaches
   ```

3. **Consolidate findings** in `research.md` using format:
   - Decision: Document structure for analysis
   - Rationale: Multiple focused documents for clarity
   - Alternatives considered: Single document vs multiple files

**Output**: research.md with research approach documented

## Phase 1: Design & Contracts
*Prerequisites: research.md complete*

1. **Extract entities from feature spec** → `data-model.md`:
   - Analysis document entities
   - Document structure and relationships
   - Content organization rules

2. **Generate documentation structure** from functional requirements:
   - For each analysis area → document outline
   - Define content contracts for consistency
   - Output structure to `/contracts/`

3. **Define validation criteria**:
   - Completeness checks for each document
   - Cross-reference validation
   - Example code validation approach

4. **Extract deliverable scenarios** from user stories:
   - Developer understanding Roslyn patterns
   - Architect comparing approaches
   - Decision-making criteria

5. **Update agent file incrementally** (O(1) operation):
   - Run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude`
     **IMPORTANT**: Execute it exactly as specified above. Do not add or remove any arguments.
   - Documentation task - minimal tech updates
   - Preserve manual additions between markers
   - Update recent changes (keep last 3)
   - Keep under 150 lines for token efficiency
   - Output to repository root

**Output**: data-model.md, /contracts/*, quickstart.md, CLAUDE.md updated

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
- Load `.specify/templates/tasks-template.md` as base
- Generate tasks from Phase 1 design docs (contracts, data model, quickstart)
- Each analysis document → research and writing task
- Each code example → validation task
- Comparison document → comparative analysis task
- Recommendations → synthesis and decision task

**Ordering Strategy**:
- Research order: Architecture before patterns before performance
- Comparison after individual analyses
- Recommendations last (after all analysis complete)
- Mark [P] for parallel research tasks

**Estimated Output**: 10-15 numbered, ordered tasks in tasks.md (documentation tasks)

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
- [x] Complexity deviations documented (N/A - documentation task)

---
*Based on Constitution v2.3.0 - See `.specify/memory/constitution.md`*
