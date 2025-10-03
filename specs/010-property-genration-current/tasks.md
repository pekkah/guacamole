# Tasks: Reverse Property Generation Logic

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

- [ ] **T001** Analyze PropertyAnalyzer.cs current logic
  - File: `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs`
  - Document lines 66-73 classification condition
  - Verify `property.SetMethod != null` → InputProperties
  - Map InputProperties/DerivedProperties usage across generators
  - **Output**: Inline comments or research notes

- [ ] **T002** [P] Inventory all 5 code generators and their property usage
  - Files: `src/Guacamole.SourceGenerators/CodeGenerators/*.cs`
  - MutationMethodGenerator: uses InputProperties
  - ChangeTypeGenerator: uses InputProperties
  - ComputationNodeGenerator: uses both lists
  - SaveChangesGenerator: uses InputProperties
  - PropertyGenerator: uses both lists
  - **Output**: List of generators and what they consume

- [ ] **T003** [P] List all snapshot test files to be updated
  - Directory: `tests/Guacamole.SourceGenerators.Tests/Snapshots/`
  - Count verified.cs files
  - Note which tests cover property classification
  - **Output**: Inventory of ~13 snapshot files

---

## Phase 3.2: Core Logic Change (TDD)

**Goal**: Invert PropertyAnalyzer classification logic using TDD approach

### Step 1: Write Failing Tests

- [ ] **T004** Create test for new get-only property behavior
  - File: `tests/Guacamole.SourceGenerators.Tests/ComputingContextGeneratorTests.cs`
  - Add test: `Generator_WithGetOnlyProperty_GeneratesMutationMethods`
  - Input: Context with `public partial IncrementalValueProvider<string> Name { get; }`
  - Expected: Generated code contains `SetName()` method
  - **Status**: MUST FAIL initially (proving old behavior)

- [ ] **T005** Create test for new property-with-setter behavior
  - File: `tests/Guacamole.SourceGenerators.Tests/ComputingContextGeneratorTests.cs`
  - Add test: `Generator_WithPropertyWithSetter_DoesNotGenerateMutationMethods`
  - Input: Context with `public partial IncrementalValueProvider<int> Count { get; set; }`
  - Expected: Generated code does NOT contain `SetCount()` method
  - **Status**: MUST FAIL initially (proving old behavior)

- [ ] **T006** Run tests to verify they fail
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests --filter "Category=NewBehavior"`
  - **Expected**: Both T004 and T005 tests fail
  - **If tests pass**: Implementation is already done (unexpected)

### Step 2: Implement Logic Inversion

- [ ] **T007** Invert PropertyAnalyzer classification condition
  - File: `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs`
  - Line 66-73: Change condition
  - **OLD**: `if (property.SetMethod != null) { inputProperties.Add(propertyInfo); }`
  - **NEW**: `if (property.SetMethod == null) { inputProperties.Add(propertyInfo); }`
  - Also update comments explaining new behavior
  - **Validation**: Code compiles without errors

- [ ] **T008** Verify PropertyAnalyzer tests now pass
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests --filter "FullyQualifiedName~PropertyAnalyzer"`
  - **Expected**: Tests from T004-T005 now pass
  - **If tests fail**: Logic change incorrect, review T007

---

## Phase 3.3: Generator Verification

**Goal**: Ensure all generators handle new classification correctly

- [ ] **T009** [P] Verify MutationMethodGenerator handles new classification
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/MutationMethodGenerator.cs`
  - Check: Generates methods for InputProperties (now get-only)
  - **Expected**: No code changes needed, generator consumes list correctly
  - **Validation**: Build succeeds

- [ ] **T010** [P] Verify ChangeTypeGenerator handles new classification
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/ChangeTypeGenerator.cs`
  - Check: Generates change records for InputProperties (now get-only)
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

- [ ] **T011** [P] Verify ComputationNodeGenerator handles new classification
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/ComputationNodeGenerator.cs`
  - Check: Creates nodes based on property classification
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

- [ ] **T012** [P] Verify SaveChangesGenerator handles new classification
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/SaveChangesGenerator.cs`
  - Check: Applies changes for InputProperties (now get-only)
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

- [ ] **T013** [P] Verify PropertyGenerator handles new classification
  - File: Review `src/Guacamole.SourceGenerators/CodeGenerators/PropertyGenerator.cs`
  - Check: Implements properties correctly for both lists
  - **Expected**: No code changes needed
  - **Validation**: Build succeeds

---

## Phase 3.4: Snapshot Test Updates

**Goal**: Update all snapshot tests to reflect new generated code

⚠️ **CRITICAL**: Review all diffs carefully before approving

- [ ] **T014** Run all snapshot tests to capture failures
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests`
  - **Expected**: ~13 snapshot tests fail with diffs
  - Capture diff output to review in T015
  - **If tests pass**: No changes detected (unexpected)

- [ ] **T015** Review all snapshot test diffs manually
  - Directory: `tests/Guacamole.SourceGenerators.Tests/Snapshots/`
  - For each .received.cs file:
    - Compare to corresponding .verified.cs
    - Verify get-only properties now have mutation methods
    - Verify properties with setters do NOT have mutation methods
    - Check for unintended changes
  - **Critical**: Do not approve if diffs look wrong
  - **Output**: List of files safe to approve

- [ ] **T016** Approve snapshot updates
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

- [ ] **T017** Verify all generator tests pass after approval
  - Command: `dotnet test tests/Guacamole.SourceGenerators.Tests`
  - **Expected**: All tests pass (0 failures)
  - **If tests fail**: Review snapshot approval, may need to re-approve

---

## Phase 3.5: Examples & Integration Tests

**Goal**: Update examples to use new convention and verify they build

- [ ] **T018** [P] Update CompilerContext example for new convention
  - File: `examples/CompilerContext/MyCompilerContext.cs`
  - Change: Properties that HAD setters (old auto) → NOW get-only
  - Change: Properties that WERE get-only (old manual) → NOW have setters
  - Review: Constructor initialization matches new convention
  - **Validation**: `dotnet build examples/CompilerContext` succeeds

- [ ] **T019** [P] Update FileWatcherContext example for new convention
  - File: `examples/FileWatcherContext/MyFileWatcherContext.cs`
  - Change: Invert setter presence to match new convention
  - Review: Constructor initialization
  - **Validation**: `dotnet build examples/FileWatcherContext` succeeds

- [ ] **T020** Run integration tests in Guacamole.Tests
  - Command: `dotnet test tests/Guacamole.Tests`
  - **Expected**: All tests pass
  - **If failures**: Examples may be using old convention, review T018-T019

---

## Phase 3.6: Documentation

**Goal**: Document breaking change for users

- [ ] **T021** [P] Create migration guide
  - File: `docs/migration/010-property-convention-reversal.md` (NEW)
  - Create `docs/migration/` directory if it doesn't exist
  - Content structure:
    - Overview of breaking change
    - Before/After code examples
    - Step-by-step migration instructions
    - Common patterns and troubleshooting
  - Reference: Use research.md decisions and quickstart.md scenarios
  - **Length**: ~2 pages

- [ ] **T022** [P] Update generated-code-reference.md examples
  - File: `docs/generated-code-reference.md`
  - Lines 24-35: Example CompilerContext
  - Change: Invert setter presence in example
  - Update comments explaining auto vs manual properties
  - **Validation**: Examples match new convention

- [ ] **T023** [P] Add CHANGELOG.md entry
  - File: `CHANGELOG.md`
  - Section: `## [Unreleased] - BREAKING CHANGES` (create if missing)
  - Entry: "Reversed property generation convention: get-only properties now automatically managed, properties with setters now user-managed"
  - Note: Requires MAJOR version bump (2.x → 3.0.0)
  - **Format**: Follow existing CHANGELOG style

---

## Phase 3.7: Final Validation

**Goal**: Ensure everything works end-to-end

- [ ] **T024** Run full test suite
  - Command: `dotnet test`
  - **Expected**: All 153+ tests pass
  - **Critical**: No test failures
  - **If failures**: Review which phase failed, fix before proceeding

- [ ] **T025** Build all projects
  - Command: `dotnet build`
  - **Expected**: All projects build successfully
  - Check: src/Guacamole.SourceGenerators, examples/, tests/
  - **If build fails**: Review compiler errors, likely example misconfiguration

- [ ] **T026** Execute quickstart.md validation
  - File: Follow steps in `specs/010-property-genration-current/quickstart.md`
  - Verify: All 6 quickstart steps succeed
  - **Expected**: Completes in ~6 minutes
  - **If quickstart fails**: Fix issues before marking feature complete

- [ ] **T027** Review all documentation for accuracy
  - Files:
    - `docs/migration/010-property-convention-reversal.md`
    - `docs/generated-code-reference.md`
    - `CHANGELOG.md`
    - `README.md` (verify examples still accurate)
  - Check: No references to old behavior
  - Check: All code examples use new convention
  - **Output**: Documentation is consistent

- [ ] **T028** Run dotnet format
  - Command: `dotnet format`
  - **Expected**: No formatting changes (code already formatted)
  - **If changes**: Commit formatting updates

- [ ] **T029** Final smoke test with new test context
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
