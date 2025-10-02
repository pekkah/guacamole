# Quickstart: CI/CD Pipeline Validation

**Feature**: `008-ci-pipelines-we`
**Purpose**: Manual validation steps to verify CI/CD pipeline is working correctly
**Prerequisites**: Workflows deployed to `.github/workflows/`, GitVersion.yml at repository root

---

## Validation Checklist

### ✅ Phase 1: Setup Validation

#### 1.1 Verify Files Exist

```bash
# Check workflow files exist
ls -la .github/workflows/ci.yml
ls -la .github/workflows/publish.yml

# Check GitVersion config exists
ls -la GitVersion.yml

# Expected: All files exist with correct permissions
```

**Success Criteria**: All three files exist

---

#### 1.2 Verify NuGet API Key Secret

```bash
# Check in GitHub repository settings
# Settings → Secrets and variables → Actions → Repository secrets
# Expected: NUGET_API_KEY secret exists
```

**Manual Steps**:
1. Navigate to repository on GitHub.com
2. Go to Settings → Secrets and variables → Actions
3. Verify `NUGET_API_KEY` appears in repository secrets list
4. (Do NOT view the secret value - GitHub won't show it anyway)

**Success Criteria**: Secret exists and is not expired

---

### ✅ Phase 2: CI Workflow Validation

#### 2.1 Trigger CI on Pull Request

**Steps**:
1. Create a new branch: `git checkout -b test/ci-validation`
2. Make a trivial change: `echo "# CI Test" >> README.md`
3. Commit: `git commit -am "test: validate CI workflow"`
4. Push: `git push origin test/ci-validation`
5. Create PR on GitHub targeting main branch

**Expected Results**:
- CI workflow starts automatically
- Test job runs on ubuntu-latest and windows-latest (parallel)
- All 148 tests pass on both platforms
- Build job runs after tests pass
- NuGet packages created as artifacts
- PR shows green check mark ✅

**Success Criteria** (FR-001, FR-002, FR-021):
- Workflow completes in < 10 minutes
- All tests pass
- Test results visible in PR checks
- Build artifacts uploaded

---

#### 2.2 Verify Test Failure Blocks Merge

**Steps**:
1. On the test branch, intentionally break a test:
   ```csharp
   // In any test file
   [Fact]
   public void TemporaryFailingTest()
   {
       Assert.True(false, "Intentional failure for CI validation");
   }
   ```
2. Commit and push: `git commit -am "test: add failing test"`
3. Check CI workflow run

**Expected Results**:
- CI workflow fails
- Test job shows failure
- Build job doesn't run (blocked by test failure)
- PR shows red ❌
- PR cannot be merged (if branch protection enabled)

**Success Criteria** (FR-004, FR-019):
- Workflow fails with non-zero exit code
- Failed test details visible in logs
- PR blocked from merging

---

#### 2.3 Verify Cross-Platform Testing

**Steps**:
1. View the failed CI workflow run
2. Click on "Test" job
3. Expand matrix section

**Expected Results**:
- Two matrix jobs listed: ubuntu-latest, windows-latest
- Both jobs ran (even though both failed)
- Same test failure on both platforms

**Success Criteria** (FR-021):
- Tests ran on both Linux and Windows
- Matrix jobs ran in parallel
- Both platforms tested identically

---

#### 2.4 Clean Up Test

**Steps**:
1. Remove the failing test
2. Commit and push: `git commit -am "test: remove failing test"`
3. Wait for CI to pass
4. Close the test PR (don't merge)
5. Delete test branch: `git branch -D test/ci-validation`

---

### ✅ Phase 3: GitVersion Validation

#### 3.1 Verify Version Calculation

**Steps**:
1. View any successful CI workflow run
2. Expand "Build" job → "Calculate Version" step
3. Check the output

**Expected Output**:
```
SemVer: 1.X.0-alpha.N
NuGetVersion: 1.X.0-alpha.N
IsPreRelease: true
```
(Where X and N are actual numbers)

**Success Criteria** (FR-016, FR-017):
- Version follows semantic versioning
- Pre-release tag is `-alpha`
- Version auto-increments based on commits

---

#### 3.2 Verify PR Version Format

**Steps**:
1. Create another test PR (any trivial change)
2. View CI workflow run
3. Check GitVersion output in Build job

**Expected Output**:
```
SemVer: 1.X.0-pr.N.M
NuGetVersion: 1.X.0-pr.N.M
```
(Where N is PR number, M is commit count)

**Success Criteria**:
- Version contains `-pr.{number}`
- Version distinct from main branch versions

---

### ✅ Phase 4: Publish Workflow Validation (Pre-Release)

#### 4.1 Trigger Pre-Release Publish

**Steps**:
1. Ensure main branch has no uncommitted changes
2. Make a trivial change on main: `echo "test" >> .gitignore`
3. Commit: `git commit -am "test: trigger pre-release publish"`
4. Push to main: `git push origin main`
5. Navigate to Actions tab on GitHub

**Expected Results**:
- Publish workflow starts automatically
- Test job runs on both platforms
- All tests pass
- Publish job runs after tests
- GitVersion calculates version `1.X.0-alpha.N`
- Packages published to NuGet.org with pre-release tag

**Success Criteria** (FR-006, FR-011, FR-014):
- Workflow completes successfully
- Packages visible on NuGet.org with `-alpha` suffix
- Packages marked as "pre-release" on NuGet.org

---

#### 4.2 Verify Pre-Release Package on NuGet.org

**Steps**:
1. Navigate to https://www.nuget.org/packages/Guacamole.Core
2. Check "Include prerelease" checkbox
3. Find the latest alpha version

**Expected Results**:
- Package version `1.X.0-alpha.N` visible
- Package shows "prerelease" badge
- Package metadata correct (description, authors, license, tags)
- Package downloadable

**Success Criteria** (FR-008, FR-014):
- Pre-release package published
- Metadata complete and correct
- Package can be consumed

---

#### 4.3 Test Pre-Release Package Installation

**Steps**:
1. Create a new test console app:
   ```bash
   mkdir /tmp/test-nuget
   cd /tmp/test-nuget
   dotnet new console
   ```

2. Add pre-release package:
   ```bash
   dotnet add package Guacamole.Core --version 1.X.0-alpha.N --prerelease
   ```

3. Build the app:
   ```bash
   dotnet build
   ```

**Expected Results**:
- Package restores successfully
- No dependency conflicts
- Build succeeds

**Success Criteria** (FR-009):
- Package can be restored
- Package can be consumed in projects

---

### ✅ Phase 5: Publish Workflow Validation (Stable Release)

#### 5.1 Create Stable Release Tag

**Steps**:
1. Checkout main branch: `git checkout main`
2. Pull latest: `git pull origin main`
3. Create annotated tag: `git tag -a v0.1.0 -m "Release 0.1.0"`
4. Push tag: `git push origin v0.1.0`
5. Navigate to Actions tab

**Expected Results**:
- Publish workflow starts automatically (triggered by tag)
- Test job runs on both platforms
- All tests pass
- GitVersion calculates version `0.1.0` (no suffix)
- Packages published to NuGet.org as stable
- GitHub Release created

**Success Criteria** (FR-006, FR-011):
- Workflow completes successfully
- Packages published with stable version
- No pre-release suffix

---

#### 5.2 Verify Stable Package on NuGet.org

**Steps**:
1. Navigate to https://www.nuget.org/packages/Guacamole.Core
2. Uncheck "Include prerelease"
3. Find version `0.1.0`

**Expected Results**:
- Package version `0.1.0` visible
- No "prerelease" badge
- Package listed in stable versions
- Package metadata correct

**Success Criteria** (FR-008):
- Stable package published
- Metadata complete
- Package listed (not pre-release)

---

#### 5.3 Verify GitHub Release Created

**Steps**:
1. Navigate to repository Releases page
2. Find "Release 0.1.0"

**Expected Results**:
- Release exists with tag `v0.1.0`
- Release title is "Release 0.1.0"
- Release is not marked as pre-release
- Release assets may include packages (optional)

**Success Criteria**:
- GitHub Release created for stable tag
- Release matches package version

---

#### 5.4 Test Duplicate Version Handling

**Steps**:
1. Make a trivial change on main
2. Commit: `git commit -am "test: trigger duplicate"`
3. Create same tag again (shouldn't work):
   ```bash
   git tag -a v0.1.0 -m "Duplicate tag"
   git push origin v0.1.0
   ```

**Expected Results**:
- Git rejects duplicate tag push
- OR: If tag somehow pushes, workflow uses `--skip-duplicate` flag
- Workflow completes successfully (doesn't fail on duplicate)

**Success Criteria** (FR-013):
- Duplicate versions handled gracefully
- No workflow failures due to duplicates

---

### ✅ Phase 6: Security Validation

#### 6.1 Verify Secret Masking

**Steps**:
1. View any publish workflow run
2. Expand "Publish to NuGet.org" step
3. Check logs for API key exposure

**Expected Results**:
- API key value never visible in logs
- GitHub automatically masks secret
- Logs show: `--api-key ***`

**Success Criteria** (FR-012):
- Secret never exposed in logs
- Automatic masking works

---

#### 6.2 Verify Minimal Permissions

**Steps**:
1. Check `.github/workflows/ci.yml` permissions section
2. Check `.github/workflows/publish.yml` permissions section

**Expected Results**:
- CI workflow: `contents: read` only
- Publish workflow: `contents: write`, `packages: write`
- No unnecessary permissions

**Success Criteria**:
- Minimal permissions principle followed
- No write access in CI workflow

---

### ✅ Phase 7: Performance Validation

#### 7.1 Verify CI Run Time

**Steps**:
1. View recent CI workflow runs
2. Check total duration

**Expected Results**:
- Total run time < 10 minutes
- Test jobs run in parallel (both platforms at once)
- Caching reduces restore time

**Success Criteria** (Performance Goal):
- CI completes in < 10 minutes
- No unnecessary sequential steps

---

#### 7.2 Verify Publish Run Time

**Steps**:
1. View recent publish workflow runs
2. Check total duration

**Expected Results**:
- Total run time < 15 minutes
- Tests run in parallel
- Package build sequential (single platform)

**Success Criteria** (Performance Goal):
- Publish completes in < 15 minutes

---

### ✅ Phase 8: Rollback Validation

#### 8.1 Test Package Unlisting (Optional)

**Steps**:
1. Choose a test pre-release version to unlist (e.g., `1.X.0-alpha.N`)
2. Use NuGet CLI to unlist:
   ```bash
   dotnet nuget delete Guacamole.Core 1.X.0-alpha.N \
     --api-key $NUGET_API_KEY \
     --source https://api.nuget.org/v3/index.json \
     --non-interactive
   ```

**Expected Results**:
- Package becomes unlisted on NuGet.org
- Package still exists (not deleted)
- Package not discoverable in searches
- Existing projects using it not affected

**Success Criteria** (FR-015):
- Rollback capability exists
- Unlisting works correctly

---

## Validation Summary

After completing all phases:

- [ ] Files exist (.github/workflows/, GitVersion.yml)
- [ ] NuGet API key secret configured
- [ ] CI triggers on PRs and main pushes
- [ ] Tests run on Linux + Windows
- [ ] Failed tests block merging
- [ ] GitVersion calculates correct versions
- [ ] Pre-release packages publish from main
- [ ] Stable packages publish from tags
- [ ] GitHub releases created for tags
- [ ] Duplicate versions handled gracefully
- [ ] Secrets masked in logs
- [ ] Minimal permissions enforced
- [ ] CI runs in < 10 minutes
- [ ] Publish runs in < 15 minutes
- [ ] Packages can be consumed
- [ ] Rollback capability works

---

## Troubleshooting

### Workflow doesn't trigger

**Check**:
- Workflow files in `.github/workflows/` directory
- YAML syntax valid (use GitHub's workflow editor)
- Trigger conditions match (push to main, PR to main, tag v*)

---

### Tests fail in CI but pass locally

**Check**:
- Platform-specific code (Windows vs Linux)
- Environment variables
- File path separators
- Case-sensitive file systems

---

### Package publish fails

**Check**:
- NuGet API key valid and not expired
- API key has "Push packages" permission
- Package version unique (not duplicate)
- All tests passed before publish

---

### GitVersion shows wrong version

**Check**:
- `fetch-depth: 0` in checkout action (full git history required)
- GitVersion.yml syntax correct
- Tags follow `v*` pattern
- Branch name matches configuration

---

## Next Steps

After validation complete:
1. Delete test tags: `git push origin --delete v0.1.0`
2. Delete test packages on NuGet.org (or leave as pre-release)
3. Update CLAUDE.md with CI/CD context
4. Document any issues found during validation
5. Ready for production use

---

## Maintenance

### Regular Tasks

**Monthly**:
- Check NuGet API key expiration
- Review workflow run times (optimize if degrading)
- Check NuGet.org download statistics

**Per Release**:
- Create git tag for stable release
- Verify package published successfully
- Verify GitHub release created
- Announce release to users

**As Needed**:
- Rotate NuGet API key (update secret)
- Update workflow actions versions (actions/checkout@v4 → v5)
- Update GitVersion version spec (6.x → 7.x)
