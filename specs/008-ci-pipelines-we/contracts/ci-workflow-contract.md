# Contract: CI Workflow

**File**: `.github/workflows/ci.yml`
**Purpose**: Run tests and build packages on every push to main and every pull request
**Triggers**: `push` to main, `pull_request` to main

---

## Workflow Specification

### Triggers

```yaml
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
```

**Requirements**:
- FR-001: Must trigger automatically on push to main
- FR-001: Must trigger automatically on pull request creation/update

---

### Jobs

#### Job 1: `test`

**Purpose**: Run all tests on matrix of operating systems

**Matrix Strategy**:
```yaml
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest]
runs-on: ${{ matrix.os }}
```

**Requirements**:
- FR-021: Must run on both Linux and Windows
- Must run matrix jobs in parallel (for performance)

**Steps**:

1. **Checkout code**
   ```yaml
   - uses: actions/checkout@v4
     with:
       fetch-depth: 0  # Required for GitVersion
   ```
   - Must fetch full git history for GitVersion

2. **Setup .NET**
   ```yaml
   - uses: actions/setup-dotnet@v4
     with:
       dotnet-version: '10.0.x'
   ```
   - FR-002: Must use .NET 10.0

3. **Cache NuGet packages**
   ```yaml
   - uses: actions/cache@v4
     with:
       path: ~/.nuget/packages
       key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
       restore-keys: |
         ${{ runner.os }}-nuget-
   ```
   - Performance optimization (not functional requirement)

4. **Restore dependencies**
   ```yaml
   - run: dotnet restore
   ```
   - Must restore before build/test

5. **Build solution**
   ```yaml
   - run: dotnet build --no-restore --configuration Release
   ```
   - Must build in Release configuration
   - Constitutional requirement: TreatWarningsAsErrors=true

6. **Run tests**
   ```yaml
   - run: dotnet test --no-build --configuration Release --logger "trx;LogFileName=test-results.trx"
   ```
   - FR-003: Must report test results (via trx logger)
   - FR-004: Must fail job if any test fails (default behavior)
   - Must not rebuild (--no-build for speed)

7. **Upload test results**
   ```yaml
   - uses: actions/upload-artifact@v4
     if: always()
     with:
       name: test-results-${{ matrix.os }}
       path: '**/test-results.trx'
   ```
   - FR-005: Must display test results in pull requests
   - Must upload even if tests fail (if: always())

**Exit Criteria**:
- Job succeeds if all tests pass
- Job fails if any test fails (FR-004)
- Job fails if build fails

---

#### Job 2: `build`

**Purpose**: Build NuGet packages (validation only, not published)

**Dependencies**:
```yaml
needs: test
```
- Must wait for tests to pass before building

**Runs On**: `ubuntu-latest` (single OS, not matrix)

**Steps**:

1. **Checkout code**
   ```yaml
   - uses: actions/checkout@v4
     with:
       fetch-depth: 0
   ```

2. **Setup .NET**
   ```yaml
   - uses: actions/setup-dotnet@v4
     with:
       dotnet-version: '10.0.x'
   ```

3. **Install GitVersion**
   ```yaml
   - uses: gittools/actions/gitversion/setup@v3
     with:
       versionSpec: '6.x'
   ```
   - FR-016: Must use GitVersion for versioning

4. **Calculate version**
   ```yaml
   - uses: gittools/actions/gitversion/execute@v3
     id: gitversion
   ```
   - FR-017: Must automatically increment version

5. **Restore dependencies**
   ```yaml
   - run: dotnet restore
   ```

6. **Build packages**
   ```yaml
   - run: dotnet pack --no-restore --configuration Release -p:Version=${{ steps.gitversion.outputs.nuGetVersion }}
   ```
   - FR-007: Must build packages for all packable projects
   - FR-008: Must include metadata (from .csproj files)
   - FR-016: Must use GitVersion-calculated version

7. **Upload packages as artifacts**
   ```yaml
   - uses: actions/upload-artifact@v4
     with:
       name: nuget-packages
       path: '**/*.nupkg'
   ```
   - FR-010: Must store build artifacts (retention per GitHub defaults)

**Exit Criteria**:
- Job succeeds if all packages build successfully
- Job fails if any package build fails (FR-020)

---

### Permissions

```yaml
permissions:
  contents: read
  pull-requests: write  # For test result comments
```

**Requirements**:
- Minimal permissions (security best practice)
- No write access to repository contents
- Pull request write for posting test results

---

### Success Criteria

**Workflow succeeds if**:
- All tests pass on both Linux and Windows (FR-021)
- All packages build successfully
- No warnings (TreatWarningsAsErrors=true)

**Workflow fails if**:
- Any test fails on either platform (FR-004)
- Build fails
- Package creation fails

---

## Validation Tests

These tests validate the CI workflow contract (to be implemented during Phase 3):

### Test 1: CI triggers on push to main
- **Given**: Code is pushed to main branch
- **When**: Push completes
- **Then**: CI workflow starts automatically

### Test 2: CI triggers on pull request
- **Given**: Pull request is created targeting main
- **When**: PR is created
- **Then**: CI workflow starts automatically

### Test 3: Tests run on both platforms
- **Given**: CI workflow is triggered
- **When**: Workflow runs
- **Then**: Test job runs on both ubuntu-latest and windows-latest

### Test 4: Workflow fails if tests fail
- **Given**: At least one test fails
- **When**: CI workflow runs
- **Then**: Workflow fails with non-zero exit code

### Test 5: Workflow fails if build fails
- **Given**: Build command fails
- **When**: CI workflow runs
- **Then**: Workflow fails before running tests

### Test 6: Test results visible in PR
- **Given**: CI workflow completes (pass or fail)
- **When**: Viewing pull request
- **Then**: Test results displayed in PR checks section

### Test 7: Build artifacts uploaded
- **Given**: Build job completes successfully
- **When**: Viewing workflow run
- **Then**: NuGet packages available as artifacts

### Test 8: Version calculated by GitVersion
- **Given**: Build job runs
- **When**: GitVersion step executes
- **Then**: Version number follows semantic versioning

---

## Example Workflow Run

**Scenario**: Developer creates PR #45 with bug fix

1. Developer pushes commit `abc123` to branch `fix/bug-45`
2. Developer creates PR #45 targeting main
3. GitHub triggers CI workflow
4. Workflow checks out code at `abc123`
5. Workflow sets up .NET 10.0 on ubuntu-latest and windows-latest (parallel)
6. Workflow restores NuGet packages (using cache)
7. Workflow builds solution in Release mode
8. Workflow runs all 148 tests on both platforms
9. All tests pass on both platforms
10. Workflow uploads test results (2 artifacts: ubuntu, windows)
11. Build job waits for test job to complete
12. Build job calculates version: `1.3.0-pr.45.1`
13. Build job creates NuGet packages with PR version
14. Build job uploads packages as artifact
15. Workflow completes successfully
16. PR shows green check mark ✅
17. Test results displayed in PR checks tab

**Expected Duration**: < 10 minutes (FR performance goal)

---

## Notes

- This workflow does NOT publish packages (see publish-workflow-contract.md)
- This workflow runs on every push/PR for fast feedback
- Cache reduces restore time by ~50%
- Matrix strategy catches platform-specific issues early
