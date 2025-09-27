# Research Results: Initial Solution Structure

**Date**: 2025-09-27
**Feature**: Initial Solution Structure
**Branch**: 003-solution-structure-we

## Package Versions and Dependencies

### Decision: .NET 10 RC1 Package Versions
**Rationale**: Using the latest RC1 versions for all packages to ensure compatibility with .NET 10
**Alternatives considered**:
- .NET 9 stable versions - rejected due to missing .NET 10 features
- Preview versions - rejected for stability concerns

**Resolved versions**:
- **.NET SDK**: 10.0.0-rc.1.25451.107 (Released September 9, 2025)
- **XUnit v3**: 3.0.1 (Latest version with .NET 10 support)
- **Verify.SourceGenerators**: 2.5.0
- **BenchmarkDotNet**: 0.15.4 (Updated September 24, 2025)
- **Microsoft.Extensions.DependencyInjection**: Ships with .NET 10 RC1 runtime
- **GitVersion.MsBuild**: 6.4.0
- **Microsoft.SourceLink.GitHub**: 8.0.0

### Decision: Visual Studio Tooling
**Rationale**: .NET 10 RC1 officially requires VS 2026 Preview, but SDK 10.0.1xx still works with VS2022
**Alternatives considered**:
- VS 2026 Preview - not suitable for production code
- VS Code with C# Dev Kit - viable alternative for development

**Resolution**: Use VS2022 17.14.13 Preview 1.0 with .NET SDK 10.0.1xx for now

## GitVersion Configuration

### Decision: GitHub Flow with Semantic Versioning
**Rationale**: Simpler workflow suitable for library development with continuous delivery
**Alternatives considered**:
- GitFlow - too complex for library development
- Manual versioning - error-prone and inconsistent

**Key configuration elements**:
- Single GitVersion.yml at repository root
- Synchronized versioning across all packages
- Commit message-based version bumping (+semver: major/minor/patch)
- Pre-release labels: alpha, beta, rc

## Directory Structure

### Decision: Category-based folder organization
**Rationale**: Clear separation of concerns, standard .NET convention
**Alternatives considered**:
- Flat structure - becomes unwieldy as solution grows
- Domain-based - overcomplicated for initial setup

**Structure**:
```
src/
├── Guacamole/                    # Core library
├── Guacamole.SourceGenerators/   # Source generators

tests/
├── Guacamole.Tests/              # Unit tests
├── Guacamole.SourceGenerators.Tests/  # Snapshot tests

/
├── Directory.Build.props         # Shared properties
├── Directory.Build.targets       # Shared targets
└── GitVersion.yml               # Version configuration
```

## Build Configuration

### Decision: Single Directory.Build.props at root
**Rationale**: Centralized configuration, easier maintenance
**Alternatives considered**:
- Hierarchical props files - unnecessary complexity for initial setup
- Per-project configuration - duplication and maintenance burden

**Key properties**:
- TargetFramework: net10.0
- LangVersion: latest
- Nullable: enable
- TreatWarningsAsErrors: true
- IsPackable: false (for test projects)
- Deterministic builds enabled
- Source Link enabled
- Symbol packages (snupkg format)

## Testing Strategy

### Decision: XUnit v3 with Microsoft Testing Platform
**Rationale**: Latest testing framework with native .NET 10 support
**Alternatives considered**:
- XUnit v2 - outdated, lacks MTP support
- NUnit - less integration with .NET tooling
- MSTest - less community adoption

**Test structure**:
- Guacamole.Tests: Unit tests with theories for parameterized testing
- Guacamole.SourceGenerators.Tests: Snapshot testing with Verify.SourceGenerators
- BenchmarkDotNet for performance benchmarks

## Package Distribution

### Decision: NuGet.org with synchronized versions
**Rationale**: Standard distribution channel, simplified version management
**Alternatives considered**:
- GitHub Packages - less discoverability
- Independent versioning - complexity with dependency management

**Package configuration**:
- Guacamole: Core library package
- Guacamole.SourceGenerators: Analyzer package
- Both packages share version numbers via GitVersion
- Symbol packages for debugging support
- README.md included in packages

## Known Issues and Mitigations

1. **Visual Studio 2026 requirement**: Use VS2022 with SDK 10.0.1xx as workaround
2. **RC1 support expiry**: Support ends October 14, 2025 - plan migration to final .NET 10
3. **Go-live license**: Production use allowed with Microsoft support

## Next Steps

All technical decisions have been resolved. Ready to proceed with Phase 1: Design & Contracts.
