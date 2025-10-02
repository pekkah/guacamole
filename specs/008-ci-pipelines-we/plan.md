
# Implementation Plan: CI/CD Pipeline for Automated Testing, Building, and Package Publishing

**Branch**: `008-ci-pipelines-we` | **Date**: 2025-10-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/008-ci-pipelines-we/spec.md`

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
Automated CI/CD pipeline using GitHub Actions to run tests on every push/PR, build NuGet packages with GitVersion-based semantic versioning, and publish stable releases from git tags and pre-releases from main branch. Cross-platform testing on Linux + Windows ensures compatibility. All quality gates (tests, builds) must pass before package publishing.

## Technical Context
**Language/Version**: C# 13 / .NET 10.0 RC1
**Primary Dependencies**: GitHub Actions, GitVersion, NuGet CLI, dotnet SDK, XUnit v3
**Storage**: N/A (CI/CD infrastructure, not data storage)
**Testing**: XUnit v3 (constitutional requirement)
**Target Platform**: GitHub Actions runners (ubuntu-latest, windows-latest)
**Project Type**: single (library projects for NuGet distribution)
**Performance Goals**: CI run time <10 minutes for tests, build time <5 minutes for packages
**Constraints**: All tests must pass (100% success rate), cross-platform compatibility (Linux + Windows), packages must not duplicate versions on NuGet.org
**Scale/Scope**: 3 library projects (Guacamole, Guacamole.Core, Guacamole.SourceGenerators), 3 test projects (148 total tests currently), multiple NuGet packages

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Incremental Computation: N/A (infrastructure feature, not query-based feature)
- [x] Query-Driven: N/A (infrastructure feature, not query-based feature)
- [x] Deterministic: CI pipelines are deterministic (same inputs → same outputs)
- [x] Performance Target: N/A (infrastructure feature, not query-based feature)
- [x] TDD Compliance: Will validate CI behavior through actual workflow runs (integration testing)
- [x] Modern C#: N/A (GitHub Actions YAML, not C# code)
- [x] Dependency Injection: N/A (infrastructure feature, not service-based code)
- [x] Library Architecture: Compliant (ensures library projects are packaged correctly)
- [x] Package API: Compliant (enforces semantic versioning and prevents breaking changes)

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
.github/
└── workflows/           # GitHub Actions workflow files (NEW)
    ├── ci.yml          # Test + build workflow for PRs and main
    └── publish.yml     # NuGet publishing workflow for tags

src/
├── Guacamole/                    # Main library (packable)
├── Guacamole.Core/               # Core library (packable)
└── Guacamole.SourceGenerators/   # Source generators (packable)

tests/
├── Guacamole.Tests/              # Tests for main library
├── Guacamole.Core.Tests/         # Tests for core library
└── Guacamole.SourceGenerators.Tests/  # Tests for source generators

GitVersion.yml           # GitVersion configuration (NEW)
```

**Structure Decision**: Single repository with library projects. CI/CD infrastructure added to `.github/workflows/` directory. GitVersion configuration at repository root controls versioning strategy based on git history and tags.

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
- Generate tasks from Phase 1 design docs (contracts, data model, quickstart)
- Each workflow contract → workflow implementation task
- GitVersion config contract → GitVersion config task
- Quickstart → validation task
- GitHub secret setup → configuration task

**Ordering Strategy**:
1. **Setup tasks first**: Repository configuration, secrets
2. **Infrastructure tasks**: GitVersion configuration
3. **Workflow implementation**: CI workflow, Publish workflow
4. **Validation tasks**: Run quickstart validation steps
5. **Documentation tasks**: Update project documentation

**Dependency Order**:
- GitVersion config before workflows (workflows depend on it)
- CI workflow before Publish workflow (can validate CI first)
- Workflows before validation (need workflows to validate)
- All implementation before documentation updates

**Parallel Execution Opportunities** [P]:
- CI and Publish workflows can be implemented in parallel (independent files)
- Contract tests can run in parallel (independent validations)

**Estimated Output**: 12-15 numbered, ordered tasks in tasks.md

**Task Breakdown Examples**:
1. Setup: Create `.github/workflows/` directory, configure NuGet API key secret
2. Config: Implement GitVersion.yml at repository root
3. CI Workflow: Implement `.github/workflows/ci.yml` (test + build jobs)
4. Publish Workflow: Implement `.github/workflows/publish.yml` (test + publish jobs)
5. Validation: Execute quickstart validation phases 1-8
6. Documentation: Update README with CI badges, update CLAUDE.md

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
- [x] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented (N/A - no deviations)

**Artifacts Created**:
- [x] research.md (Phase 0)
- [x] data-model.md (Phase 1)
- [x] contracts/ci-workflow-contract.md (Phase 1)
- [x] contracts/publish-workflow-contract.md (Phase 1)
- [x] contracts/gitversion-config-contract.md (Phase 1)
- [x] quickstart.md (Phase 1)
- [x] CLAUDE.md updated (Phase 1)
- [x] tasks.md (Phase 3)

---
*Based on Constitution v2.3.0 - See `.specify/memory/constitution.md`*
