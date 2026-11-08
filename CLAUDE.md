# guacamole Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-09-27

## Active Technologies
- C# 15 (`LangVersion=latest`) / .NET 11 RC3 SDK, pinned in `global.json` (11.0.100-rc.3.26522.148)
- Runtime (`src/Guacamole`, net11.0): Microsoft.Extensions.DependencyInjection, System.Collections.Immutable; in-memory computation graph with LRU value cache and version-based lazy cache validation
- Source generator (`src/Guacamole.SourceGenerators`, netstandard2.0): Microsoft.CodeAnalysis.CSharp 5.9.0, incremental generator using `ForAttributeWithMetadataName`; diagnostics GUAC0003-GUAC0009 (keep `AnalyzerReleases.*.md` and `DiagnosticDescriptors.cs` in sync)
- Tests: xUnit v3 on Microsoft.Testing.Platform (selected in `global.json`); generator snapshot tests use Verify.SourceGenerators and Verify.XunitV3 33.x; Verify 33's SponsorCheck gate (SC021) is satisfied by `Verify_SponsorshipExemption=OpenSource` with `Verify_SponsorshipExemptionUntil=2027-11` (format yyyy-MM, max 12 months out) in the test csproj, which must be renewed before November 2027 or the build fails
- Benchmarks: BenchmarkDotNet (`benchmarks/Guacamole.Benchmarks`)
- CI/CD: GitHub Actions, GitVersion 6.8.2; every push to `main` publishes `X.Y.Z-preview.N` to nuget.org (`0.2.0-preview.N` until the first stable tag `v0.2.0`; must exceed every version already on nuget.org), a stable `vX.Y.Z` tag publishes `X.Y.Z` + GitHub release (prerelease tags rejected), PRs never publish (see `docs/ci-cd.md`)

## Project Structure
```
src/Guacamole/                    runtime library
src/Guacamole.SourceGenerators/   [ComputingContext] source generator
tests/                            Guacamole.Tests, Guacamole.SourceGenerators.Tests, Guacamole.Benchmarks.Tests
benchmarks/                       BenchmarkDotNet benchmarks
examples/                         runnable example projects (part of guacamole.sln)
docs/                             user documentation
```

## Commands
```bash
dotnet build guacamole.sln        # builds src, tests, benchmarks and examples
dotnet test                       # runs all test projects (Microsoft.Testing.Platform)
dotnet run --project examples/BasicSourceUsage
cd benchmarks/Guacamole.Benchmarks && dotnet run -c Release
```

## Code Style
C# 15 / .NET 11.0 RC3: Follow standard conventions. LF line endings on every platform (`.gitattributes` `* text=auto eol=lf`, `.editorconfig` `end_of_line = lf`); CI runs `dotnet format guacamole.sln --verify-no-changes` on Linux and Windows. Format only sees source generator output for the configuration it evaluates (Debug unless `Configuration` is set) and only if that configuration was built, so CI sets `Configuration=Release` for its format step after the Release build (#72). Reproduce CI with `dotnet build guacamole.sln -c Release && Configuration=Release dotnet format guacamole.sln --verify-no-changes --no-restore` (see `docs/ci-cd.md`, Format check)

## Recent Changes
- 010-property-genration-current: Added C# 15 / .NET 11.0 RC3 + Microsoft.CodeAnalysis.CSharp (Roslyn source generators), Microsoft.Extensions.DependencyInjection, System.Collections.Immutable
- 009-benchmarks-we-need: Added C# 15 / .NET 11.0 RC3 + BenchmarkDotNet, Microsoft.Extensions.DependencyInjection, existing Guacamole.Core infrastructure
- 008-ci-pipelines-we: Added C# 15 / .NET 11.0 RC3 + GitHub Actions, GitVersion, NuGet CLI, dotnet SDK, XUnit v3

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
