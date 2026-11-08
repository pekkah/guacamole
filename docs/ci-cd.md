# CI/CD Pipeline Documentation

## Overview

Guacamole uses GitHub Actions to build, test and pack on every change. Every push to `main`
publishes a **preview** package to nuget.org once the tests pass, and a `vX.Y.Z` tag publishes
the **stable** version `X.Y.Z` and creates a GitHub release.

| Event | Workflow | Test matrix | Pack (artifact) | nuget.org | GitHub release |
|-------|----------|-------------|-----------------|-----------|----------------|
| Pull request to `main` | CI | Linux + Windows | yes (`X.Y.Z-pr.N`) | **no** | no |
| Push to `main` | Publish (reuses CI) | Linux + Windows | yes | yes, preview `X.Y.Z-preview.N` | no |
| Push tag `vX.Y.Z` | Publish (reuses CI) | Linux + Windows | yes | yes, stable `X.Y.Z` | yes |
| Push tag with a prerelease suffix (`v1.2.3-beta.1`) | Publish | - | - | refused (`::error::`) | - |
| Manual run on `main` or a `vX.Y.Z` tag | Publish (reuses CI) | Linux + Windows | yes | same as the push (re-run; duplicates skipped) | tags only |
| Manual run on any other ref | Publish | - | - | refused | - |
| PR / push to `main` touching `.github/workflows/**` | Lint workflows | - | - | - | - |

The test matrix runs exactly once per event: `ci.yml` has no `push` trigger, and Publish
calls it as a reusable workflow (`workflow_call`) instead of duplicating its jobs, then
publishes only if it passed. See [Why Publish reuses CI](#why-publish-reuses-ci).

## Workflows

### CI (`.github/workflows/ci.yml`)

**Triggers**: pull requests targeting `main`, and `workflow_call` (used by Publish for pushes
to `main` and `v*` tags). There is no `push` trigger, so pushes are not built twice.

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

**Triggers**: push to `main`, push of tags matching `v*`, and `workflow_dispatch` (to re-run
the publish for `main` or a tag, e.g. after fixing the nuget.org policy).

**Jobs**:

1. **guard** - fails unless the ref is `main` or a stable `vMAJOR.MINOR.PATCH` tag. A tag with a
   prerelease suffix (`v1.2.3-beta.1`) fails here with an `::error::`, before the test matrix,
   and nothing is published
2. **build** - calls `ci.yml` (full test matrix + pack); `ci.yml` checks the tag again when it
   computes the version
3. **publish** (needs `build`, `contents: write`, `id-token: write`)
   - Runs for tags, and for `main` builds whose version is a prerelease. If `main`'s HEAD already
     carries a release tag (commit and tag pushed together), GitVersion reports the stable
     version and the job is skipped; the tag's own run publishes it and creates the release
   - Download the `nuget-packages` artifact
   - `NuGet/login@v1` exchanges the job's GitHub OIDC token for a short-lived nuget.org API
     key ([trusted publishing](#nuget-trusted-publishing))
   - [Version guard](#version-guard): fails unless each package's version is higher than every
     version of that package already on nuget.org (a re-run of an existing version passes)
   - `dotnet nuget push artifacts/*.nupkg --skip-duplicate` to nuget.org with that key
     (`.snupkg` symbol packages next to the `.nupkg` files are pushed alongside automatically)
   - For tag refs only: `gh release create <tag> --generate-notes --verify-tag` with the `.nupkg`
     and `.snupkg` files attached. If the release already exists (re-run), the assets are
     re-uploaded with `--clobber`. Preview builds from `main` get no GitHub release; they are
     only on nuget.org

**Concurrency**: `publish-<ref>` with `cancel-in-progress: false`, so publishes of the same ref
run one at a time and a running publish is never cancelled. GitHub keeps at most one *pending*
run per group: after several quick pushes to `main`, an older pending run can be replaced by the
newest one. That only skips an intermediate preview; versions stay in order. Each tag is its own
ref, so tags never wait for `main` or for each other.

### Why Publish reuses CI

Alternatives that were considered:

- **`ci.yml` keeps `push: main` and Publish also runs on `push: main`**: simple, but every push
  to `main` runs the Linux + Windows matrix twice.
- **Publish on `workflow_run` of CI**: CI runs once, but the publish run has to download
  artifacts from another run (run id, `actions: read`), always runs in the context of the default
  branch (so the tag/ref has to be recovered from the triggering run's payload), and tags would
  need a CI push trigger again. More moving parts for the same result.
- **Chosen: Publish triggers on `push: main` + `v*` tags and calls `ci.yml`; `ci.yml` has only
  `pull_request` and `workflow_call`**. CI runs once per push, the packages come from the same
  run (`needs: build`), and nothing is published unless every test job passed. Pushes to `main`
  show up as Publish runs (`Publish / Build / Test on ...`) instead of CI runs; pull request
  check names are unchanged.

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

| Ref | Version | Published |
|-----|---------|-----------|
| Tag `v1.2.3` | `1.2.3` (stable) | nuget.org + GitHub release |
| Tag `v1.3.0-beta.1` (any prerelease suffix) | - | **refused**, the build fails |
| `main`, before the first stable tag `v0.2.0` | `0.2.0-preview.<commits>` (`next-version`) | nuget.org (preview) |
| `main`, HEAD is the tagged `v1.2.3` commit | `1.2.3` | by the tag's run only |
| `main`, 1 commit after `v1.2.3` | `1.3.0-preview.1` | nuget.org (preview) |
| `main`, 5 commits after `v1.2.3` | `1.3.0-preview.5` | nuget.org (preview) |
| Pull request | `<next>-pr.<n>` | no (CI artifact only) |
| Other branch, e.g. `feature/foo` | `<next>-foo.<n>` | no (never built by Publish) |
| Local build | `0.2.0-dev` (`Directory.Build.props`) | no |

- **Tag builds** (`refs/tags/v*`): the version is the tag name without the leading `v`, and
  must be a stable `MAJOR.MINOR.PATCH`. `v1.2.3-beta.1`, `v1.2.3+meta`, `v1.2` or `vfoo` fail
  with an `::error::` (in Publish's guard job and again in `ci.yml`).
- **Everything else**: GitVersion's `SemVer` variable (GitVersion 6.8.2, pinned in
  `.config/dotnet-tools.json`, configured by `GitVersion.yml`).
- **Local builds**: `Directory.Build.props` sets `0.2.0-dev`; CI overrides it with `-p:Version`.

### Version increment rule (GitVersion)

`GitVersion.yml` uses `mode: ContinuousDelivery` with `label: preview` and `increment: Minor` on
`main`:

- After a stable tag `vX.Y.Z`, `main` builds are `X.(Y+1).0-preview.N`, where `N` is the number
  of commits since the tag. `N` only grows, so every push to `main` gets a new, higher version.
- Release the next version by tagging the `main` commit you want: `vX.(Y+1).0` (or any higher
  stable version, e.g. `v(X+1).0.0`; GitVersion continues from the latest tag).
- Commit messages can override the increment: `+semver: major|minor|patch|none`. For example
  `+semver: major` after `v1.2.3` makes the previews `2.0.0-preview.N`.

```bash
git commit -m "feat!: new API +semver: major"
```

**Starting at 0.2.0.** nuget.org already has `0.1.0-alpha.23` ... `0.1.0-alpha.41` (kept, not
unlisted), so the first stable release is `v0.2.0`. `GitVersion.yml` sets `next-version: 0.2.0`,
a floor that applies while no tag at or above it exists: before `v0.2.0`, `main` builds are
`0.2.0-preview.N`, which sort above every `0.1.0-alpha.*` package. Once `v0.2.0` is tagged the
tag is the version source and `main` continues with `0.3.0-preview.N`; `next-version` does not
pin it back, so it can stay in the file (raising it later is only needed to skip versions).
GitVersion parses `next-version` with the `tag-prefix` pattern, so the prefix is `'v?'` (with a
mandatory `v` it fails with *Failed to parse 0.2.0*). Release tags are still always `vX.Y.Z`:
Publish only runs for `v*` tags and rejects anything that is not `vMAJOR.MINOR.PATCH`.

This was verified with GitVersion 6.8.2 in a scratch clone: `main` with no tags gives
`0.2.0-preview.<commits>`; `feature/bar` gives `0.2.0-bar.1` and a PR merge ref
`0.2.0-pr.<n>`; tagging `main` `v0.2.0` gives `0.2.0` (also for a detached checkout with
`GITHUB_REF=refs/tags/v0.2.0`, as on Actions); the next commits give `0.3.0-preview.1`,
`0.3.0-preview.2`; `feature/foo` then gives `0.3.0-foo.1` and a PR merge ref `0.3.0-pr.<n>`.
Without `next-version` (verified earlier with 6.4.0 and 6.8.2, identical results) the same
history gives `0.1.0-preview.N`, then `0.1.0`, then `0.2.0-preview.1`, and a `+semver: major`
commit gives `1.0.0-preview.N`.

Why not GitVersion's `FullSemVer`? On non-`main` branches it carries `+N` build metadata
(e.g. `SemVer 0.3.0-foo.1`, `FullSemVer 0.3.0-foo.1+3`). NuGet ignores build metadata when
comparing versions, so two different builds would collide and `--skip-duplicate` would silently
skip the newer one. GitVersion 6 no longer outputs `NuGetVersion`/`NuGetVersionV2`, so `SemVer`
is the right variable.

Why use the tag name instead of GitVersion on tags? It always matches the release exactly.
GitVersion agrees for stable tags (`v0.2.0` -> `0.2.0`), but ignores prerelease tags whose label
differs from the branch label, which is one more reason prerelease tags are not allowed:
prereleases come from `main`.

### Version guard

Before pushing, the publish job checks out `.github/scripts/check_nuget_version.py` (sparse
checkout) and runs it with the version and the package IDs taken from the `.nupkg` file names.
For each package it reads `https://api.nuget.org/v3-flatcontainer/<id lowercase>/index.json`,
which lists every version ever pushed, listed or unlisted (404: no versions yet, allowed), and
fails with an `::error::` unless the new version is **strictly higher** than the highest
existing one under NuGet's SemVer 2.0 ordering: numeric parts and numeric prerelease
identifiers compare numerically, other identifiers as strings ignoring case, numeric identifiers
sort below alphanumeric ones, a release is higher than its prereleases, fewer identifiers lose
when all preceding ones are equal, and build metadata is ignored. So `0.2.0-preview.1` passes
against the published `0.1.0-alpha.41`, while a stale `0.1.0-alpha.24` or `0.0.9` is refused.

The one exception is a version that already exists for that package: it passes, because that
is a re-run of a publish (for example a re-run of the tag after a failed release step), and
`--skip-duplicate` then skips the package without changing nuget.org. A partial earlier
publish (one package pushed, the other not) is completed the same way.

The script runs a self-test of the ordering first (`--self-test`, e.g.
`0.2.0-preview.1 > 0.1.0-alpha.41`, `0.2.0 > 0.2.0-preview.9`, `0.3.0-preview.1 > 0.2.0`,
`1.0.0-alpha.10 > 1.0.0-alpha.9`). It uses only the Python standard library; its ordering was
cross-checked against NuGet.Versioning 6.14.0 on 398 random versions (identical order). Run it
locally with

```bash
python3 .github/scripts/check_nuget_version.py --self-test
python3 .github/scripts/check_nuget_version.py 0.2.0-preview.1 Guacamole Guacamole.SourceGenerators
```

## Creating a Release

### Preview packages

Nothing to do: every push to `main` (including merged pull requests) publishes
`X.Y.Z-preview.N` to nuget.org after the tests pass. Install them with

```bash
dotnet add package Guacamole --prerelease
dotnet add package Guacamole.SourceGenerators --prerelease
```

### Stable release

```bash
git checkout main
git pull origin main
git tag -a v1.2.3 -m "Release 1.2.3"
git push origin v1.2.3
```

The Publish workflow then runs the test matrix, packs `1.2.3`, pushes it to nuget.org and
creates the GitHub release `v1.2.3` with the packages attached. The next push to `main`
publishes `1.3.0-preview.1`. Tags must be stable (`vMAJOR.MINOR.PATCH`); a prerelease tag is
refused.

### Release checklist

Previews need no checklist. For a stable release, before tagging (in the commit that will be
tagged; the push of that commit to `main` publishes one more `X.Y.Z-preview.N`, which is expected):

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
3. Wait for that commit's Publish run on `main` to pass, then tag exactly that commit
   (`git tag -a vX.Y.Z <sha>`). The first stable release is `v0.2.0`: until then `main` publishes
   `0.2.0-preview.N`, which sorts above the published `0.1.0-alpha.*` packages and below `0.2.0`.
   Publish refuses any version that is not higher than what nuget.org already has (see
   [Version guard](#version-guard)).

After the tag's Publish run has pushed `X.Y.Z` to nuget.org and created the GitHub release, in a
follow-up commit on `main` (its push publishes the first `X.(Y+1).0-preview.N`):

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
   version (`0.2.0`). `dotnet pack` then downloads that version of each package from
   nuget.org and fails on breaking changes against it (`EnablePackageValidation` is already on).
   Raise the baseline when a later release intentionally breaks compatibility (a new major
   version, or a new minor version while the major version is 0). Previews (`-preview.N`) are
   never used as a baseline.
4. `GitVersion.yml` `next-version: 0.2.0` can stay: once `v0.2.0` exists it no longer has any
   effect (verified: the next commit is `0.3.0-preview.1`).

### Re-running a publish

Re-running a failed Publish run (or Actions -> **Publish** -> *Run workflow* on `main` or a tag)
is safe: already-pushed packages are skipped (`--skip-duplicate`) and existing release assets
are replaced. A manual run on `main` publishes the version of `main`'s current HEAD.

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

   The policy has no branch or tag filter: it matches any run of `publish.yml` in this
   repository, so it covers both pushes to `main` (previews) and `v*` tags (stable). If you add
   a GitHub environment to the publish job later, the policy's environment must match it, and
   the environment's deployment branch/tag rules must allow both `main` and `v*` tags.
2. In GitHub: **Settings → Secrets and variables → Actions → New repository secret**
   `NUGET_USER` = the nuget.org username that owns the policy. The `NuGet/login` step reads it.
3. Delete the old `NUGET_API_KEY` repository secret and revoke the key on nuget.org; it is no
   longer used.

A new policy may start as temporarily active until the first successful publish binds it to
the repository; see the nuget.org documentation.

The GitHub release uses the built-in `GITHUB_TOKEN` (job permission `contents: write`); no
extra secret is required.

## Troubleshooting

### Publish fails at "Check ref" or "Calculate version"
The tag is not a stable `vMAJOR.MINOR.PATCH` (e.g. `v1.2.3-beta.1`), or a manual run was
started on a ref other than `main` or a `v*` tag. Nothing was published. Delete the tag
(`git push origin --delete v1.2.3-beta.1`) and push a valid one; previews come from `main`.

### A push to `main` did not publish
- The Build job failed (tests, format check, pack): nothing is published until it passes
- The publish job was skipped because `main`'s HEAD carries a release tag; the tag's run
  publishes that version
- The run was still pending when a newer push arrived and was replaced by the newer run
  (see the concurrency note under [Publish](#publish-githubworkflowspublishyml))

### Package publish fails
- `NuGet login` fails: no matching trusted publishing policy (check owner, repository and
  workflow file name `publish.yml`), the policy owner doesn't own the package, or the
  `NUGET_USER` secret is missing or isn't the policy owner's nuget.org username
- `403 Forbidden` on push: the policy's owner account doesn't own (or can't create) the
  package ID
- A version already on nuget.org is skipped (`--skip-duplicate`), not overwritten. Bump the tag.
- `Check version is higher than nuget.org` fails: the version is not higher than the highest
  version of that package on nuget.org (listed or unlisted). Tag a higher version, or check
  `next-version` in `GitVersion.yml` for `main` builds

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

- No long-lived NuGet credentials are stored: the `publish` job (only for pushes to `main` and
  stable `v*` tags, after the tests pass) gets a one-hour key via OIDC trusted publishing; only
  that job has `id-token: write`
- Pull requests (including from forks) run `ci.yml` only, with `contents: read`, and can never
  publish
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
