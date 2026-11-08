# Tasks: Reverse Property Generation Logic

> **Reconciliation (2026-11-08, #45)**: The convention reversal shipped in #10 (c0a7d00: `PropertyAnalyzer.cs`, generator tests and snapshots, examples, `docs/migration/010-property-convention-reversal.md`, `CHANGELOG.md`). Later, push-based sources (#19) removed the generated mutation methods and replaced `examples/CompilerContext` and `examples/FileWatcherContext`, and the generator rewrite (#29) replaced `PropertyAnalyzer.cs` and `CodeGenerators/*` with `ContextAnalyzer.cs`/`ContextEmitter.cs`. The tasks below are marked done for #10; file paths and method names refer to code that no longer exists. Status markers: see [specs/README.md](../README.md).

**Feature**: 010-property-genration-current
**Input**: Design documents from `/home/pekkah/p/guacamole/specs/010-property-genration-current/`
**Prerequisites**: plan.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → Tech stack: C# 13 / .NET 10 RC1, Roslyn source generators
   → Structure: Single library project (src/Guacamole.SourceGenerators)
2. Load design documents:
   → data-model.md: PropertyInfo entity (classification logic change)
   → contracts/: PropertyAnalyzerContract.cs (test scenarios)
   → research.md: Implementation approach and decisions
3. Generate tasks by category:
   → Setup: None needed (existing project)
   → Tests: Snapshot tests for all 5 generators
   → Core: PropertyAnalyzer logic inversion
   → Integration: Snapshot approval, example updates
   → Polish: Documentation, migration guide
4. Apply task rules:
   → Snapshot tests can run parallel [P] after core change
   → Documentation tasks are independent [P]
   → Generator updates sequential (shared classification logic)
5. Number tasks sequentially (T001-T029)
6. TDD order: Tests → Implementation → Validation
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- All file paths are absolute from repository root `/home/pekkah/p/guacamole/`

---

## Phase 3.1: Research & Analysis

**Goal**: Understand current implementation before making changes

- [x] **T001** Analyze PropertyAnalyzer.cs current logic — done: #10 (see `research.md`, `data-model.md`)
  - File: `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs`
  - Document lines 66-73 classification condition
  - Verify `property.SetMethod != null` → InputProperties
  - Map InputProperties/DerivedProperties usage across generators
  - **Output**: Inline comments or research notes

- [x] **T002** [P] Inventory all 5 code generators and their property usage — done: #10 (see `research.md`, `data-model.md`)
  - Files: `src/Guacamole.SourceGenerators/CodeGenerators/*.cs`
  - MutationMethodGenerator: uses InputProperties
  - ChangeTypeGenerator: uses InputProperties
  - ComputationNodeGenerator: uses both lists
  - SaveChangesGenerator: uses InputProperties
  - PropertyGenerator: uses both lists
  - **Output**: List of generators and what they consume

- [x] **T003** [P] List all snapshot test files to be updated — done: #10 (see `research.md`, `data-model.md`)
  - Directory: `tests/Guacamole.SourceGenerators.Tests/Snapshots/`
  - Count verified.cs files
  - Note which tests cover property classification
  - **Output**: Inventory of ~13 snapshot files

---

## Phase 3.2: Core Logic Change (TDD)

**Goal**: Invert PropertyAnalyzer classification logic using TDD approach

### Step 1: Write Failing Tests

- [x] **T004** Create test for new get-only property behavior — done: #10; today covered by `Generator_ForSingleValueProperty_GeneratesSourceBackedProvider`
  - File: `tests/Guacamole.SourceGenerators.Tests/ComputingContextGeneratorTests.cs`
  - Add test: `Generator_WithGetOnlyProperty_GeneratesMutationMethods`
  - Input: Context with `public partial IncrementalValueProvider<string> Name { get; }`
  - Expected: Generated code contains `SetName()` method
  - **Status**: MUST FAIL initially (proving old behavior)

- [x] **T005** Create test for new property-with-setter behavior — done: #10; today covered by `Generator_WithUserManagedProperty_DoesNotImplementIt`
  - File: `tests/Guacamole.SourceGenerators.Tests/ComputingContextGeneratorTests.cs`
  - Add test: `Generator_WithPropertyWithSetter_DoesNotGenerateMutationMethods`
  - Input: Context with `public partial IncrementalValueProvider<int> Count { get; set; }`
  - Expected: Generated code does NOT contain `SetCount()` method
  - **Status**: MUST FAIL initially (proving old behavior)

- [x] ~~**T006** Run tests to verify they fail~~ — superseded: TDD process step; cannot be verified after the fact, outcome covered by T004/T005
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests --filter "Category=NewBehavior"`
  - **Expected**: Both T004 and T005 tests fail
  - **If tests pass**: Implementation is already done (unexpected)

### Step 2: Implement Logic Inversion

- [x] **T007** Invert PropertyAnalyzer classification condition — done: #10 (`PropertyAnalyzer.cs`, replaced by `ContextAnalyzer.cs` in #29)
  - File: `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs`
  - Line 66-73: Change condition
  - **OLD**: `if (property.SetMethod != null) { inputProperties.Add(propertyInfo); }`
  - **NEW**: `if (property.SetMethod == null) { inputProperties.Add(propertyInfo); }`
  - Also update comments explaining new behavior
  - **Validation**: Code compiles without errors

- [x] **T008** Verify PropertyAnalyzer tests now pass — done: #10
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests --filter "FullyQualifiedName~PropertyAnalyzer"`
  - **Expected**: Tests from T004-T005 now pass
  - **If tests fail**: Logic change incorrect, review T007

---

## Phase 3.3: Generator Verification

**Goal**: Ensure all generators handle new classification correctly

- [x] **T009** [P] Verify MutationMethodGenerator handles new classification — done: #10 (generator tests updated; generators removed in #29)
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/MutationMethodGenerator.cs`
  - Check: Generates methods for InputProperties (now get-only)
  - **Expected**: No code changes needed, generator consumes list correctly
  - **Validation**: Build succeeds

- [x] **T010** [P] Verify ChangeTypeGenerator handles new classification — done: #10 (generator tests updated; generators removed in #29)
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/ChangeTypeGenerator.cs`
  - Check: Generates change records for InputProperties (now get-only)
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

- [x] **T011** [P] Verify ComputationNodeGenerator handles new classification — done: #10 (generator tests updated; generators removed in #29)
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/ComputationNodeGenerator.cs`
  - Check: Creates nodes based on property classification
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

- [x] **T012** [P] Verify SaveChangesGenerator handles new classification — done: #10 (generator tests updated; generators removed in #29)
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/SaveChangesGenerator.cs`
  - Check: Applies changes for InputProperties (now get-only)
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

- [x] **T013** [P] Verify PropertyGenerator handles new classification — done: #10 (generator tests updated; generators removed in #29)
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/PropertyGenerator.cs`
  - Check: Implements properties correctly for both lists
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

---

## Phase 3.4: Snapshot Test Updates

**Goal**: Update all snapshot tests to reflect new generated code

⚠️ **CRITICAL**: Review all diffs carefully before approving

- [x] **T014** Run all snapshot tests to capture failures — done: #10 (snapshots updated)
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests`
  - **Expected**: ~13 snapshot tests fail with diffs
  - Capture diff output to review in T015
  - **If tests pass**: No changes detected (unexpected)

- [x] **T015** Review all snapshot test diffs manually — done: #10 (snapshots updated)
  - Directory: `tests/Guacamole.SourceGenerators.Tests/Snapshots/`
  - For each .received.cs file:
    - Compare to corresponding .verified.cs
    - Verify get-only properties now have mutation methods
    - Verify properties with setters do NOT have mutation methods
    - Check for unintended changes
  - **Critical**: Do not approve if diffs look wrong
  - **Output**: List of files safe to approve

- [x] **T016** Approve snapshot updates — done: #10 (snapshots updated)
  - For each verified diff from T015:
    - Option 1: `mv *.received.cs *.verified.cs` (manual)
    - Option 2: Use Verify test runner approve command
  - Files affected (estimate):
    - ComputingContextGeneratorTests snapshots: ~3 files
    - PropertyGeneratorTests snapshots: ~2 files
    - MutationMethodGeneratorTests snapshots: ~2 files
    - ChangeTypeGeneratorTests snapshots: ~2 files
    - ComputationNodeGeneratorTests snapshots: ~2 files
    - SaveChangesGeneratorTests snapshots: ~2 files
  - **Total**: ~13 snapshot files

- [x] **T017** Verify all generator tests pass after approval — done: #10 (snapshots updated)
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests`
  - **Expected**: All tests pass (0 failures)
  - **If tests fail**: Review snapshot approval, may need to re-approve

---

## Phase 3.5: Examples & Integration Tests

**Goal**: Update examples to use new convention and verify they build

- [x] **T018** [P] Update CompilerContext example for new convention — done: #10 (example replaced in #19)
  - File: `examples/CompilerContext/MyCompilerContext.cs`
  - Change: Properties that HAD setters (old auto) → NOW get-only
  - Change: Properties that WERE get-only (old manual) → NOW have setters
  - Review: Constructor initialization matches new convention
  - **Validation**: `dotnet build examples/CompilerContext` succeeds

- [x] **T019** [P] Update FileWatcherContext example for new convention — done: #10 (example replaced in #19)
  - File: `examples/FileWatcherContext/MyFileWatcherContext.cs`
  - Change: Invert setter presence to match new convention
  - Review: Constructor initialization
  - **Validation**: `dotnet build examples/FileWatcherContext` succeeds

- [x] **T020** Run integration tests in Guacamole.Tests — done: CI
  - Command: `dotnet test tests/Guacamole.Tests`
  - **Expected**: All tests pass
  - **If failures**: Examples may be using old convention, review T018-T019

---

## Phase 3.6: Documentation

**Goal**: Document breaking change for users

- [x] **T021** [P] Create migration guide — done: `docs/migration/010-property-convention-reversal.md`
  - File: `docs/migration/010-property-convention-reversal.md` (NEW)
  - Create `docs/migration/` directory if it doesn't exist
  - Content structure:
    - Overview of breaking change
    - Before/After code examples
    - Step-by-step migration instructions
    - Common patterns and troubleshooting
  - Reference: Use research.md decisions and quickstart.md scenarios
  - **Length**: ~2 pages

- [x] **T022** [P] Update generated-code-reference.md examples
  - File: `docs/generated-code-reference.md`
  - Lines 24-35: Example CompilerContext
  - Change: Invert setter presence in example
  - Update comments explaining auto vs manual properties
  - **Validation**: Examples match new convention

- [x] **T023** [P] Add CHANGELOG.md entry — done: "property convention reversal" entry in `CHANGELOG.md`
  - File: `CHANGELOG.md`
  - Section: `## [Unreleased] - BREAKING CHANGES` (create if missing)
  - Entry: "Reversed property generation convention: get-only properties now automatically managed, properties with setters now user-managed"
  - Note: Requires MAJOR version bump (2.x → 3.0.0)
  - **Format**: Follow existing CHANGELOG style

---

## Phase 3.7: Final Validation

**Goal**: Ensure everything works end-to-end

- [x] **T024** Run full test suite — done: CI
  - Command: `dotnet test`
  - **Expected**: All 153+ tests pass
  - **Critical**: No test failures
  - **If failures**: Review which phase failed, fix before proceeding

- [x] **T025** Build all projects — done: CI
  - Command: `dotnet build`
  - **Expected**: All projects build successfully
  - Check: src/Guacamole.SourceGenerators, examples/, tests/
  - **If build fails**: Review compiler errors, likely example misconfiguration

- [x] ~~**T026** Execute quickstart.md validation~~ — superseded: `quickstart.md` checks `PropertyAnalyzer.cs` and generated `Set{X}()` methods, both removed (#19, #29)
  - File: Follow steps in `specs/010-property-genration-current/quickstart.md`
  - Verify: All 6 quickstart steps succeed
  - **Expected**: Completes in ~6 minutes
  - **If quickstart fails**: Fix issues before marking feature complete

- [x] **T027** Review all documentation for accuracy — done: #33 brought the docs in line with the current code
  - Files:
    - `docs/migration/010-property-convention-reversal.md`
    - `docs/generated-code-reference.md`
    - `CHANGELOG.md`
    - `README.md` (verify examples still accurate)
  - Check: No references to old behavior
  - Check: All code examples use new convention
  - **Output**: Documentation is consistent

- [ ] **T028** Run dotnet format — open: `dotnet format --verify-no-changes` is not clean (line endings: `.editorconfig` `crlf` vs `.gitattributes` LF, plus a few whitespace fixes)
  - Command: `dotnet format`
  - **Expected**: No formatting changes (code already formatted)
  - **If changes**: Commit formatting updates

- [x] **T029** Final smoke test with new test context — done: covered by `ComputingContextGeneratorTests` and `DiagnosticTests` (GUAC0003/GUAC0005)
  - Create minimal test: Get-only property should generate SetMethod()
  - Create minimal test: Property with setter should NOT generate SetMethod()
  - **Validation**: Both tests pass
  - **Cleanup**: Remove test contexts after validation

---

## Dependencies

```
Phase 3.1 (Research): T001-T003
  ├─ Can run in parallel
  └─ BLOCKS → Phase 3.2

Phase 3.2 (Core Logic): T004-T008
  ├─ T004, T005 (write tests) [P]
  ├─ T006 (verify tests fail)
  ├─ T007 (implement logic change) - BLOCKS ALL DOWNSTREAM
  └─ T008 (verify tests pass)
  └─ BLOCKS → Phase 3.3, 3.4

Phase 3.3 (Generator Verification): T009-T013
  ├─ All tasks can run in parallel [P]
  └─ BLOCKS → Phase 3.4

Phase 3.4 (Snapshots): T014-T017
  ├─ T014 (run tests)
  ├─ T015 (review diffs) - CRITICAL MANUAL STEP
  ├─ T016 (approve snapshots)
  └─ T017 (verify tests pass)
  └─ BLOCKS → Phase 3.5

Phase 3.5 (Examples): T018-T020
  ├─ T018, T019 (update examples) [P]
  └─ T020 (test examples)
  └─ No blocking (parallel with Phase 3.6)

Phase 3.6 (Documentation): T021-T023
  ├─ All tasks can run in parallel [P]
  └─ No blocking (parallel with Phase 3.5)

Phase 3.7 (Validation): T024-T029
  ├─ T024 (full test suite) - REQUIRES all previous phases
  ├─ T025 (build all)
  ├─ T026 (quickstart)
  ├─ T027 (doc review)
  ├─ T028 (format)
  └─ T029 (smoke test)
```

## Parallel Execution Examples

### Research Phase (T001-T003)
```bash
# Can run concurrently:
# Terminal 1:
Task: "Analyze PropertyAnalyzer.cs current logic in src/Guacamole.SourceGenerators/PropertyAnalyzer.cs"

# Terminal 2:
Task: "Inventory all 5 code generators and their property usage in src/Guacamole.SourceGenerators/CodeGenerators/*.cs"

# Terminal 3:
Task: "List all snapshot test files in tests/Guacamole.SourceGenerators.Tests/Snapshots/"
```

### Generator Verification (T009-T013)
```bash
# All can run in parallel (different files):
Task: "Verify MutationMethodGenerator in src/Guacamole.SourceGenerators/CodeGenerators/MutationMethodGenerator.cs"
Task: "Verify ChangeTypeGenerator in src/Guacamole.SourceGenerators/CodeGenerators/ChangeTypeGenerator.cs"
Task: "Verify ComputationNodeGenerator in src/Guacamole.SourceGenerators/CodeGenerators/ComputationNodeGenerator.cs"
Task: "Verify SaveChangesGenerator in src/Guacamole.SourceGenerators/CodeGenerators/SaveChangesGenerator.cs"
Task: "Verify PropertyGenerator in src/Guacamole.SourceGenerators/CodeGenerators/PropertyGenerator.cs"
```

### Documentation Tasks (T021-T023)
```bash
# All independent, different files:
Task: "Create migration guide in docs/migration/010-property-convention-reversal.md"
Task: "Update examples in docs/generated-code-reference.md"
Task: "Add CHANGELOG entry in CHANGELOG.md"
```

## Task Execution Guidelines

### TDD Workflow (Critical)
1. **Always** write tests first (T004-T005)
2. **Verify** tests fail (T006)
3. **Then** implement (T007)
4. **Verify** tests pass (T008)

### Snapshot Review (Critical)
- **T015 is manual and critical**: Do not skip diff review
- Look for unintended changes in snapshots
- Verify only property classification changed, not other code patterns
- When in doubt, reject snapshot and investigate

### Commit Strategy
- Commit after each completed task
- Use task ID in commit message: "T007: Invert PropertyAnalyzer classification logic"
- Snapshot approvals should be separate commits for easy review

### Validation Checkpoints
- After T008: Core logic tests pass ✓
- After T017: All snapshot tests pass ✓
- After T024: Full test suite passes ✓
- After T026: Quickstart succeeds ✓

## Notes

- **Total Tasks**: 29
- **Parallel Tasks**: 11 marked with [P]
- **Critical Manual Steps**: T015 (snapshot review)
- **Estimated Time**: ~3.5 hours
  - Research: 30 min
  - Core change: 15 min
  - Snapshot review: 30 min
  - Documentation: 2 hours
  - Validation: 30 min

## Validation Checklist

✅ **Task Generation Rules Applied**:
- [x] All contracts have corresponding tests (PropertyAnalyzerContract → T004, T005)
- [x] Data model changes have implementation tasks (PropertyInfo → T007)
- [x] All tests come before implementation (T004-T006 before T007)
- [x] Parallel tasks truly independent (different files, no shared state)
- [x] Each task specifies exact file path
- [x] No [P] task modifies same file as another [P] task
- [x] Quickstart scenarios have validation tasks (T026)
- [x] TDD ordering enforced (Tests → Implementation → Validation)

---

**Status**: ✅ Ready for execution
**Next Step**: Execute tasks sequentially by phase, respecting dependencies
**Branch**: 010-property-genration-current
