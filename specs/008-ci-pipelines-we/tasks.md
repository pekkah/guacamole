# Tasks: CI/CD Pipeline for Automated Testing, Building, and Package Publishing

> **Reconciliation (2026-11-08, #45)**: The release flow was replaced by tag-based publishing that reuses CI (#28) and nuget.org trusted publishing (#35, #37); the per-task **Status** lines under T002 and T006–T012 record what was validated. Status markers: see [specs/README.md](../README.md).

**Input**: Design documents from `/specs/008-ci-pipelines-we/`
**Prerequisites**: plan.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

---

## Execution Summary

This task list implements automated CI/CD pipelines using GitHub Actions, GitVersion for semantic versioning, and NuGet package publishing. The implementation follows infrastructure-as-code principles where workflows are YAML files tested through actual GitHub Actions runs.

**Key Deliverables**:
- GitHub Actions workflows (`.github/workflows/ci.yml`, `.github/workflows/publish.yml`)
- GitVersion configuration (`GitVersion.yml`)
- Repository secrets configuration (NuGet API key)
- Documentation updates (README badges, CLAUDE.md)

**Testing Strategy**:
Unlike traditional TDD, infrastructure code is validated through:
1. YAML syntax validation (local)
2. Actual workflow runs (integration testing)
3. Manual quickstart validation steps

---

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

---

## Phase 3.1: Repository Setup

### T001: Create GitHub Actions workflow directory structure
**Description**: Create `.github/workflows/` directory at repository root for GitHub Actions workflow files.

**Commands**:
```bash
mkdir -p .github/workflows
```

**Success Criteria**:
- Directory `.github/workflows/` exists
- Directory has correct permissions (755)

**Files Created**: `.github/workflows/` (directory)

---

### T002: Configure NuGet API key secret in GitHub repository

**Status (2026-11-08, #45)**: superseded: publishing now uses nuget.org trusted publishing (`NuGet/login@v1`, #35, #37); no `NUGET_API_KEY` secret is stored. See `docs/ci-cd.md`.
**Description**: Add NuGet.org API key as encrypted GitHub repository secret for package publishing authentication.

**Manual Steps**:
1. Navigate to https://www.nuget.org → Account Settings → API Keys
2. Create new API key with scopes: "Push packages", "Unlist packages"
3. Copy API key value (shown only once)
4. Navigate to GitHub repository → Settings → Secrets and variables → Actions
5. Click "New repository secret"
6. Name: `NUGET_API_KEY`
7. Value: [paste API key]
8. Click "Add secret"

**Success Criteria**:
- Secret `NUGET_API_KEY` visible in repository secrets list
- Secret value masked (not displayed)
- API key has "Push packages" permission

**Documentation Reference**: `contracts/publish-workflow-contract.md` (Secrets section)

---

## Phase 3.2: GitVersion Configuration

### T003: Implement GitVersion configuration file
**Description**: Create `GitVersion.yml` at repository root with Mainline mode configuration for semantic versioning based on git history and tags.

**File**: `GitVersion.yml`

**Implementation**:
```yaml
mode: Mainline
next-version: 1.0.0
tag-prefix: v
tag-pre-release-weight: 60000

branches:
  main:
    tag: alpha
    increment: Minor
    regex: ^main$
    track-merge-target: false
    is-mainline: true

  pull-request:
    tag: pr
    increment: None
    regex: ^(pull|pull\-requests|pr)[/-]
    tag-number-pattern: '[/-](?<number>\d+)'

  feature:
    tag: alpha
    increment: None
    regex: ^features?[/-]

commit-message-incrementing: Enabled
major-version-bump-message: '\+semver:\s?(breaking|major)'
minor-version-bump-message: '\+semver:\s?(feature|minor)'
patch-version-bump-message: '\+semver:\s?(fix|patch)'
no-bump-message: '\+semver:\s?(none|skip)'

ignore:
  sha: []
```

**Success Criteria**:
- File exists at repository root
- YAML syntax valid (validated by GitVersion)
- Main branch configured with `alpha` pre-release tag
- Tag prefix set to `v`

**Documentation Reference**: `contracts/gitversion-config-contract.md`

**Validation**: Run `gitversion` locally (if installed) or wait for workflow run

---

## Phase 3.3: CI Workflow Implementation

### T004: Implement CI workflow for automated testing and building
**Description**: Create `.github/workflows/ci.yml` with test matrix (Linux + Windows) and build job for NuGet packages.

**File**: `.github/workflows/ci.yml`

**Implementation**:
```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read
  pull-requests: write

env:
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  test:
    name: Test on ${{ matrix.os }}
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]

    steps:
      - name: Checkout code
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-

      - name: Restore dependencies
        run: dotnet restore

      - name: Build solution
        run: dotnet build --no-restore --configuration Release

      - name: Run tests
        run: dotnet test --no-build --configuration Release --logger "trx;LogFileName=test-results.trx"

      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results-${{ matrix.os }}
          path: '**/test-results.trx'

  build:
    name: Build NuGet packages
    runs-on: ubuntu-latest
    needs: test

    steps:
      - name: Checkout code
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install GitVersion
        uses: gittools/actions/gitversion/setup@v3
        with:
          versionSpec: '6.x'

      - name: Calculate version
        uses: gittools/actions/gitversion/execute@v3
        id: gitversion

      - name: Display version
        run: |
          echo "SemVer: ${{ steps.gitversion.outputs.semVer }}"
          echo "NuGetVersion: ${{ steps.gitversion.outputs.nuGetVersion }}"

      - name: Restore dependencies
        run: dotnet restore

      - name: Build packages
        run: dotnet pack --no-restore --configuration Release -p:Version=${{ steps.gitversion.outputs.nuGetVersion }}

      - name: Upload packages
        uses: actions/upload-artifact@v4
        with:
          name: nuget-packages
          path: '**/*.nupkg'
```

**Success Criteria**:
- File exists at `.github/workflows/ci.yml`
- YAML syntax valid
- Triggers on push to main and pull requests
- Test job runs on both ubuntu-latest and windows-latest
- Build job waits for test job to complete (needs: test)
- GitVersion calculates version number

**Documentation Reference**: `contracts/ci-workflow-contract.md`

**Requirements**: FR-001, FR-002, FR-003, FR-004, FR-005, FR-007, FR-008, FR-016, FR-017, FR-021

---

## Phase 3.4: Publish Workflow Implementation

### T005: Implement publish workflow for NuGet package publishing
**Description**: Create `.github/workflows/publish.yml` with test matrix and publish job that publishes to NuGet.org from tags (stable) and main branch (pre-release).

**File**: `.github/workflows/publish.yml`

**Implementation**:
```yaml
name: Publish

on:
  push:
    tags:
      - 'v*'
    branches:
      - main

permissions:
  contents: write
  packages: write

env:
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  test:
    name: Test on ${{ matrix.os }}
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]

    steps:
      - name: Checkout code
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-

      - name: Restore dependencies
        run: dotnet restore

      - name: Build solution
        run: dotnet build --no-restore --configuration Release

      - name: Run tests
        run: dotnet test --no-build --configuration Release --logger "trx;LogFileName=test-results.trx"

      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results-${{ matrix.os }}
          path: '**/test-results.trx'

  publish:
    name: Publish to NuGet.org
    runs-on: ubuntu-latest
    needs: test

    steps:
      - name: Checkout code
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install GitVersion
        uses: gittools/actions/gitversion/setup@v3
        with:
          versionSpec: '6.x'

      - name: Calculate version
        uses: gittools/actions/gitversion/execute@v3
        id: gitversion

      - name: Display version
        run: |
          echo "SemVer: ${{ steps.gitversion.outputs.semVer }}"
          echo "NuGetVersion: ${{ steps.gitversion.outputs.nuGetVersion }}"
          echo "IsPreRelease: ${{ steps.gitversion.outputs.preReleaseTag }}"

      - name: Restore dependencies
        run: dotnet restore

      - name: Build packages
        run: dotnet pack --no-restore --configuration Release -p:Version=${{ steps.gitversion.outputs.nuGetVersion }}

      - name: Publish to NuGet.org
        run: dotnet nuget push **/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate

      - name: Create GitHub Release
        if: startsWith(github.ref, 'refs/tags/')
        uses: actions/create-release@v1
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        with:
          tag_name: ${{ github.ref_name }}
          release_name: Release ${{ steps.gitversion.outputs.semVer }}
          draft: false
          prerelease: false
```

**Success Criteria**:
- File exists at `.github/workflows/publish.yml`
- YAML syntax valid
- Triggers on tags matching `v*` and pushes to main
- Test job identical to CI workflow
- Publish job waits for test job (needs: test)
- Uses `NUGET_API_KEY` secret
- `--skip-duplicate` flag prevents errors on duplicate versions
- GitHub release created only for tags

**Documentation Reference**: `contracts/publish-workflow-contract.md`

**Requirements**: FR-006, FR-011, FR-012, FR-013, FR-014, FR-016, FR-017, FR-019, FR-020

---

## Phase 3.5: Validation & Testing

### T006: Validate CI workflow with test pull request

**Status (2026-11-08, #45)**: done: CI runs on every pull request on Linux and Windows and runs `pack` (e.g. PR runs for #38 and #39 succeeded).
**Description**: Create a test pull request to trigger the CI workflow and verify all jobs complete successfully (corresponds to quickstart Phase 2.1).

**Manual Steps**:
1. Create test branch: `git checkout -b test/ci-validation`
2. Make trivial change: `echo "# CI Test" >> README.md`
3. Commit: `git commit -am "test: validate CI workflow"`
4. Push: `git push origin test/ci-validation`
5. Create PR on GitHub targeting main
6. Wait for CI workflow to complete
7. Verify: Tests pass on both Linux and Windows
8. Verify: Build job creates packages
9. Verify: PR shows green check mark
10. Close PR and delete branch

**Success Criteria**:
- CI workflow triggers automatically on PR creation
- Test job runs on both ubuntu-latest and windows-latest
- All tests pass (148 tests currently)
- Build job completes successfully
- Test results visible in PR checks
- Workflow completes in < 10 minutes

**Documentation Reference**: `quickstart.md` (Phase 2.1)

**Requirements**: FR-001, FR-005, FR-021

---

### T007: Validate test failure blocks workflow

**Status (2026-11-08, #45)**: done: `pack` has `needs: test` in `.github/workflows/ci.yml`, so a failing test skips packing. Merge blocking depends on branch protection, which is a repository setting.
**Description**: Verify that failing tests block the build job and PR merge (corresponds to quickstart Phase 2.2).

**Manual Steps**:
1. On test branch, add failing test:
   ```csharp
   [Fact]
   public void TemporaryFailingTest()
   {
       Assert.True(false, "Intentional failure for CI validation");
   }
   ```
2. Commit and push
3. Verify CI workflow fails
4. Verify build job doesn't run (blocked)
5. Verify PR shows red ❌
6. Remove failing test, commit, push
7. Verify CI passes

**Success Criteria**:
- Failed test causes workflow to fail
- Build job skipped when test job fails
- Failed test details visible in logs
- PR cannot be merged when tests fail

**Documentation Reference**: `quickstart.md` (Phase 2.2)

**Requirements**: FR-004, FR-019

---

### T008: Validate GitVersion version calculation

**Status (2026-11-08, #45)**: done: GitVersion runs in every CI `pack` job (`GitVersion.yml`: `alpha` on `main`, `pr` on pull requests). Tag builds now take the version from the tag (see `docs/ci-cd.md`).
**Description**: Verify GitVersion correctly calculates semantic versions for different scenarios (PR, main branch, tags) (corresponds to quickstart Phase 3).

**Manual Steps**:
1. View CI workflow run on test PR
2. Check GitVersion output in Build job logs
3. Verify version format: `1.X.0-pr.N.M`
4. Merge a commit to main (or use existing)
5. Check version format on main: `1.X.0-alpha.N`
6. Verify version increments based on commits

**Success Criteria**:
- PR versions contain `-pr.{number}` suffix
- Main branch versions contain `-alpha.{number}` suffix
- Versions follow semantic versioning
- Versions auto-increment based on commit count

**Documentation Reference**: `quickstart.md` (Phase 3), `contracts/gitversion-config-contract.md`

**Requirements**: FR-016, FR-017

---

### T009: Validate pre-release package publishing

**Status (2026-11-08, #45)**: done under the original flow (Publish runs on pushes to `main` published `-alpha` prereleases, e.g. run 24), then superseded: pushes to `main` no longer publish (#28). Prereleases from `main` are published by a manual Publish run.
**Description**: Trigger pre-release package publishing from main branch and verify packages appear on NuGet.org (corresponds to quickstart Phase 4).

**Manual Steps**:
1. Push a commit to main branch
2. Wait for Publish workflow to complete
3. Navigate to NuGet.org packages
4. Verify packages published with `-alpha` suffix
5. Verify packages marked as "pre-release"
6. Test package installation in a new project

**Success Criteria**:
- Publish workflow triggers on main branch push
- All tests pass before publishing
- Packages published to NuGet.org with pre-release tag
- Package metadata correct (description, authors, license)
- Packages can be restored and consumed

**Documentation Reference**: `quickstart.md` (Phase 4), `contracts/publish-workflow-contract.md`

**Requirements**: FR-006, FR-008, FR-009, FR-011, FR-014, FR-019

---

### T010: Validate stable release publishing

**Status (2026-11-08, #45)**: open: no `v*` tag has been pushed yet, so the tag-based release (nuget.org + GitHub release, now with trusted publishing) has not run end to end.
**Description**: Create a stable release tag and verify packages publish without pre-release suffix and GitHub release is created (corresponds to quickstart Phase 5).

**Manual Steps**:
1. Checkout main: `git checkout main && git pull`
2. Create tag: `git tag -a v0.1.0 -m "Release 0.1.0"`
3. Push tag: `git push origin v0.1.0`
4. Wait for Publish workflow to complete
5. Verify packages on NuGet.org (version `0.1.0`, no suffix)
6. Verify GitHub Release created
7. Test package installation

**Success Criteria**:
- Publish workflow triggers on tag push
- Version is stable (no pre-release suffix)
- Packages published to NuGet.org as stable
- GitHub Release created with correct version
- Packages downloadable and consumable

**Documentation Reference**: `quickstart.md` (Phase 5), `contracts/publish-workflow-contract.md`

**Requirements**: FR-006, FR-011, FR-018

---

### T011: Validate duplicate version handling

**Status (2026-11-08, #45)**: done: `dotnet nuget push ... --skip-duplicate` in `.github/workflows/publish.yml`.
**Description**: Verify that attempting to publish a duplicate version is handled gracefully with `--skip-duplicate` flag (corresponds to quickstart Phase 5.4).

**Manual Steps**:
1. Attempt to create duplicate tag (git will reject)
2. OR: Verify `--skip-duplicate` in workflow YAML
3. Review publish workflow logs for skip-duplicate behavior

**Success Criteria**:
- Workflow doesn't fail on duplicate versions
- `--skip-duplicate` flag present in publish command
- Logs show appropriate skip message if duplicate detected

**Documentation Reference**: `quickstart.md` (Phase 5.4), `contracts/publish-workflow-contract.md`

**Requirements**: FR-013

---

### T012: Validate security and permissions

**Status (2026-11-08, #45)**: superseded: there is no long-lived API key to mask any more (trusted publishing, #35). Permissions: CI and the Publish workflow default to `contents: read`; only the publish job has `contents: write` + `id-token: write` (no `packages: write`).
**Description**: Verify secrets are masked in logs and workflows have minimal permissions (corresponds to quickstart Phase 6).

**Manual Steps**:
1. View Publish workflow run logs
2. Check "Publish to NuGet.org" step
3. Verify API key is masked (shows as `***`)
4. Review workflow YAML permissions sections
5. Verify CI has `contents: read` only
6. Verify Publish has `contents: write`, `packages: write`

**Success Criteria**:
- NuGet API key never visible in logs
- GitHub automatically masks secret
- CI workflow has minimal read-only permissions
- Publish workflow has only required write permissions

**Documentation Reference**: `quickstart.md` (Phase 6), `contracts/publish-workflow-contract.md`

**Requirements**: FR-012

---

## Phase 3.6: Documentation & Polish

### T013 [P]: Update README with CI/CD badges
**Description**: Add GitHub Actions workflow status badges to README.md to display CI and publish workflow status.

**File**: `README.md`

**Implementation**: Add badges at top of README:
```markdown
# Guacamole

[![CI](https://github.com/pekkah/guacamole/actions/workflows/ci.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/ci.yml)
[![Publish](https://github.com/pekkah/guacamole/actions/workflows/publish.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/v/Guacamole.Core.svg)](https://www.nuget.org/packages/Guacamole.Core/)
```

**Success Criteria**:
- Badges appear at top of README
- Badges link to workflow runs and NuGet package
- Badges display correct status (passing/failing)

**Documentation Reference**: Standard GitHub badges practice

---

### T014 [P]: Add CI/CD documentation to repository
**Description**: Create `docs/ci-cd.md` explaining the CI/CD pipeline setup, how to create releases, and troubleshooting.

**File**: `docs/ci-cd.md`

**Content Outline**:
- Overview of CI/CD pipeline
- Workflow triggers (PR, main, tags)
- How to create a stable release (tag creation)
- How to test pre-release packages
- Versioning strategy (GitVersion)
- Troubleshooting common issues
- Secret management (NuGet API key)

**Success Criteria**:
- Documentation file exists and is comprehensive
- Covers all workflow scenarios
- Includes troubleshooting section
- Links to relevant contracts and quickstart

---

### T015 [P]: Clean up test artifacts and validate final state
**Description**: Delete test branches, tags, and pre-release packages created during validation. Verify all workflows are in working state.

**Manual Steps**:
1. Delete test branches: `git branch -D test/ci-validation`
2. Delete test tags (if any): `git push origin --delete v0.1.0`
3. Optionally unlist test pre-release packages on NuGet.org
4. Run final CI check with a clean PR
5. Verify all validation checklist items complete

**Success Criteria**:
- No test branches or tags remain
- Repository clean
- All workflows passing
- All quickstart validation phases complete

**Documentation Reference**: `quickstart.md` (Next Steps section)

---

## Dependencies

### Sequential Dependencies
```
T001 (Create workflows dir)
  ↓
T003, T004, T005 (GitVersion, CI, Publish workflows)
  ↓
T006, T007, T008 (CI validation)
  ↓
T009, T010, T011, T012 (Publish validation)
  ↓
T013, T014, T015 (Documentation and cleanup)
```

### Parallel Opportunities
- **T002** (NuGet secret) can run in parallel with T001, T003
- **T013, T014, T015** (Documentation tasks) can run in parallel with each other

### Critical Path
T001 → T003 → T004 → T006 → T009 → T010 (minimum path to working CI/CD)

---

## Parallel Execution Examples

### Phase 3.6: Documentation Tasks (T013-T015)
All documentation tasks can run in parallel as they touch different files:

```bash
# Launch all documentation tasks together:
# Task 1: Update README badges
# Task 2: Create CI/CD documentation
# Task 3: Clean up test artifacts
```

---

## Validation Checklist

Before marking tasks complete, verify:

- [x] All contracts have corresponding workflow files
  - ci-workflow-contract.md → `.github/workflows/ci.yml` (T004)
  - publish-workflow-contract.md → `.github/workflows/publish.yml` (T005)
  - gitversion-config-contract.md → `GitVersion.yml` (T003)

- [x] All workflows tested through actual runs
  - CI workflow validated (T006, T007, T008)
  - Publish workflow validated (T009, T010, T011)
  - Security validated (T012)

- [x] All quickstart phases covered
  - Phase 1: Setup (T001, T002)
  - Phase 2: CI validation (T006, T007)
  - Phase 3: GitVersion (T008)
  - Phase 4: Pre-release (T009)
  - Phase 5: Stable release (T010, T011)
  - Phase 6: Security (T012)

- [x] Documentation tasks touch different files
  - T013: README.md
  - T014: docs/ci-cd.md
  - T015: Cleanup (no file writes)

- [x] Each task specifies exact file paths or manual steps

- [x] No parallel tasks modify the same file

---

## Notes

**Infrastructure Testing**: Unlike traditional TDD where tests are written as code, infrastructure validation happens through:
1. YAML syntax validation (can use `yamllint` locally)
2. GitHub Actions dry-run validation (automatic on push)
3. Actual workflow execution (integration testing)
4. Manual quickstart validation steps

**No Contract Test Tasks**: Infrastructure-as-code doesn't have traditional contract tests. Instead, the contracts define the expected workflow behavior, and validation tasks (T006-T012) verify actual behavior matches contracts.

**Commit Strategy**:
- T001-T005: Commit after each workflow file creation
- T006-T012: Document validation results (no code commits)
- T013-T015: Commit documentation updates

**Rollback**: If any validation task fails:
1. Review workflow logs for errors
2. Fix workflow YAML or configuration
3. Re-run validation task
4. Reference troubleshooting section in quickstart.md

---

## Execution Status

**Phase Status**:
- [x] Phase 3.1: Repository Setup (T001-T002)
- [x] Phase 3.2: GitVersion Configuration (T003)
- [x] Phase 3.3: CI Workflow Implementation (T004)
- [x] Phase 3.4: Publish Workflow Implementation (T005)
- [ ] Phase 3.5: Validation & Testing (T006-T012) - *Requires actual workflow runs on GitHub* — open: T006–T009 and T011 done, T012 superseded; T010 (first tag release) still open
- [x] Phase 3.6: Documentation & Polish (T013-T015)

**Implementation Status**: ✅ Core implementation complete
**Next Steps**: Push changes to GitHub and execute validation tasks T006-T012

Use `/implement` or execute tasks manually in dependency order.
