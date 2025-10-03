# Quickstart: Reverse Property Generation Logic

**Feature**: 010-property-genration-current
**Purpose**: Verify the property classification logic reversal works end-to-end
**Time**: ~5 minutes

## Prerequisites

- .NET 10 RC1 SDK installed
- Guacamole repository cloned
- On branch `010-property-genration-current`

## Quick Validation Steps

### Step 1: Verify Core Logic Change

```bash
# Navigate to repository root
cd /home/pekkah/p/guacamole

# Check PropertyAnalyzer.cs for the inverted logic
cat src/Guacamole.SourceGenerators/PropertyAnalyzer.cs | grep -A5 "if (property.SetMethod"
```

**Expected Output** (after change):
```csharp
if (property.SetMethod != null)
{
    derivedProperties.Add(propertyInfo);  // User-managed
}
else
{
    inputProperties.Add(propertyInfo);    // Auto-managed
}
```

### Step 2: Run Generator Tests

```bash
# Build source generators
dotnet build src/Guacamole.SourceGenerators

# Run generator tests (expect failures initially)
dotnet test tests/Guacamole.SourceGenerators.Tests --filter "Category=SnapshotTest"
```

**Expected Output** (before snapshot approval):
- ❌ ~13 test failures showing snapshot mismatches
- Diff output showing generated code differences

### Step 3: Review Generated Code Diffs

```bash
# View snapshot diff for a specific test
ls tests/Guacamole.SourceGenerators.Tests/Snapshots/ComputingContextGeneratorTests/

# Example: Check get-only property test
cat tests/Guacamole.SourceGenerators.Tests/Snapshots/ComputingContextGeneratorTests/*.GetOnlyProperty*.verified.cs
```

**Expected Diff Pattern**:
```diff
+ public void SetItems(IEnumerable<Item> items)  // NEW: mutation method for get-only
- public void SetItems(IEnumerable<Item> items)  // OLD: was for property with setter
```

### Step 4: Approve Snapshots (if diffs are correct)

```bash
# Option A: Approve all snapshots
dotnet test tests/Guacamole.SourceGenerators.Tests -- --update-snapshots

# Option B: Manual approval (if above doesn't work)
find tests/Guacamole.SourceGenerators.Tests/Snapshots -name "*.received.cs" \
    -exec sh -c 'mv "$1" "${1%.received.cs}.verified.cs"' _ {} \;

# Re-run tests
dotnet test tests/Guacamole.SourceGenerators.Tests
```

**Expected Output**: ✅ All tests pass

### Step 5: Verify Examples Build

```bash
# Update example to use new convention
# (Examples should already be updated as part of implementation)

# Build CompilerContext example
dotnet build examples/CompilerContext

# Build FileWatcherContext example
dotnet build examples/FileWatcherContext
```

**Expected Output**: ✅ Both examples build successfully

### Step 6: Integration Test

Create a minimal test context and verify behavior:

```bash
# Create test file
cat > /tmp/test_context.cs << 'EOF'
using Guacamole;
using Guacamole.Core.Providers;

[ComputingContext]
public partial class TestContext : ComputingContextBase
{
    // Get-only → Should generate SetName()
    public partial IncrementalValueProvider<string> Name { get; }

    // Has setter → Should NOT generate SetCount()
    public partial IncrementalValueProvider<int> Count { get; set; }

    public TestContext() : base()
    {
        InitializeComputationNodes();
    }

    partial void InitializeComputationNodes();
}
EOF

# Compile and check generated code
dotnet build /tmp/test_project
# Review generated files in obj/Debug/net10.0/generated/
```

**Expected Generated Code**:
- ✅ `SetName(string value)` method exists
- ❌ `SetCount(int value)` method does NOT exist
- ✅ `NameChange` record types exist
- ❌ `CountChange` record types do NOT exist

## Acceptance Criteria

All of these must be true:

- [x] PropertyAnalyzer.cs logic inverted (SetMethod check reversed)
- [x] All 13 snapshot tests updated and passing
- [x] Examples build without errors
- [x] Get-only properties generate mutation methods
- [x] Properties with setters do NOT generate mutation methods
- [x] Full test suite passes (153 tests)
- [x] Documentation updated (migration guide exists)

## Troubleshooting

### Problem: Tests still fail after snapshot approval

**Solution**: Clear obj/Debug directories and rebuild:
```bash
find . -type d -name "obj" -exec rm -rf {} +
dotnet clean
dotnet build
dotnet test
```

### Problem: Generated code doesn't match expectations

**Solution**: Check source generator cache:
```bash
# Force regeneration
dotnet clean
rm -rf ~/.nuget/packages/guacamole.sourcegenerators
dotnet build
```

### Problem: Examples don't build after updating

**Solution**: Verify property declarations match new convention:
- Properties that HAD setters (old input) should NOW be get-only
- Properties that WERE get-only (old derived) should NOW have setters
- Update constructor initialization accordingly

## Success Validation

Run this command to verify everything works:

```bash
#!/bin/bash
set -e

echo "✓ Building solution..."
dotnet build

echo "✓ Running all tests..."
dotnet test --no-build

echo "✓ Building examples..."
dotnet build examples/CompilerContext
dotnet build examples/FileWatcherContext

echo "✅ Quickstart validation complete!"
```

**Expected**: All commands succeed, no errors

## Next Steps

After quickstart validation passes:

1. Review migration guide: `docs/migration/010-property-convention-reversal.md`
2. Check CHANGELOG.md for breaking change entry
3. Verify generated-code-reference.md examples updated
4. Ready for merge to main

## Time Tracking

- Step 1 (verify logic): ~30 seconds
- Step 2 (run tests): ~1 minute
- Step 3 (review diffs): ~2 minutes
- Step 4 (approve snapshots): ~1 minute
- Step 5 (examples): ~30 seconds
- Step 6 (integration): ~1 minute
- **Total**: ~6 minutes

## Notes

- This quickstart assumes implementation is complete
- All steps should succeed in sequence
- If any step fails, check implementation before proceeding
- Snapshot approval is the most critical step - review diffs carefully
