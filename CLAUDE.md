# guacamole Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-09-27

## Active Technologies
- C# 13 / .NET 10.0 RC1 + Microsoft.Extensions.DependencyInjection, System.Collections.Immutable (003-solution-structure-we)
- C# 13 / .NET 10 RC1 + Microsoft.Extensions.DependencyInjection, Microsoft.CodeAnalysis.CSharp (source generators), Verify.SourceGenerators (snapshot testing) (004-poc-investigate-what)
- N/A (framework library) (004-poc-investigate-what)
- N/A (documentation/analysis task) + N/A (analysis of Roslyn API patterns) (005-roslyn-analysis-create)
- N/A (markdown documents only) (005-roslyn-analysis-create)
- C# 13 / .NET 10.0 RC1 + Microsoft.Extensions.DependencyInjection, Microsoft.CodeAnalysis.CSharp (source generators), existing Guacamole.Core infrastructure (007-computingcontextbase-we-need)
- In-memory computation graph with LRU caching (007-computingcontextbase-we-need)
- C# 13 / .NET 10.0 RC1 + GitHub Actions, GitVersion, NuGet CLI, dotnet SDK, XUnit v3 (008-ci-pipelines-we)
- N/A (CI/CD infrastructure, not data storage) (008-ci-pipelines-we)
- C# 13 / .NET 10.0 RC1 + BenchmarkDotNet, Microsoft.Extensions.DependencyInjection, existing Guacamole.Core infrastructure (009-benchmarks-we-need)
- In-memory benchmark results (BenchmarkDotNet standard output formats) (009-benchmarks-we-need)
- C# 13 / .NET 10.0 RC1 + Microsoft.CodeAnalysis.CSharp (Roslyn source generators), Microsoft.Extensions.DependencyInjection, System.Collections.Immutable (010-property-genration-current)
- N/A (source generator - compile-time code generation) (010-property-genration-current)

## Project Structure
```
src/
tests/
```

## Commands
# Add commands for C# 13 / .NET 10.0 RC1

## Code Style
C# 13 / .NET 10.0 RC1: Follow standard conventions

## Recent Changes
- 010-property-genration-current: Added C# 13 / .NET 10.0 RC1 + Microsoft.CodeAnalysis.CSharp (Roslyn source generators), Microsoft.Extensions.DependencyInjection, System.Collections.Immutable
- 009-benchmarks-we-need: Added C# 13 / .NET 10.0 RC1 + BenchmarkDotNet, Microsoft.Extensions.DependencyInjection, existing Guacamole.Core infrastructure
- 008-ci-pipelines-we: Added C# 13 / .NET 10.0 RC1 + GitHub Actions, GitVersion, NuGet CLI, dotnet SDK, XUnit v3

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
