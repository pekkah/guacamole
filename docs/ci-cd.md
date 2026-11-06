# CI/CD Pipeline Documentation

## Overview

Guacamole uses GitHub Actions to build, test and pack on every change, and to publish
NuGet packages **only** when a version tag is pushed (or the Publish workflow is run by hand).

| Event | Workflow | Test matrix | Pack (artifact) | nuget.org | GitHub release |
|-------|----------|-------------|-----------------|-----------|----------------|
| Pull request to `main` | CI | Linux + Windows | yes | no | no |
| Push to `main` | CI | Linux + Windows | yes | **no** | no |
| Push tag `v1.2.3` / `v1.2.3-beta.1` | Publish (reuses CI) | Linux + Windows | yes | yes | yes (prerelease if the version has a label) |
| Manual run on a `v*` tag | Publish (reuses CI) | Linux + Windows | yes | yes | yes |
| Manual run on `main` | Publish (reuses CI) | Linux + Windows | yes | yes (GitVersion prerelease, e.g. `0.2.0-alpha.5`) | no |
| Manual run on any other branch | Publish | - | - | refused | - |

The test matrix runs exactly once per event: tag pushes do not trigger CI on their own, and
Publish calls `ci.yml` as a reusable workflow instead of duplicating its jobs.

## Workflows

### CI (`.github/workflows/ci.yml`)

**Triggers**: push to `main`, pull requests targeting `main`, and `workflow_call` (used by Publish).

**Jobs**:

1. **test** (matrix: `ubuntu-latest`, `windows-latest`)
   - Checkout with full history, setup .NET `11.0.x` (preview quality), NuGet cache
   - `dotnet build guacamole.sln -c Release` - this includes `src/`, `tests/`, `benchmarks/`
     and `examples/`, so public API changes that break the examples fail CI
   - `dotnet test --solution guacamole.sln` (Microsoft.Testing.Platform runner, see `global.json`);
     non-test executables (examples, benchmarks) are ignored by the runner
   - Upload TRX test results
2. **pack** (Linux, needs `test`)
   - Calculate the package version (see [Versioning](#versioning-strategy))
   - `dotnet pack guacamole.sln -c Release -p:Version=<version>`; examples, benchmarks and
     tests have `IsPackable=false`, so only `Guacamole` and `Guacamole.SourceGenerators` are packed
   - Upload `.nupkg` + `.snupkg` as the `nuget-packages` artifact
   - Exposes `version` and `prerelease` as `workflow_call` outputs

CI has `contents: read` permission only and never publishes.

### Publish (`.github/workflows/publish.yml`)

**Triggers**: push of tags matching `v*`, and `workflow_dispatch`.

**Jobs**:

1. **guard** - fails unless the ref is `main` or a `v*` tag
2. **build** - calls `ci.yml` (full test matrix + pack)
3. **publish** (needs `build`, `contents: write`, `id-token: write`)
   - Download the `nuget-packages` artifact
   - `NuGet/login@v1` exchanges the job's GitHub OIDC token for a short-lived nuget.org API
     key ([trusted publishing](#nuget-trusted-publishing))
   - `dotnet nuget push artifacts/*.nupkg --skip-duplicate` to nuget.org with that key
     (`.snupkg` symbol packages are pushed alongside automatically)
   - For tag refs: `gh release create <tag> --generate-notes --verify-tag` with the `.nupkg`
     and `.snupkg` files attached, and `--prerelease` when the version contains a `-label`.
     If the release already exists (re-run), the assets are re-uploaded with `--clobber`.

Runs are serialized per ref (`concurrency: publish-<ref>`, not cancelled).

## Versioning Strategy

Versions are SemVer 2.0 **without build metadata**.

- **Tag builds** (`refs/tags/v*`): the version is the tag name without the leading `v`, and
  must match `MAJOR.MINOR.PATCH[-prerelease]`. `v1.2.3+meta` or `vfoo` fail the build.
- **Everything else**: GitVersion's `SemVer` variable (GitVersion 6.x, pinned in
  `.config/dotnet-tools.json`, configured by `GitVersion.yml`).
- **Local builds**: `Directory.Build.props` sets `0.1.0-dev`; CI overrides it with `-p:Version`.

Why not GitVersion's `FullSemVer`? On non-`main` branches it carries `+N` build metadata
(observed locally on a topic branch: `SemVer 0.1.0-<branch>.1`, `FullSemVer 0.1.0-<branch>.1+42`). NuGet ignores
build metadata when comparing versions, so two different builds would collide and
`--skip-duplicate` would silently skip the newer one. GitVersion 6 no longer outputs
`NuGetVersion`/`NuGetVersionV2`, so `SemVer` is the right variable.

Why use the tag name instead of GitVersion on tags? With `mode: ContinuousDelivery` and
`label: alpha` on `main`, GitVersion ignores prerelease tags with a different label: a commit
tagged `v0.2.0-beta.1` is reported as `0.2.0-alpha.N`. Stable tags (`v0.2.0` -> `0.2.0`) are
reported correctly, but taking the tag verbatim is simpler and always matches the release.

### Example versions

| Ref | Version |
|-----|---------|
| Tag `v1.2.3` | `1.2.3` |
| Tag `v1.3.0-beta.1` | `1.3.0-beta.1` |
| `main`, 2 commits after `v1.2.3` | `1.3.0-alpha.2` |
| `main`, no tags yet | `0.1.0-alpha.<commits>` |
| Pull request | `<next>-pr<...>` prerelease (artifact only, never published) |

### Version increment (GitVersion)

- `main` increments the **minor** version after the last tag
- Commit messages can override: `+semver: major|minor|patch|none`

```bash
git commit -m "feat: add caching support +semver: minor"
```

## Creating a Release

### Stable or prerelease release

```bash
git checkout main
git pull origin main
git tag -a v1.2.3 -m "Release 1.2.3"        # prerelease: v1.3.0-beta.1
git push origin v1.2.3
```

The Publish workflow then runs the test matrix, packs `1.2.3`, pushes it to nuget.org and
creates the GitHub release `v1.2.3` with the packages attached. Tags with a `-label` produce a
prerelease on both nuget.org and GitHub.

### Ad-hoc prerelease from main

Actions → **Publish** → *Run workflow* → branch `main`. This publishes the current GitVersion
prerelease (e.g. `0.2.0-alpha.5`) to nuget.org without creating a GitHub release. Running it
again without new commits produces the same version and is skipped by `--skip-duplicate`.

### Re-running a release

Re-running a failed Publish run for a tag is safe: already-pushed packages are skipped and
existing release assets are replaced.

## NuGet Trusted Publishing

Publishing uses [nuget.org trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
the publish job requests a GitHub OIDC token (`id-token: write`) and `NuGet/login@v1`
exchanges it for an API key that is valid for one hour. No long-lived API key is stored in
the repository.

One-time setup:

1. On nuget.org, signed in as the package owner: your profile → **Trusted Publishing** →
   **Add policy** → GitHub Actions, with
   - Repository owner: `pekkah`
   - Repository: `guacamole`
   - Workflow file: `publish.yml`
   - Environment: leave empty (the workflow does not use a GitHub environment)
2. The workflow logs in as nuget.org user `pekkah` (the `user:` input of the
   `NuGet/login` step in `publish.yml`); change it there if the owning account changes.
3. Delete the old `NUGET_API_KEY` repository secret and revoke the key on nuget.org; it is no
   longer used.

A new policy may start as temporarily active until the first successful publish binds it to
the repository; see the nuget.org documentation.

The GitHub release uses the built-in `GITHUB_TOKEN` (job permission `contents: write`); no
extra secret is required.

## Troubleshooting

### Publish fails at "Calculate version"
The tag is not `v<MAJOR>.<MINOR>.<PATCH>[-label]`. Delete it and push a valid one.

### Publish run was refused by "Check ref"
Manual runs are only allowed on `main` or a `v*` tag.

### Package publish fails
- `NuGet login` fails: no matching trusted publishing policy (check owner, repository and
  workflow file name `publish.yml`), the policy owner doesn't own the package, or the
  `user:` in `publish.yml` isn't the policy owner's nuget.org username
- `403 Forbidden` on push: the policy's owner account doesn't own (or can't create) the
  package ID
- A version already on nuget.org is skipped (`--skip-duplicate`), not overwritten. Bump the tag.

### GitVersion shows an unexpected version
- Checkout must use `fetch-depth: 0` and `fetch-tags: true`
- Tags must be prefixed with `v`
- Run it locally:

```bash
dotnet tool restore
dotnet gitversion                          # full JSON
dotnet gitversion /showvariable SemVer
```

### Tests fail in CI but pass locally
Typical causes are path separators, case-sensitive file systems on Linux, environment
defaults and time zones. The Linux + Windows matrix exists to catch these.

## Rollback / Unlisting Packages

Use a personal API key created for this one-off (trusted publishing keys only exist inside
the workflow), or unlist from the package page on nuget.org.

```bash
dotnet nuget delete Guacamole 1.2.3 \
  --api-key $NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json \
  --non-interactive
```

On nuget.org this unlists the version (it is not deleted). Publish a fixed version and
mark the GitHub release accordingly.

## Security

- No long-lived NuGet credentials are stored: the `publish` job (only for `v*` tags or manual
  runs on `main`) gets a one-hour key via OIDC trusted publishing; only that job has
  `id-token: write`
- CI runs with `contents: read`; only the publish job gets `contents: write`
- Action versions are pinned to major versions (`actions/checkout@v7`, `setup-dotnet@v6`,
  `cache@v6`, `upload-artifact@v7`, `download-artifact@v8`, `NuGet/login@v1`)

## Links

- [CI workflow](../.github/workflows/ci.yml)
- [Publish workflow](../.github/workflows/publish.yml)
- [GitVersion configuration](../GitVersion.yml)
- [Guacamole on nuget.org](https://www.nuget.org/packages/Guacamole/)
