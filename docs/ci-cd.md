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
| PR / push to `main` touching `.github/workflows/**` | Lint workflows | - | - | - | - |

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
   - `dotnet format guacamole.sln --verify-no-changes` on both OSes, with `Configuration=Release`
     set in the step's environment (see [Format check](#format-check) and [Line endings](#line-endings))
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

### Lint workflows (`.github/workflows/lint-workflows.yml`)

**Triggers**: pull requests targeting `main` and pushes to `main` that change anything under
`.github/workflows/**`.

**Job**: **actionlint** runs [actionlint](https://github.com/rhysd/actionlint) on all
workflow files using the `rhysd/actionlint` Docker image pinned to an exact version
(`1.7.12`). The image bundles shellcheck, so `run:` scripts are checked too. This catches
mistakes a YAML syntax check misses: unknown contexts/expressions, wrong action inputs,
invalid `needs:` or `permissions:` entries, and shell script bugs. The job has
`contents: read` only.

Run it locally before pushing workflow changes (install shellcheck to get the script checks):

```bash
# release binary: https://github.com/rhysd/actionlint/releases (verify against *_checksums.txt)
actionlint                       # lints .github/workflows/*.yml from the repo root
# or with Docker
docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:1.7.12 -color
```

## Line endings

All text files use LF, in the repository and in every working tree, on every platform:
`.gitattributes` has `* text=auto eol=lf` and `.editorconfig` has `end_of_line = lf`. That is
what lets the format check run on the Windows runner too (with plain `text=auto`, Windows
checkouts get CRLF and every file fails the check). The only exceptions are `*.cmd`/`*.bat`
(CRLF, none exist today) and binary files. Verify snapshots (`*.verified.*`) are LF/UTF-8 as
well.

If you cloned before this was set up, refresh your working tree once (commit or stash first):

```bash
git rm --cached -r -q . && git reset --hard
```

## Format check

`dotnet format` does not compile the solution. It loads it into an MSBuildWorkspace and takes
analyzers, including the `[ComputingContext]` source generator, from the paths the project
evaluation points at. The test, benchmark and example projects reference the generator as a
project (`ProjectReference` with `OutputItemType="Analyzer"` and
`ReferenceOutputAssembly="false"`), so the analyzer path is
`src/Guacamole.SourceGenerators/bin/<Configuration>/netstandard2.0/Guacamole.SourceGenerators.dll`,
and that DLL must already have been built in the configuration format evaluates.

With no `Configuration` set, format evaluates **Debug**, while CI builds Release. The Debug
generator DLL then does not exist, format loads the projects without it (and without a
warning), and every generated member is missing from its semantic model. Format reports
compiler errors that have a code fix, so a line such as
`var batch = context.ItemsSource.BeginBatch();` fails the check with
`CS1503: cannot convert from 'int' to 'T'` on the next `batch.Push(...)`, although the real
build compiles it (#72). Typing the variable explicitly (`IBatchOperation<int> batch = ...`)
only hid the problem.

`dotnet format` has no option for MSBuild properties, but MSBuild reads environment variables
as properties, so CI sets `Configuration: Release` in the format step's `env:` and format uses
the Release generator built by the step before it. With that, `var` works in tests again; the
explicit declarations that were added as a workaround can stay.

Run the same sequence as CI locally. Start from a clean tree, so stale Debug outputs in `obj/`
and `bin/` cannot hide the problem (`git clean -xfd` deletes all untracked files):

```bash
git clean -xfd && dotnet restore guacamole.sln
dotnet build guacamole.sln --no-restore -c Release
Configuration=Release dotnet format guacamole.sln --verify-no-changes --no-restore
# PowerShell: $env:Configuration = 'Release'; dotnet format guacamole.sln --verify-no-changes --no-restore
```

After a plain (Debug) `dotnet build guacamole.sln`, run `dotnet format guacamole.sln --verify-no-changes`
without the variable instead: format must evaluate the configuration you built.

## Public API tracking

`src/Guacamole` uses [Microsoft.CodeAnalysis.PublicApiAnalyzers](https://www.nuget.org/packages/Microsoft.CodeAnalysis.PublicApiAnalyzers).
Every public type and member is listed in `src/Guacamole/PublicAPI.Shipped.txt` (released API)
or `src/Guacamole/PublicAPI.Unshipped.txt` (changes since the last release). Adding public API
without listing it (RS0016) or removing listed API (RS0017) fails the build, so API changes show
up in review as diffs of these files.

- After changing the public API, add the entries with the IDE code fix, or with
  `dotnet format analyzers src/Guacamole/Guacamole.csproj --diagnostics RS0016 --severity info`.
  For removed API, delete its line from `PublicAPI.Unshipped.txt`, or mark it `*REMOVED*` in front
  of the line copied from `PublicAPI.Shipped.txt`.
- When releasing a version, move the `PublicAPI.Unshipped.txt` entries to `PublicAPI.Shipped.txt`
  (keep `#nullable enable` as the first line of both files); see the
  [release checklist](#release-checklist). Until the first release, `PublicAPI.Shipped.txt` is empty.

RS0026 (several overloads with optional parameters) is disabled: the operators deliberately give
every overload a trailing optional `ProviderMetadata`. The source generator project is an
analyzer with no public API for consumers, so it is not tracked.

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

### Release checklist

Before tagging (in the commit that will be tagged):

1. `CHANGELOG.md`: rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`, add a new empty
   `## [Unreleased]` above it, and update the link references at the bottom:
   `[Unreleased]: https://github.com/pekkah/guacamole/compare/vX.Y.Z...HEAD` and
   `[X.Y.Z]: https://github.com/pekkah/guacamole/releases/tag/vX.Y.Z` (for the first release the
   `[Unreleased]` link points at `commits/main`, since there is no earlier tag to compare with).
2. Check the package contents locally (`dotnet pack guacamole.sln -c Release`, then list the
   `.nupkg` files in `artifacts/`): `Guacamole` has `lib/net11.0/Guacamole.dll` and `Guacamole.xml`
   plus a `.snupkg` with the PDB; `Guacamole.SourceGenerators` has
   `analyzers/dotnet/cs/Guacamole.SourceGenerators.dll` (PDB embedded, no `lib/` folder, marked as
   a development dependency); both contain `README.md`.

After the tag is published, in a follow-up commit on `main`:

1. **Promote the public API.** Move every entry of `src/Guacamole/PublicAPI.Unshipped.txt` to
   `src/Guacamole/PublicAPI.Shipped.txt`, leaving only `#nullable enable` in the unshipped file
   (keep it as the first line of both). For a `*REMOVED*` entry, delete the corresponding line from
   the shipped file instead of copying it. From then on, RS0016/RS0017 report changes against the
   released API (see [Public API tracking](#public-api-tracking)).
2. **Promote the analyzer release.** Move the rules of
   `src/Guacamole.SourceGenerators/AnalyzerReleases.Unshipped.md` to `AnalyzerReleases.Shipped.md`
   under a `## Release X.Y.Z` heading (keep the comment header of both files); keep
   `DiagnosticDescriptors.cs` in sync with them.
3. **Set the package validation baseline** after the **first stable** tag: in
   `Directory.Build.props`, uncomment `<PackageValidationBaselineVersion>` and set it to that
   version (for example `0.1.0`). `dotnet pack` then downloads that version of each package from
   nuget.org and fails on breaking changes against it (`EnablePackageValidation` is already on).
   Raise the baseline when a later release intentionally breaks compatibility (a new major
   version, or a new minor version while the major version is 0). Previews (`-preview.N`) are
   never used as a baseline.

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
2. In GitHub: **Settings → Secrets and variables → Actions → New repository secret**
   `NUGET_USER` = the nuget.org username that owns the policy. The `NuGet/login` step reads it.
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
  `NUGET_USER` secret is missing or isn't the policy owner's nuget.org username
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
- CI and the workflow lint run with `contents: read`; only the publish job gets `contents: write`
- Action versions are pinned to major versions (`actions/checkout@v7`, `setup-dotnet@v6`,
  `cache@v6`, `upload-artifact@v7`, `download-artifact@v8`, `NuGet/login@v1`); the
  actionlint Docker image is pinned to an exact version

## Links

- [CI workflow](../.github/workflows/ci.yml)
- [Publish workflow](../.github/workflows/publish.yml)
- [Lint workflows](../.github/workflows/lint-workflows.yml)
- [GitVersion configuration](../GitVersion.yml)
- [Guacamole on nuget.org](https://www.nuget.org/packages/Guacamole/)
