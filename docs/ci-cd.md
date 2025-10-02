# CI/CD Pipeline Documentation

## Overview

The Guacamole project uses GitHub Actions for automated testing, building, and publishing NuGet packages. The pipeline ensures code quality through cross-platform testing and automates package distribution.

## Workflows

### CI Workflow (`.github/workflows/ci.yml`)

**Triggers**:
- Push to `main` branch
- Pull requests targeting `main` branch

**Jobs**:
1. **Test** (matrix: Linux + Windows)
   - Checkout code with full git history
   - Setup .NET 10.0
   - Cache NuGet packages for performance
   - Restore dependencies
   - Build solution in Release mode
   - Run all tests with TRX logger
   - Upload test results as artifacts

2. **Build**
   - Depends on test job (waits for tests to pass)
   - Runs on Linux only
   - Install and run GitVersion for version calculation
   - Build NuGet packages with semantic versioning
   - Upload packages as artifacts

**Purpose**: Validate code changes, run tests on multiple platforms, and verify packages build successfully.

---

### Publish Workflow (`.github/workflows/publish.yml`)

**Triggers**:
- Tags matching `v*` (e.g., `v1.2.3`) → Stable releases
- Push to `main` branch → Pre-release packages

**Jobs**:
1. **Test** (identical to CI workflow)
   - Run full test suite on Linux + Windows
   - Upload test results

2. **Publish**
   - Depends on test job (publishing blocked if tests fail)
   - Calculate version with GitVersion
   - Build NuGet packages
   - Publish packages to NuGet.org
   - Create GitHub Release (tags only)

**Purpose**: Publish validated packages to NuGet.org after all tests pass.

---

## Versioning Strategy

Guacamole uses **GitVersion** with Mainline mode for semantic versioning.

### Version Formats

| Trigger | Version Example | Type | Published to NuGet |
|---------|----------------|------|-------------------|
| Tag `v1.2.3` | `1.2.3` | Stable | Yes (stable) |
| Push to main | `1.3.0-alpha.47` | Pre-release | Yes (pre-release) |
| Pull request #45 | `1.3.0-pr.45.5` | Development | No |
| Feature branch | `1.3.0-alpha.feature-name.3` | Development | No |

### Version Increment

Versions auto-increment based on:
- **Main branch**: Minor version (`1.2.0` → `1.3.0`)
- **Commit messages**: Use `+semver:` tags
  - `+semver: major` → Increment major version (breaking changes)
  - `+semver: minor` → Increment minor version (new features)
  - `+semver: patch` → Increment patch version (bug fixes)
  - `+semver: none` → No version increment

**Example commit**:
```bash
git commit -m "feat: add caching support +semver: minor"
```

---

## Creating a Release

### Stable Release (Production)

1. Ensure `main` branch is stable and all tests pass
2. Create and push an annotated tag:
   ```bash
   git checkout main
   git pull origin main
   git tag -a v1.2.3 -m "Release 1.2.3"
   git push origin v1.2.3
   ```

3. Publish workflow automatically:
   - Runs tests on Linux + Windows
   - Publishes packages to NuGet.org (version `1.2.3`)
   - Creates GitHub Release

### Pre-Release (Alpha)

Pre-release packages are published automatically when code is pushed to `main`:

```bash
git checkout main
git pull origin main
# Make changes
git commit -am "feat: new feature"
git push origin main
```

Publish workflow automatically publishes packages with `-alpha` suffix (e.g., `1.3.0-alpha.47`).

### Testing Pre-Release Packages

Install pre-release packages with `--prerelease` flag:

```bash
dotnet add package Guacamole.Core --version 1.3.0-alpha.47 --prerelease
```

---

## NuGet API Key Management

### Initial Setup

1. Generate API key on NuGet.org:
   - Go to https://www.nuget.org → Account Settings → API Keys
   - Click "Create"
   - Name: `GitHub Actions - Guacamole`
   - Scopes: `Push packages`, `Unlist packages`
   - Expiration: 365 days (set calendar reminder)
   - Copy API key (shown only once)

2. Add secret to GitHub repository:
   - Go to repository → Settings → Secrets and variables → Actions
   - Click "New repository secret"
   - Name: `NUGET_API_KEY`
   - Value: [paste API key]
   - Click "Add secret"

### Rotating API Key

API keys should be rotated annually:

1. Generate new API key on NuGet.org
2. Update `NUGET_API_KEY` secret in GitHub
3. Delete old API key on NuGet.org
4. Verify publish workflow still works

---

## Troubleshooting

### Workflow doesn't trigger

**Symptoms**: No workflow run appears after push/PR

**Checks**:
- Verify workflow files exist in `.github/workflows/`
- Check YAML syntax (use GitHub's workflow editor)
- Ensure trigger conditions match (push to main, PR to main, tag `v*`)
- Check repository Actions settings (Actions may be disabled)

---

### Tests fail in CI but pass locally

**Symptoms**: Tests pass locally but fail in GitHub Actions

**Common Causes**:
- **Platform differences**: Code behaves differently on Linux vs Windows
- **File paths**: Use `Path.Combine()` instead of hardcoded separators
- **Case sensitivity**: Linux filesystems are case-sensitive
- **Environment variables**: CI environment may have different defaults
- **Time zones**: Use UTC for time-based tests

**Solution**: Run tests on both platforms locally or use matrix strategy to catch issues early.

---

### Package publish fails

**Symptoms**: Publish job fails with authentication or upload errors

**Checks**:
1. **API key valid**: Check expiration on NuGet.org
2. **API key permissions**: Ensure "Push packages" scope enabled
3. **Secret configured**: Verify `NUGET_API_KEY` exists in GitHub secrets
4. **Version unique**: Check if version already exists on NuGet.org
5. **Tests passed**: Publish job won't run if tests fail

**Common Errors**:
- `401 Unauthorized`: API key invalid or expired
- `409 Conflict`: Package version already exists (use `--skip-duplicate`)
- `403 Forbidden`: API key lacks required permissions

---

### GitVersion shows wrong version

**Symptoms**: Version calculation doesn't match expectations

**Checks**:
- Verify `fetch-depth: 0` in checkout step (full git history required)
- Check `GitVersion.yml` syntax
- Ensure tags follow `v*` pattern (not `1.2.3`)
- Review branch name matches configuration regex

**Debug**:
```bash
# Run GitVersion locally (requires GitVersion tool)
dotnet tool install --global GitVersion.Tool
gitversion /showvariable SemVer
gitversion /showvariable NuGetVersion
```

---

### CI runs slow

**Symptoms**: Workflows take longer than expected

**Optimization Tips**:
1. **Cache NuGet packages**: Already configured (saves ~30 seconds)
2. **Reduce matrix**: If only Windows needed, remove Linux from matrix
3. **Skip duplicate builds**: Use `concurrency` to cancel old runs
4. **Optimize tests**: Split slow tests into separate jobs

**Current Performance**:
- CI workflow: ~5-8 minutes
- Publish workflow: ~8-12 minutes

---

## Rollback / Unlisting Packages

If a defective package is published, you can unlist it:

```bash
dotnet nuget delete Guacamole.Core 1.2.3 \
  --api-key $NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json \
  --non-interactive
```

**Important**:
- Unlisting makes the package undiscoverable (not deleted)
- Existing projects using the package are not affected
- Publish a fixed version and communicate upgrade path

---

## Security Best Practices

### Secrets Management
- ✅ NuGet API key stored as encrypted GitHub secret
- ✅ Secrets automatically masked in logs
- ✅ Publish workflow has minimal write permissions
- ✅ CI workflow has read-only permissions

### Dependency Security
- Review Dependabot alerts regularly
- Keep GitHub Actions versions up to date
- Pin action versions (e.g., `@v4` not `@main`)

---

## Monitoring

### Build Status
- Check GitHub Actions tab for workflow runs
- Review PR checks before merging
- Monitor NuGet.org for package downloads

### Maintenance Tasks

**Monthly**:
- Review workflow run times (optimize if degrading)
- Check NuGet API key expiration
- Review NuGet.org package statistics

**Per Release**:
- Create git tag for stable release
- Verify package published successfully
- Verify GitHub release created
- Test package installation in sample project

---

## Links

- [CI Workflow](.github/workflows/ci.yml)
- [Publish Workflow](.github/workflows/publish.yml)
- [GitVersion Configuration](../GitVersion.yml)
- [Validation Guide](../specs/008-ci-pipelines-we/quickstart.md)
- [NuGet.org Package](https://www.nuget.org/packages/Guacamole.Core/)

---

## Questions?

For CI/CD issues:
1. Check this documentation
2. Review workflow logs in GitHub Actions
3. Consult [quickstart.md](../specs/008-ci-pipelines-we/quickstart.md) for detailed validation steps
4. Check [contracts](../specs/008-ci-pipelines-we/contracts/) for workflow specifications
