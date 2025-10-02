# Research: CI/CD Pipeline for Automated Testing, Building, and Package Publishing

**Feature**: `008-ci-pipelines-we`
**Date**: 2025-10-02
**Status**: Complete

---

## Research Questions

### 1. GitVersion Configuration for .NET Libraries

**Decision**: Use GitVersion with Mainline mode for semantic versioning

**Rationale**:
- Mainline mode works well with trunk-based development (main branch + short-lived feature branches)
- Automatically increments version based on commit messages following Conventional Commits
- Supports both stable releases (from tags) and pre-release versions (from main branch)
- Industry standard for .NET projects with 5.8k+ stars on GitHub
- Integrates seamlessly with GitHub Actions via `gittools/actions`

**Alternatives Considered**:
- **Manual versioning**: Rejected - error-prone, doesn't scale, requires manual tracking
- **Nerdbank.GitVersioning**: Rejected - more complex setup, overkill for this use case
- **MinVer**: Considered but GitVersion has better GitHub Actions integration

**Configuration Approach**:
```yaml
mode: Mainline
branches:
  main:
    tag: alpha
    increment: Minor
  pull-request:
    tag: pr
```

---

### 2. GitHub Actions Workflow Structure

**Decision**: Two separate workflows - `ci.yml` and `publish.yml`

**Rationale**:
- **Separation of concerns**: CI runs on every push/PR, publishing only on tags/main
- **Security**: Publishing workflow has access to NuGet API key secret, CI workflow doesn't need it
- **Performance**: CI workflow can be optimized for speed, publishing can include additional validation
- **Clarity**: Easier to understand and maintain separate workflows for different purposes

**Alternatives Considered**:
- **Single workflow with conditionals**: Rejected - harder to read, mixes concerns, more complex job dependencies
- **Three workflows (test, build, publish)**: Rejected - over-engineered, increases coordination complexity

**Workflow Triggers**:
- `ci.yml`: `push` to main, `pull_request` to main
- `publish.yml`: `push` with tags matching `v*`, `push` to main branch

---

### 3. Cross-Platform Testing Strategy

**Decision**: Matrix strategy with ubuntu-latest and windows-latest

**Rationale**:
- Tests run in parallel on both platforms, catching platform-specific issues early
- GitHub Actions provides free minutes for public repositories
- Most .NET developers use either Windows or Linux
- macOS testing skipped per spec clarification (reduces CI time and cost)

**Configuration**:
```yaml
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest]
runs-on: ${{ matrix.os }}
```

**Alternatives Considered**:
- **Sequential testing**: Rejected - slower, wastes CI time
- **Linux only**: Rejected - spec requires cross-platform validation
- **Include macOS**: Rejected per user clarification

---

### 4. NuGet Package Validation

**Decision**: Multi-stage validation before publishing

**Rationale**:
- **Stage 1**: `dotnet build` ensures packages build successfully
- **Stage 2**: `dotnet pack` creates NuGet packages
- **Stage 3**: Test installation in clean environment (`dotnet add package`)
- **Stage 4**: Symbol packages validation (ensures debugging works)
- **Stage 5**: NuGet push with skip-duplicate flag

**Validation Steps**:
1. All tests pass (enforced by workflow dependency)
2. Packages build without warnings (TreatWarningsAsErrors=true in csproj)
3. Package metadata complete (version, description, tags, license)
4. Packages can be restored and consumed
5. No duplicate versions on NuGet.org (handled by `--skip-duplicate`)

**Alternatives Considered**:
- **Minimal validation** (just pack and push): Rejected - risk of broken packages
- **NuGet.org validation only**: Rejected - failures happen after push attempt
- **Local NuGet feed testing**: Considered but adds complexity without significant benefit

---

### 5. Workflow Permissions and Security

**Decision**: Minimal permissions with explicit grants

**Rationale**:
- Follow principle of least privilege
- CI workflow: `contents: read` only (no write access)
- Publish workflow: `contents: write` (for creating releases), `packages: write` (for NuGet)
- NuGet API key stored as GitHub repository secret (`NUGET_API_KEY`)
- Secrets never logged or exposed in workflow outputs

**Security Measures**:
- Secret masking in logs (automatic GitHub feature)
- No secrets in CI workflow (only in publish workflow)
- Tag-based publishing requires protected tags (recommended but not enforced by workflow)
- Package validation before publishing prevents malformed packages

**Alternatives Considered**:
- **Broad permissions** (write access everywhere): Rejected - security risk
- **Azure Key Vault for secrets**: Rejected - overkill for single API key, adds complexity
- **OIDC federation**: Considered for future enhancement, not needed for initial implementation

---

### 6. Build Caching Strategy

**Decision**: Cache NuGet packages and build outputs

**Rationale**:
- Reduces CI run time by 40-60% (based on GitHub Actions documentation)
- NuGet package cache: `~/.nuget/packages` (Linux), `~\.nuget\packages` (Windows)
- Cache key includes lock file hash for invalidation on dependency changes
- GitHub provides 10GB cache per repository (sufficient for this project)

**Configuration**:
```yaml
- uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
    restore-keys: |
      ${{ runner.os }}-nuget-
```

**Alternatives Considered**:
- **No caching**: Rejected - wastes time and resources
- **Custom Docker images with pre-installed dependencies**: Rejected - maintenance overhead
- **Restore-only without cache**: Rejected - slower than caching

---

### 7. Test Reporting and Notifications

**Decision**: Built-in GitHub Actions test reporting with PR status checks

**Rationale**:
- GitHub automatically displays test results in PR checks
- Failed tests block PR merging (if branch protection enabled)
- Test summary visible in Actions tab
- No additional notification service needed for initial implementation

**Test Output Format**:
- XUnit XML output via `dotnet test --logger "trx;LogFileName=test-results.trx"`
- GitHub Actions test reporter action for enhanced visualization
- Failed test details visible in workflow logs

**Future Enhancements** (not in initial scope):
- Slack notifications for failed builds on main
- Email notifications for package publishing
- Test coverage reporting via Coverlet + Codecov

**Alternatives Considered**:
- **External test reporting service** (e.g., Test Reporter): Rejected - unnecessary complexity
- **Custom notification service**: Rejected - GitHub native features sufficient
- **No test reporting**: Rejected - doesn't meet FR-003 requirement

---

### 8. Pre-release Package Strategy

**Decision**: Publish pre-release packages from main branch with `-alpha.{build}` suffix

**Rationale**:
- Allows testing of main branch packages before creating stable release
- GitVersion automatically appends build metadata
- NuGet.org supports pre-release packages with semantic version suffixes
- Users can opt-in to pre-release packages explicitly
- Stable releases only published from git tags (manual trigger)

**Version Format Examples**:
- Stable: `1.2.3` (from tag `v1.2.3`)
- Pre-release: `1.3.0-alpha.47` (from main branch, build #47)
- PR build: `1.3.0-pr.123.5` (from PR #123, commit 5)

**Publishing Rules**:
- Tags matching `v*`: Publish stable package (version without suffix)
- Push to main: Publish pre-release package (version with `-alpha` suffix)
- PRs: Build and test only, no publishing

**Alternatives Considered**:
- **No pre-release packages**: Rejected - spec requires pre-release publishing from main
- **Manual approval for pre-release**: Rejected - defeats purpose of automation
- **Different pre-release suffixes** (beta, rc): Deferred - can add later based on need

---

## Technology Decisions Summary

| Technology | Version | Purpose |
|------------|---------|---------|
| GitHub Actions | Latest | CI/CD platform |
| GitVersion | 6.x | Semantic versioning |
| .NET SDK | 10.0.x | Build and test |
| XUnit | v3 | Test framework (constitutional) |
| NuGet CLI | Latest (in SDK) | Package publishing |
| actions/checkout | v4 | Repository checkout |
| actions/setup-dotnet | v4 | .NET SDK installation |
| actions/cache | v4 | Build caching |
| gittools/actions/gitversion | v3 | GitVersion action |

---

## Open Questions Resolved

All technical unknowns from the specification have been researched and resolved:

1. ✅ **GitVersion configuration**: Mainline mode with conventional commits
2. ✅ **Workflow structure**: Two workflows (ci.yml, publish.yml)
3. ✅ **Cross-platform testing**: Matrix with Linux + Windows
4. ✅ **Package validation**: Multi-stage validation pipeline
5. ✅ **Security**: Minimal permissions, secret management via GitHub Secrets
6. ✅ **Caching**: NuGet package caching for performance
7. ✅ **Test reporting**: GitHub native test reporting with PR checks
8. ✅ **Pre-release strategy**: Alpha packages from main, stable from tags

---

## Next Steps

Phase 0 complete. Ready for Phase 1: Design & Contracts.

Phase 1 will produce:
- `data-model.md`: CI/CD entities and their relationships
- `contracts/`: Workflow contract specifications (YAML schemas)
- `quickstart.md`: Manual validation steps for CI/CD setup
- Updated `CLAUDE.md`: CI/CD context for future development
