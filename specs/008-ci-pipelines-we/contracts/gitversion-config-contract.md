# Contract: GitVersion Configuration

**File**: `GitVersion.yml`
**Purpose**: Configure GitVersion for semantic versioning strategy
**Location**: Repository root

---

## Configuration Specification

### Basic Settings

```yaml
mode: Mainline
next-version: 1.0.0
```

**Requirements**:
- FR-016: Use GitVersion for version calculation
- FR-017: Auto-increment based on git history
- Mainline mode: Optimized for trunk-based development (main + short-lived branches)

---

### Branch Configuration

#### Main Branch

```yaml
branches:
  main:
    tag: alpha
    increment: Minor
    regex: ^main$
    track-merge-target: false
    is-mainline: true
```

**Behavior**:
- Commits to main increment **minor** version
- Pre-release tag: `-alpha.{CommitsSinceVersionSource}`
- Example: `1.3.0-alpha.47` (47 commits since last release)

**Requirements**:
- FR-014: Pre-release packages from main branch
- FR-017: Auto-increment minor version

---

#### Release Tags

```yaml
branches:
  main:
    tag: ''  # No tag for releases
```

**Behavior**:
- Tags matching `v*` produce stable versions
- Tag `v1.2.3` → Version `1.2.3` (no suffix)
- No pre-release tag for tagged commits

**Requirements**:
- FR-006: Stable packages from git tags
- FR-016: Semantic versioning

---

#### Pull Request Branches

```yaml
branches:
  pull-request:
    tag: pr
    increment: None
    regex: ^(pull|pull\-requests|pr)[/-]
    tag-number-pattern: '[/-](?<number>\d+)'
```

**Behavior**:
- PR branches get `-pr.{number}.{CommitsSinceTag}` suffix
- Example: `1.3.0-pr.45.5` (PR #45, 5th commit)
- No version increment (increment: None)

**Requirements**:
- Distinct versions for PR builds
- Not published to NuGet (CI workflow only)

---

#### Feature Branches

```yaml
branches:
  feature:
    tag: alpha
    increment: None
    regex: ^features?[/-]
```

**Behavior**:
- Feature branches get `-alpha.{BranchName}.{CommitsSinceTag}`
- Example: `1.3.0-alpha.fix-bug.3`
- No version increment (inherit from main)

**Requirements**:
- Distinct versions for feature branch builds
- Not published to NuGet

---

### Commit Message Increment

```yaml
commit-message-incrementing: Enabled
major-version-bump-message: '\+semver:\s?(breaking|major)'
minor-version-bump-message: '\+semver:\s?(feature|minor)'
patch-version-bump-message: '\+semver:\s?(fix|patch)'
no-bump-message: '\+semver:\s?(none|skip)'
```

**Behavior**:
- Commit message controls version increment
- `+semver: major` → Increment major version
- `+semver: minor` → Increment minor version
- `+semver: patch` → Increment patch version
- `+semver: none` → No increment

**Example Commit Messages**:
```
feat: add new feature +semver: minor
fix: correct bug +semver: patch
refactor: breaking change +semver: major
docs: update README +semver: none
```

**Requirements**:
- FR-017: Auto-increment based on commits
- Conventional commits for clarity

---

### Tag Configuration

```yaml
tag-prefix: v
tag-pre-release-weight: 60000
```

**Behavior**:
- Tags must start with `v` (e.g., `v1.2.3`)
- Pre-release weight affects version sorting

**Requirements**:
- FR-006: Tags trigger stable releases
- Consistent tag format

---

### Ignore Configuration

```yaml
ignore:
  sha: []
  commits-before: 2025-10-02T00:00:00
```

**Behavior**:
- Ignore commits before specified date (optional)
- Ignore specific commit SHAs (optional)

**Purpose**: Clean version history if migrating from manual versioning

---

## Version Calculation Examples

### Stable Release from Tag

**Scenario**: Tag `v1.2.3` on main branch

```
Input:
- Tag: v1.2.3
- Branch: main
- Commits since tag: 0

Output:
- SemVer: 1.2.3
- NuGetVersion: 1.2.3
- FullSemVer: 1.2.3
- IsPreRelease: false
```

---

### Pre-Release from Main

**Scenario**: 47 commits on main since last tag `v1.2.0`

```
Input:
- Latest tag: v1.2.0
- Branch: main
- Commits since tag: 47
- Next increment: Minor

Output:
- SemVer: 1.3.0-alpha.47
- NuGetVersion: 1.3.0-alpha.47
- FullSemVer: 1.3.0-alpha.47+Branch.main.Sha.abc1234
- IsPreRelease: true
```

---

### Pull Request Build

**Scenario**: PR #45, 5th commit

```
Input:
- Latest tag: v1.2.0
- Branch: pull/45/merge
- PR number: 45
- Commits: 5

Output:
- SemVer: 1.3.0-pr.45.5
- NuGetVersion: 1.3.0-pr.45.5
- FullSemVer: 1.3.0-pr.45.5+Branch.pull-45-merge.Sha.def5678
- IsPreRelease: true
```

---

### Feature Branch Build

**Scenario**: Feature branch `feature/add-caching`, 3 commits

```
Input:
- Latest tag: v1.2.0
- Branch: feature/add-caching
- Commits: 3

Output:
- SemVer: 1.3.0-alpha.add-caching.3
- NuGetVersion: 1.3.0-alpha.add-caching.3
- FullSemVer: 1.3.0-alpha.add-caching.3+Branch.feature-add-caching.Sha.ghi9012
- IsPreRelease: true
```

---

## Validation Tests

### Test 1: Stable version from tag
- **Given**: Tag `v1.2.3` exists on main
- **When**: GitVersion runs at tag
- **Then**: NuGetVersion is `1.2.3` (no suffix)

### Test 2: Pre-release version from main
- **Given**: 47 commits on main since tag `v1.2.0`
- **When**: GitVersion runs on main
- **Then**: NuGetVersion is `1.3.0-alpha.47`

### Test 3: PR version format
- **Given**: PR #45 branch
- **When**: GitVersion runs on PR branch
- **Then**: NuGetVersion contains `-pr.45`

### Test 4: Major increment from commit message
- **Given**: Commit with `+semver: major`
- **When**: GitVersion calculates next version from `1.2.3`
- **Then**: Next version is `2.0.0`

### Test 5: Minor increment (default for main)
- **Given**: Normal commit on main (no semver tag)
- **When**: GitVersion calculates next version from `1.2.3`
- **Then**: Next version is `1.3.0`

### Test 6: No increment for feature branches
- **Given**: Commit on feature branch
- **When**: GitVersion calculates version
- **Then**: Version inherits from main, no increment

### Test 7: Tag prefix validation
- **Given**: Tag without `v` prefix (e.g., `1.2.3`)
- **When**: GitVersion runs
- **Then**: Tag is ignored (doesn't match tag-prefix)

---

## Integration with GitHub Actions

### Usage in Workflows

```yaml
- name: Install GitVersion
  uses: gittools/actions/gitversion/setup@v3
  with:
    versionSpec: '6.x'

- name: Calculate Version
  uses: gittools/actions/gitversion/execute@v3
  id: gitversion

- name: Display Version
  run: |
    echo "SemVer: ${{ steps.gitversion.outputs.semVer }}"
    echo "NuGetVersion: ${{ steps.gitversion.outputs.nuGetVersion }}"
    echo "FullSemVer: ${{ steps.gitversion.outputs.fullSemVer }}"
    echo "IsPreRelease: ${{ steps.gitversion.outputs.preReleaseTag }}"

- name: Build with Version
  run: dotnet pack -p:Version=${{ steps.gitversion.outputs.nuGetVersion }}
```

**Requirements**:
- FR-016: Use GitVersion for versioning
- FR-017: Auto-increment based on git history

---

## Output Variables

GitVersion provides these output variables in GitHub Actions:

| Variable | Example | Usage |
|----------|---------|-------|
| `semVer` | `1.3.0-alpha.47` | Display version |
| `nuGetVersion` | `1.3.0-alpha.47` | NuGet package version |
| `fullSemVer` | `1.3.0-alpha.47+Branch.main.Sha.abc1234` | Full version with metadata |
| `major` | `1` | Major version number |
| `minor` | `3` | Minor version number |
| `patch` | `0` | Patch version number |
| `preReleaseTag` | `alpha.47` | Pre-release suffix |
| `commitsSinceVersionSource` | `47` | Commits since last tag |
| `branchName` | `main` | Current branch name |

---

## Troubleshooting

### Problem: Version not incrementing

**Cause**: No commits since last tag
**Solution**: Make additional commits or use `+semver: minor` in commit message

---

### Problem: Wrong version on PR

**Cause**: PR branch regex not matching
**Solution**: Ensure PR branch matches `^(pull|pull-requests|pr)[/-]` pattern

---

### Problem: Tag not producing stable version

**Cause**: Tag doesn't match `v*` pattern
**Solution**: Use `v` prefix for tags (e.g., `v1.2.3`, not `1.2.3`)

---

## Notes

- GitVersion.yml must be at repository root
- Requires full git history (`fetch-depth: 0` in checkout action)
- Version calculated at build time (deterministic for same commit)
- Configuration validated on first GitVersion run
- Mode "Mainline" optimized for trunk-based development (main + short-lived branches)
