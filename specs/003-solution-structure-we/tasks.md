# Tasks: Initial Solution Structure

**Input**: Design documents from `/specs/003-solution-structure-we/`
**Prerequisites**: plan.md (required), research.md, data-model.md, contracts/

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → If not found: ERROR "No implementation plan found"
   → Extract: tech stack, libraries, structure
2. Load optional design documents:
   → data-model.md: Extract entities → model tasks
   → contracts/: Each file → contract test task
   → research.md: Extract decisions → setup tasks
3. Generate tasks by category:
   → Setup: project init, dependencies, linting
   → Tests: contract tests, integration tests
   → Core: models, services, CLI commands
   → Integration: DB, middleware, logging
   → Polish: unit tests, performance, docs
4. Apply task rules:
   → Different files = mark [P] for parallel
   → Same file = sequential (no [P])
   → Tests before implementation (TDD)
5. Number tasks sequentially (T001, T002...)
6. Generate dependency graph
7. Create parallel execution examples
8. Validate task completeness:
   → All contracts have tests?
   → All entities have models?
   → All endpoints implemented?
9. Return: SUCCESS (tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- **Solution root**: Repository root directory
- **Source projects**: `src/Guacamole/`, `src/Guacamole.SourceGenerators/`
- **Test projects**: `tests/Guacamole.Tests/`, `tests/Guacamole.SourceGenerators.Tests/`
- **Build configuration**: Root level files

## Phase 3.1: Setup
- [x] T001 Create solution directory structure per implementation plan
- [x] T002 [P] Create Directory.Build.props with shared build properties at repository root
- [x] T003 [P] Create GitVersion.yml configuration file at repository root

## Phase 3.2: Solution File
- [x] T004 Create Guacamole.sln solution file at repository root

## Phase 3.3: Core Library Project
- [x] T005 Create src/Guacamole/Guacamole.csproj with .NET 10 target and DI dependencies
- [x] T006 [P] Create src/Guacamole/Engine/ directory with placeholder QueryEngine.cs
- [x] T007 [P] Create src/Guacamole/Caching/ directory with placeholder ICacheManager.cs
- [x] T008 [P] Create src/Guacamole/Dependencies/ directory with placeholder IDependencyTracker.cs
- [x] T009 [P] Create src/Guacamole/Extensions/ directory with ServiceCollectionExtensions.cs

## Phase 3.4: Source Generators Project
- [x] T010 Create src/Guacamole.SourceGenerators/Guacamole.SourceGenerators.csproj with analyzer packages
- [x] T011 [P] Create src/Guacamole.SourceGenerators/Generators/ directory with placeholder QueryGenerator.cs
- [x] T012 [P] Create src/Guacamole.SourceGenerators/Templates/ directory with code generation templates

## Phase 3.5: Unit Test Project
- [x] T013 Create tests/Guacamole.Tests/Guacamole.Tests.csproj with XUnit v3 and project references
- [x] T014 [P] Create tests/Guacamole.Tests/Engine/ directory with QueryEngineTests.cs
- [x] T015 [P] Create tests/Guacamole.Tests/Caching/ directory with CacheManagerTests.cs
- [x] T016 [P] Create tests/Guacamole.Tests/Dependencies/ directory with DependencyTrackerTests.cs

## Phase 3.6: Snapshot Test Project
- [x] T017 Create tests/Guacamole.SourceGenerators.Tests/Guacamole.SourceGenerators.Tests.csproj with Verify.SourceGenerators
- [x] T018 [P] Create tests/Guacamole.SourceGenerators.Tests/Snapshots/ directory for verified snapshots
- [x] T019 [P] Create tests/Guacamole.SourceGenerators.Tests/GeneratorTests/ directory with QueryGeneratorTests.cs

## Phase 3.7: Integration and Validation
- [ ] T020 Add all projects to Guacamole.sln solution file
- [ ] T021 Verify solution builds successfully with `dotnet build`
- [ ] T022 Verify test projects run with `dotnet test`
- [ ] T023 Verify NuGet packages can be created with `dotnet pack`
- [ ] T024 Verify GitVersion calculates version numbers correctly

## Dependencies
- T001 (directory structure) before all other tasks
- T002, T003 (build configs) before project creation
- T004 (solution file) before adding projects to solution
- Project creation (T005, T010, T013, T017) before directory creation within projects
- T020 (add to solution) requires all projects to exist
- Validation tasks (T021-T024) require all implementation tasks

## Parallel Example
```
# Launch T002-T003 together:
Task: "Create Directory.Build.props with shared build properties at repository root"
Task: "Create GitVersion.yml configuration file at repository root"

# Launch T006-T009 together:
Task: "Create src/Guacamole/Engine/ directory with placeholder QueryEngine.cs"
Task: "Create src/Guacamole/Caching/ directory with placeholder ICacheManager.cs"
Task: "Create src/Guacamole/Dependencies/ directory with placeholder IDependencyTracker.cs"
Task: "Create src/Guacamole/Extensions/ directory with ServiceCollectionExtensions.cs"

# Launch T014-T016 together:
Task: "Create tests/Guacamole.Tests/Engine/ directory with QueryEngineTests.cs"
Task: "Create tests/Guacamole.Tests/Caching/ directory with CacheManagerTests.cs"
Task: "Create tests/Guacamole.Tests/Dependencies/ directory with DependencyTrackerTests.cs"
```

## Notes
- [P] tasks = different files/directories, no dependencies
- All .csproj files reference Directory.Build.props automatically
- GitVersion.MsBuild package provides automatic versioning
- Test projects must have IsPackable=false (configured in Directory.Build.props)
- Source generators must be analyzers, not regular assemblies

## Task Generation Rules
*Applied during main() execution*

1. **From Plan Structure**:
   - Each project directory → project creation task
   - Each subdirectory → directory + placeholder file task [P]

2. **From Data Model**:
   - BuildConfiguration entity → Directory.Build.props task
   - GitVersionConfiguration entity → GitVersion.yml task
   - Solution entity → .sln file task

3. **From Quickstart**:
   - Each validation step → verification task
   - Build commands → integration tasks

4. **Ordering**:
   - Setup → Projects → Integration → Validation
   - Solution file after all projects exist

## Validation Checklist
*GATE: Checked by main() before returning*

- [x] All entities from data-model have corresponding tasks
- [x] All validation steps from quickstart have tasks
- [x] All projects from plan structure have creation tasks
- [x] Directory structure tasks come before content tasks
- [x] Solution file task comes after all project tasks
- [x] Parallel tasks truly independent (different files/directories)