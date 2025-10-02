# Contract: Publish Workflow

**File**: `.github/workflows/publish.yml`
**Purpose**: Publish NuGet packages to NuGet.org
**Triggers**: `push` with tags `v*`, `push` to main branch

---

## Workflow Specification

### Triggers

```yaml
on:
  push:
    tags:
      - 'v*'    # Stable releases (e.g., v1.2.3)
    branches:
      - main    # Pre-release packages
```

**Requirements**:
- FR-006: Must trigger on git tag push for stable releases
- FR-006: Must trigger on main branch push for pre-releases
- Must NOT trigger on pull requests (security: no publishing from PRs)

---

### Jobs

#### Job 1: `test`

**Purpose**: Run all tests before publishing (same as CI workflow test job)

**Matrix Strategy**:
```yaml
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest]
runs-on: ${{ matrix.os }}
```

**Requirements**:
- FR-019: Must block publishing if tests fail
- FR-021: Must test on both Linux and Windows

**Steps**: (identical to CI workflow test job)

1. Checkout code (fetch-depth: 0)
2. Setup .NET 10.0
3. Cache NuGet packages
4. Restore dependencies
5. Build solution (Release)
6. Run tests with trx logger
7. Upload test results

**Exit Criteria**:
- Job succeeds if all tests pass on both platforms
- Job fails if any test fails → blocks publish job

---

#### Job 2: `publish`

**Purpose**: Build and publish NuGet packages to NuGet.org

**Dependencies**:
```yaml
needs: test
```
- FR-019: Must wait for tests to pass

**Runs On**: `ubuntu-latest` (single OS, publishing doesn't need matrix)

**Steps**:

1. **Checkout code**
   ```yaml
   - uses: actions/checkout@v4
     with:
       fetch-depth: 0
   ```
   - Full history required for GitVersion

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

4. **Calculate version**
   ```yaml
   - uses: gittools/actions/gitversion/execute@v3
     id: gitversion
   ```
   - FR-016: Use GitVersion for version calculation
   - FR-017: Auto-increment based on git history
   - For tags: Returns stable version (e.g., `1.2.3`)
   - For main: Returns pre-release version (e.g., `1.3.0-alpha.47`)

5. **Restore dependencies**
   ```yaml
   - run: dotnet restore
   ```

6. **Build packages**
   ```yaml
   - run: dotnet pack --no-restore --configuration Release -p:Version=${{ steps.gitversion.outputs.nuGetVersion }}
   ```
   - FR-007: Build all packable projects
   - FR-008: Include metadata from .csproj
   - FR-020: Fail if build fails

7. **Validate packages**
   ```yaml
   - run: dotnet nuget verify **/*.nupkg
   ```
   - FR-020: Validate package contents before publishing
   - Ensures packages are well-formed

8. **Publish to NuGet.org**
   ```yaml
   - run: dotnet nuget push **/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
   ```
   - FR-011: Publish stable releases from tags
   - FR-011: Publish pre-releases from main
   - FR-012: Authenticate with API key from GitHub Secret
   - FR-013: Skip duplicate versions (--skip-duplicate flag)
   - FR-014: Pre-release suffix added by GitVersion

9. **Create GitHub Release** (tags only)
   ```yaml
   - uses: actions/create-release@v1
     if: startsWith(github.ref, 'refs/tags/')
     with:
       tag_name: ${{ github.ref_name }}
       release_name: Release ${{ steps.gitversion.outputs.semVer }}
       draft: false
       prerelease: false
   ```
   - Create GitHub release for tagged versions
   - Not required for pre-releases from main

**Exit Criteria**:
- Job succeeds if all packages published successfully
- Job succeeds (with skip) if package version already exists (--skip-duplicate)
- Job fails if NuGet push fails for other reasons

---

### Environment Variables

```yaml
env:
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true
```

**Purpose**: Optimize CI performance, disable telemetry

---

### Permissions

```yaml
permissions:
  contents: write   # For creating GitHub releases
  packages: write   # For NuGet publishing
```

**Requirements**:
- FR-012: Must have access to NuGet API key secret
- Write access to contents for release creation
- Minimal permissions (security best practice)

---

### Secrets

**Required Secret**: `NUGET_API_KEY`

**Setup Instructions**:
1. Generate API key on NuGet.org (account settings)
2. Add to GitHub repository secrets (Settings → Secrets → Actions)
3. Name: `NUGET_API_KEY`
4. Scope: Push packages, Unlist packages (recommended)
5. Expiration: Set expiration reminder (e.g., 1 year)

**Security Requirements**:
- FR-012: Store as encrypted GitHub repository secret
- Never log or expose in workflow output (automatic masking)
- Rotate periodically (recommend: annually)

---

### Version Strategy

**Stable Releases** (from tags):
```
Tag: v1.2.3
GitVersion Output: 1.2.3
NuGet Version: 1.2.3
Published as: Guacamole.Core 1.2.3 (stable)
```

**Pre-Release** (from main branch):
```
Branch: main
Commit: 47th commit since last release
GitVersion Output: 1.3.0-alpha.47
NuGet Version: 1.3.0-alpha.47
Published as: Guacamole.Core 1.3.0-alpha.47 (pre-release)
```

**PR Builds** (not published):
```
Branch: feature/xyz
PR: #123
GitVersion Output: 1.3.0-pr.123.5
NuGet Version: N/A (not published)
```

**Requirements**:
- FR-014: Pre-release suffix for main branch builds
- FR-016: GitVersion for version calculation
- FR-017: Auto-increment based on commits
- FR-018: Version uniqueness (--skip-duplicate)

---

## Validation Tests

### Test 1: Publish triggers on tag push
- **Given**: Tag `v1.2.3` is pushed
- **When**: Push completes
- **Then**: Publish workflow starts automatically

### Test 2: Publish triggers on main push
- **Given**: Code is pushed to main branch
- **When**: Push completes
- **Then**: Publish workflow starts automatically

### Test 3: Publish blocked if tests fail
- **Given**: At least one test fails
- **When**: Publish workflow runs
- **Then**: Publish job does not execute (needs: test failed)

### Test 4: Stable version from tag
- **Given**: Tag `v1.2.3` pushed
- **When**: Publish workflow calculates version
- **Then**: Version is `1.2.3` (no pre-release suffix)

### Test 5: Pre-release version from main
- **Given**: Commit pushed to main branch
- **When**: Publish workflow calculates version
- **Then**: Version includes `-alpha` suffix

### Test 6: Package published to NuGet.org
- **Given**: Publish job completes successfully
- **When**: Checking NuGet.org
- **Then**: Package is visible and downloadable

### Test 7: Duplicate version skipped
- **Given**: Version `1.2.3` already exists on NuGet.org
- **When**: Attempting to publish `1.2.3` again
- **Then**: Workflow succeeds with skip message (--skip-duplicate)

### Test 8: GitHub release created for tag
- **Given**: Tag `v1.2.3` published successfully
- **When**: Viewing GitHub releases
- **Then**: Release `1.2.3` exists with package assets

### Test 9: API key authentication works
- **Given**: Valid NuGet API key in secrets
- **When**: Publishing package
- **Then**: Authentication succeeds, package published

### Test 10: Invalid API key fails gracefully
- **Given**: Invalid or expired NuGet API key
- **When**: Publishing package
- **Then**: Workflow fails with authentication error

---

## Example Workflow Runs

### Example 1: Stable Release

**Scenario**: Maintainer creates stable release v1.2.3

1. Maintainer creates and pushes tag `v1.2.3` on main branch
2. GitHub triggers publish workflow (tag trigger)
3. Workflow checks out code at tag `v1.2.3`
4. Workflow runs all tests on Linux + Windows
5. All tests pass
6. GitVersion calculates version: `1.2.3`
7. Workflow builds packages with version `1.2.3`
8. Workflow validates packages (dotnet nuget verify)
9. Workflow publishes packages to NuGet.org:
   - Guacamole.Core 1.2.3
   - Guacamole 1.2.3
   - Guacamole.SourceGenerators 1.2.3
10. Workflow creates GitHub release `Release 1.2.3`
11. Users can now `dotnet add package Guacamole.Core --version 1.2.3`

**Expected Duration**: < 15 minutes

---

### Example 2: Pre-Release from Main

**Scenario**: Automated pre-release after PR merge

1. PR #45 merged to main branch
2. GitHub triggers publish workflow (main branch trigger)
3. Workflow checks out latest main commit
4. Workflow runs all tests on Linux + Windows
5. All tests pass
6. GitVersion calculates version: `1.3.0-alpha.47`
7. Workflow builds packages with version `1.3.0-alpha.47`
8. Workflow validates packages
9. Workflow publishes pre-release packages to NuGet.org:
   - Guacamole.Core 1.3.0-alpha.47 (pre-release)
   - Guacamole 1.3.0-alpha.47 (pre-release)
   - Guacamole.SourceGenerators 1.3.0-alpha.47 (pre-release)
10. Users can test with `dotnet add package Guacamole.Core --version 1.3.0-alpha.47`

**Expected Duration**: < 15 minutes

---

## Rollback Strategy

**Problem**: Defective package published to NuGet.org

**Solution Options**:

1. **Unlist package** (FR-015):
   ```bash
   dotnet nuget delete Guacamole.Core 1.2.3 --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --non-interactive
   ```
   - Package becomes unlisted (not deleted)
   - Existing users not affected
   - New users cannot discover it

2. **Publish fixed version**:
   - Fix bug, create new tag `v1.2.4`
   - Publish fixed version
   - Communicate upgrade path to users

3. **Yank version** (not supported by NuGet.org):
   - NuGet.org doesn't support yanking
   - Use unlist instead

**Recommendation**: Unlist defective version + publish fix

---

## Notes

- This workflow requires tests to pass before publishing (FR-019)
- `--skip-duplicate` prevents errors when version already exists (FR-013)
- Pre-release packages allow testing main branch without creating tags
- GitHub releases created only for stable tags (not for pre-releases)
- API key requires "Push packages" permission (minimum)
- Workflow runs after CI workflow (tests run twice: once in CI, once here)
  - Alternative: Could reuse CI test results, but running twice is safer
