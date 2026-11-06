# guacamole Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-09-27

## Active Technologies
- C# 15 (`LangVersion=latest`) / .NET 11 RC3 SDK, pinned in `global.json` (11.0.100-rc.3.26522.148)
- Runtime (`src/Guacamole`, net11.0): Microsoft.Extensions.DependencyInjection, System.Collections.Immutable; in-memory computation graph with LRU value cache and version-based lazy cache validation
- Source generator (`src/Guacamole.SourceGenerators`, netstandard2.0): Microsoft.CodeAnalysis.CSharp 5.9.0, incremental generator using `ForAttributeWithMetadataName`; diagnostics GUAC0003-GUAC0009 (keep `AnalyzerReleases.*.md` and `DiagnosticDescriptors.cs` in sync)
- Tests: xUnit v3 on Microsoft.Testing.Platform (selected in `global.json`); generator snapshot tests use Verify.SourceGenerators and Verify.XunitV3 32.x (33.x requires a sponsorship property, so stay on 32.x)
- Benchmarks: BenchmarkDotNet (`benchmarks/Guacamole.Benchmarks`)
- CI/CD: GitHub Actions, GitVersion; NuGet publishing only from `v*` tags (see `docs/ci-cd.md`)

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
C# 15 / .NET 11.0 RC3: Follow standard conventions

## Recent Changes
- 010-property-genration-current: Added C# 15 / .NET 11.0 RC3 + Microsoft.CodeAnalysis.CSharp (Roslyn source generators), Microsoft.Extensions.DependencyInjection, System.Collections.Immutable
- 009-benchmarks-we-need: Added C# 15 / .NET 11.0 RC3 + BenchmarkDotNet, Microsoft.Extensions.DependencyInjection, existing Guacamole.Core infrastructure
- 008-ci-pipelines-we: Added C# 15 / .NET 11.0 RC3 + GitHub Actions, GitVersion, NuGet CLI, dotnet SDK, XUnit v3

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
