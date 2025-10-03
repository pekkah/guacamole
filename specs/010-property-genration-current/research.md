# Research: Reverse Property Generation Logic

**Feature**: 010-property-genration-current
**Date**: 2025-10-03

## Research Questions

### 1. Current PropertyAnalyzer Implementation

**Question**: What is the exact logic for property classification?

**Location**: `src/Guacamole.SourceGenerators/PropertyAnalyzer.cs:66-73`

**Current Implementation**:
```csharp
if (property.SetMethod != null)
{
    inputProperties.Add(propertyInfo);
}
else
{
    derivedProperties.Add(propertyInfo);
}
```

**Decision**: Invert the condition - properties WITHOUT setters become input properties (auto-managed), properties WITH setters become derived properties (user-managed).

**Rationale**:
- Get-only properties signal "framework should manage this"
- Properties with setters signal "I'll handle initialization/changes myself"
- This matches developer intuition from standard C# property patterns

**Alternatives Considered**:
- Add attribute to explicitly mark auto vs manual → Rejected: Adds API surface, requires migration
- Make it opt-in via configuration → Rejected: Increases complexity, unclear default
- Keep current behavior → Rejected: Counterintuitive, confuses developers

### 2. Affected Code Generators

**Question**: Which generators consume property classification results?

**Findings**:

1. **MutationMethodGenerator** (`CodeGenerators/MutationMethodGenerator.cs`)
   - Generates: `Add*()`, `Remove*()`, `Set*()`, `Replace*()` methods
   - Currently: Only for `InputProperties` (properties with setters)
   - **Impact**: Will now generate for get-only properties

2. **ChangeTypeGenerator** (`CodeGenerators/ChangeTypeGenerator.cs`)
   - Generates: Change record types (`*Change.Add`, `*Change.Remove`, etc.)
   - Currently: Only for `InputProperties`
   - **Impact**: Change records now for get-only properties

3. **ComputationNodeGenerator** (`CodeGenerators/ComputationNodeGenerator.cs`)
   - Generates: Computation node classes (connects to graph)
   - Currently: Different nodes for input vs derived
   - **Impact**: Node generation logic remains same, just inverted classification

4. **SaveChangesGenerator** (`CodeGenerators/SaveChangesGenerator.cs`)
   - Generates: `SaveChanges()` method that applies pending changes
   - Currently: Processes changes from `InputProperties` only
   - **Impact**: SaveChanges now applies changes to get-only properties

5. **PropertyGenerator** (`CodeGenerators/PropertyGenerator.cs`)
   - Generates: Property getter/setter implementations
   - Currently: Implements both input and derived properties
   - **Impact**: Implementation pattern remains same, classification inverted

**Decision**: All 5 generators must be verified but likely require NO code changes - they consume the classification lists correctly, only the classification logic itself changes.

**Rationale**: Generators are designed to work with "properties that need infrastructure" (InputProperties list). By inverting which list properties go into, generators automatically adapt.

### 3. Snapshot Test Strategy

**Question**: How to update Verify snapshot tests efficiently?

**Tool**: Verify.SourceGenerators library (https://github.com/VerifyTests/Verify.SourceGenerators)

**Workflow**:
1. Make code change (invert PropertyAnalyzer logic)
2. Run tests → All snapshot tests fail (generated code changed)
3. Review diffs in test output/CI
4. If diffs are expected: Run tests with accept flag or manually approve `.verified.cs` files
5. Commit updated snapshots

**Affected Test Files** (estimated based on generator test count):
- `ComputingContextGeneratorTests.cs` → 3 snapshots
- `PropertyGeneratorTests.cs` → 2 snapshots
- `MutationMethodGeneratorTests.cs` → 2 snapshots
- `ChangeTypeGeneratorTests.cs` → 2 snapshots
- `ComputationNodeGeneratorTests.cs` → 2 snapshots
- `SaveChangesGeneratorTests.cs` → 2 snapshots
- **Total**: ~13 snapshot files in `Snapshots/` directory

**Approval Process**:
- Option 1: Run tests with `VerifySettings.AutoVerify()` in debug mode
- Option 2: Manually move `.received.cs` → `.verified.cs`
- Option 3: Use Verify test runner with approve all command

**Decision**: Use Option 3 (test runner approve) for batch approval after manual review of diffs.

**Rationale**: Batch operation is faster, but manual review ensures no unintended changes.

### 4. Breaking Change Documentation

**Question**: What documentation updates are required for this breaking change?

**Required Documents**:

1. **Migration Guide** (`docs/migration/010-property-convention-reversal.md`)
   - Content: Step-by-step migration instructions
   - Format: Before/After code examples
   - Includes: Common patterns, troubleshooting
   - Audience: Existing Guacamole users

2. **CHANGELOG.md Update**
   - Section: `## [Unreleased] - BREAKING CHANGES`
   - Content: "Reversed property generation convention: get-only properties now auto-managed, setter properties now user-managed"
   - Versioning Impact: MAJOR version bump required (2.x.x → 3.0.0)

3. **docs/generated-code-reference.md**
   - Update: All example contexts showing property declarations
   - Change: Swap setter/get-only in examples
   - Location: Lines 24-35 (example context definition)

4. **examples/ Projects**
   - Update: `examples/CompilerContext/MyCompilerContext.cs`
   - Update: `examples/FileWatcherContext/MyFileWatcherContext.cs`
   - Change: Invert which properties have setters based on intended behavior

**Decision**: Create migration guide first, then update other docs referencing it.

**Rationale**: Migration guide is primary user-facing document, other docs can reference it.

## Research Summary

**Key Findings**:
1. Logic change is localized to PropertyAnalyzer.cs:66-73 (7 lines of code)
2. All 5 generators consume classification correctly - no generator code changes needed
3. ~13 snapshot tests will fail and need approval after manual review
4. Breaking change requires migration guide + CHANGELOG + doc updates + example updates

**Implementation Approach**:
1. Invert PropertyAnalyzer condition (simple boolean flip)
2. Run tests to generate new snapshots
3. Review all snapshot diffs carefully
4. Approve snapshots
5. Write migration documentation
6. Update examples and reference docs

**Risk Assessment**:
- **Low Risk**: Core logic change is trivial (boolean inversion)
- **Medium Risk**: Snapshot approval requires careful review to avoid unintended changes
- **Low Risk**: Documentation updates are straightforward

**Estimated Effort**:
- Code change: 15 minutes
- Snapshot review & approval: 30 minutes
- Documentation: 2 hours
- Testing & validation: 30 minutes
- **Total**: ~3.5 hours

## Technical Decisions Summary

| Decision | Rationale |
|----------|-----------|
| Invert SetMethod != null condition | Matches developer intuition (get-only = auto, setter = manual) |
| No generator code changes | Generators already consume classification correctly |
| Batch snapshot approval after review | Efficient while maintaining code review quality |
| Migration guide first | Primary user-facing document for breaking change |
| MAJOR version bump | Follows semver for breaking changes |
