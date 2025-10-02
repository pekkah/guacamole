# Data Model: CI/CD Pipeline Entities

**Feature**: `008-ci-pipelines-we`
**Date**: 2025-10-02
**Status**: Draft

---

## Overview

This document defines the key entities in the CI/CD pipeline system. While these are not C# classes (they're GitHub Actions concepts), understanding their structure helps design the workflow files.

---

## Core Entities

### 1. Workflow Run

Represents a single execution of a GitHub Actions workflow.

**Attributes**:
- `id`: Unique identifier for the run
- `name`: Workflow name ("CI" or "Publish")
- `event`: Trigger event (push, pull_request, tag)
- `status`: Current status (queued, in_progress, completed)
- `conclusion`: Final result (success, failure, cancelled, skipped)
- `branch`: Git branch name
- `commit_sha`: Git commit SHA that triggered the run
- `triggered_by`: GitHub user or bot that triggered the run
- `started_at`: Timestamp when run started
- `completed_at`: Timestamp when run completed
- `run_number`: Sequential run number
- `run_attempt`: Retry attempt number

**Relationships**:
- Has many `Job` entities
- Belongs to one `Repository`
- Triggered by one `GitEvent`

**State Transitions**:
```
queued → in_progress → completed
                    ↓
            [conclusion: success|failure|cancelled]
```

**Validation Rules**:
- Must have at least one job
- Cannot transition from completed back to in_progress
- Conclusion only set when status is completed

---

### 2. Job

Represents a single job within a workflow run (e.g., "test-linux", "test-windows", "publish").

**Attributes**:
- `id`: Unique identifier for the job
- `name`: Job name
- `runs_on`: Runner OS (ubuntu-latest, windows-latest)
- `status`: Current status (queued, in_progress, completed)
- `conclusion`: Final result (success, failure, cancelled, skipped)
- `started_at`: Timestamp when job started
- `completed_at`: Timestamp when job completed
- `steps`: Array of step results

**Relationships**:
- Belongs to one `WorkflowRun`
- Has many `Step` entities
- Produces zero or more `Artifact` entities
- Generates one `TestResult` (for test jobs)
- May produce one or more `BuildArtifact` (for build jobs)

**Validation Rules**:
- Job fails if any step fails (unless `continue-on-error: true`)
- Steps execute sequentially in defined order
- Job cannot start until dependencies complete (if `needs:` specified)

---

### 3. Step

Represents a single step within a job (e.g., "Checkout code", "Run tests").

**Attributes**:
- `name`: Step name
- `status`: Current status (in_progress, completed)
- `conclusion`: Final result (success, failure, skipped)
- `started_at`: Timestamp when step started
- `completed_at`: Timestamp when step completed
- `output`: Console output from the step

**Relationships**:
- Belongs to one `Job`

**Common Steps**:
- Checkout: `actions/checkout@v4`
- Setup .NET: `actions/setup-dotnet@v4`
- Restore dependencies: `dotnet restore`
- Run tests: `dotnet test`
- Build packages: `dotnet pack`
- Publish packages: `dotnet nuget push`

---

### 4. Test Result

Represents the aggregated results from running all tests.

**Attributes**:
- `total_tests`: Total number of tests executed
- `passed_tests`: Number of tests that passed
- `failed_tests`: Number of tests that failed
- `skipped_tests`: Number of tests that were skipped
- `duration_ms`: Total execution time in milliseconds
- `test_framework`: Testing framework used (XUnit v3)
- `target_framework`: Target framework tested (.NET 10.0)
- `os`: Operating system where tests ran
- `failures`: Array of failure details

**Failure Detail**:
- `test_name`: Fully qualified test name
- `error_message`: Failure message
- `stack_trace`: Exception stack trace
- `file`: Source file where test is defined
- `line`: Line number in source file

**Relationships**:
- Produced by one `Job` (test job)
- Belongs to one `WorkflowRun`

**Validation Rules**:
- `total_tests = passed_tests + failed_tests + skipped_tests`
- If `failed_tests > 0`, job must fail
- Duration must be positive

**Display Requirements**:
- Must be visible in GitHub PR checks (FR-005)
- Must include pass/fail count and duration (FR-003)

---

### 5. Build Artifact

Represents a NuGet package built during the workflow.

**Attributes**:
- `package_id`: NuGet package ID (e.g., "Guacamole.Core")
- `version`: Semantic version (e.g., "1.2.3" or "1.3.0-alpha.47")
- `file_name`: Package file name (e.g., "Guacamole.Core.1.2.3.nupkg")
- `file_size_bytes`: Package file size
- `hash_sha256`: SHA-256 hash of package file
- `created_at`: Timestamp when package was built
- `metadata`: Package metadata (description, authors, tags, license)
- `is_symbols_package`: Whether this is a symbols package (.snupkg)

**Relationships**:
- Produced by one `Job` (build job)
- May be published as one `PublishedPackage`

**Metadata Structure**:
- `description`: Package description
- `authors`: Package authors
- `tags`: Package tags (comma-separated)
- `license`: License identifier (e.g., "MIT")
- `project_url`: Project URL
- `repository_url`: Repository URL

**Validation Rules**:
- Package ID must match project name
- Version must follow semantic versioning
- Metadata fields must be populated (non-empty)
- File must have `.nupkg` extension (or `.snupkg` for symbols)

---

### 6. Published Package

Represents a NuGet package published to NuGet.org.

**Attributes**:
- `package_id`: NuGet package ID
- `version`: Published version
- `is_prerelease`: Whether this is a pre-release version
- `published_at`: Timestamp when package was published
- `published_by`: GitHub user/bot that published
- `downloads`: Download count (from NuGet.org)
- `listed`: Whether package is listed on NuGet.org
- `nuget_url`: URL to package on NuGet.org

**Relationships**:
- Created from one `BuildArtifact`
- Triggered by one `WorkflowRun`

**State Transitions**:
```
not_published → publishing → published
                           ↓
                    [listed|unlisted]
```

**Validation Rules**:
- Version must be unique (no duplicates on NuGet.org) (FR-013)
- Cannot publish if tests failed (FR-019)
- Cannot publish if build failed (FR-020)
- Pre-release versions must have suffix (FR-014)

**Publishing Rules** (from spec):
- Stable packages: Published from git tags matching `v*`
- Pre-release packages: Published from main branch with `-alpha` suffix
- PR builds: Not published

---

### 7. Version Number

Represents a semantic version generated by GitVersion.

**Attributes**:
- `major`: Major version number
- `minor`: Minor version number
- `patch`: Patch version number
- `prerelease_tag`: Pre-release tag (e.g., "alpha", "pr")
- `prerelease_number`: Pre-release build number
- `build_metadata`: Build metadata (commit SHA, etc.)
- `full_semver`: Full semantic version string
- `nuget_version`: Version formatted for NuGet

**Format Examples**:
- Stable: `1.2.3` (major.minor.patch)
- Pre-release: `1.3.0-alpha.47` (major.minor.patch-tag.number)
- PR build: `1.3.0-pr.123.5` (major.minor.patch-pr.number.commit)

**Generation Rules** (GitVersion):
- Main branch: Auto-increment minor, add `-alpha.{build}`
- Tag `v1.2.3`: Use exact version `1.2.3`
- PR branch: Add `-pr.{pr-number}.{commit}`
- Increment based on commit messages (conventional commits)

**Validation Rules**:
- Must follow semantic versioning specification
- Major, minor, patch must be non-negative integers
- Pre-release tag must be alphanumeric + hyphen
- Build metadata must not affect version precedence

---

### 8. Git Event

Represents a git event that triggers a workflow.

**Attributes**:
- `event_type`: Event type (push, pull_request, create)
- `ref`: Git reference (branch name or tag name)
- `ref_type`: Reference type (branch, tag)
- `sha`: Commit SHA
- `author`: Commit author
- `message`: Commit message
- `timestamp`: Event timestamp

**Event Types**:
- `push` to main: Triggers CI + pre-release publish
- `pull_request` to main: Triggers CI only
- `create` tag `v*`: Triggers CI + stable publish

**Relationships**:
- Triggers one or more `WorkflowRun` entities

---

## Entity Relationships Diagram

```
GitEvent
  ↓ triggers
WorkflowRun
  ↓ contains
Job (matrix: linux, windows)
  ↓ contains
Step (checkout, setup, restore, test, build, publish)
  ↓ produces
TestResult (passed/failed counts, duration)
BuildArtifact (*.nupkg files)
  ↓ publishes as
PublishedPackage (on NuGet.org)
```

---

## Quality Gates

These are the critical validation points that determine workflow success/failure:

### Gate 1: Test Gate (FR-004, FR-019)
- **Condition**: All tests must pass (failed_tests = 0)
- **Action if failed**: Block PR merge, block package publishing
- **Applies to**: All workflows (CI, Publish)

### Gate 2: Build Gate (FR-020)
- **Condition**: All packages build successfully (dotnet pack exit code 0)
- **Action if failed**: Block package publishing
- **Applies to**: Publish workflow only

### Gate 3: Cross-Platform Gate (FR-021)
- **Condition**: Tests pass on both Linux AND Windows
- **Action if failed**: Block PR merge, block package publishing
- **Applies to**: All workflows (matrix strategy)

### Gate 4: Version Uniqueness Gate (FR-013)
- **Condition**: Package version doesn't exist on NuGet.org
- **Action if failed**: Skip publishing (using `--skip-duplicate` flag)
- **Applies to**: Publish workflow only

---

## Cache Entities

### NuGet Package Cache

**Attributes**:
- `cache_key`: Hash of all `*.csproj` files
- `cache_path`: `~/.nuget/packages` (Linux) or `~\.nuget\packages` (Windows)
- `cached_packages`: List of cached package names and versions
- `cache_size_mb`: Total cache size in megabytes

**Invalidation Rules**:
- Cache key changes when any `.csproj` file changes
- Fallback to prefix match if exact key not found
- Cache expires after 7 days (GitHub Actions default)

---

## Secret Entities

### NuGet API Key Secret

**Attributes**:
- `secret_name`: "NUGET_API_KEY"
- `secret_value`: NuGet.org API key (encrypted)
- `created_at`: When secret was created
- `updated_at`: When secret was last updated
- `expires_at`: API key expiration (from NuGet.org)

**Security Rules**:
- Never logged or exposed in workflow output
- Automatically masked by GitHub Actions
- Only accessible in publish workflow steps
- Stored as GitHub repository secret (encrypted at rest)

---

## Summary

This data model defines the structure of CI/CD pipeline entities and their relationships. While these entities are represented as GitHub Actions YAML configurations rather than C# classes, understanding their structure ensures the workflow files implement all requirements correctly.

**Next**: Phase 1 will continue with contract specifications in the `contracts/` directory.
