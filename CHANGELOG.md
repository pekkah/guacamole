# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

#### ⚠️ BREAKING CHANGE: Property Convention Reversal

**The property generation convention has been reversed:**

- **OLD:** Properties WITH setters (`{ get; set; }`) → Auto-managed (mutation methods generated)
- **NEW:** Properties WITHOUT setters (`{ get; }`) → Auto-managed (mutation methods generated)

**Migration Required:**

To migrate existing code:
1. Remove setters from properties where you want auto-generated mutation methods
2. Add setters to properties you manage manually

**Before:**
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    public partial IncrementalValueProvider<string> Name { get; set; }  // Auto-managed
}
```

**After:**
```csharp
[ComputingContext]
public partial class MyContext : ComputingContextBase
{
    public partial IncrementalValueProvider<string> Name { get; }  // Auto-managed
}
```

**Rationale:** The new convention is more intuitive and aligns with C# 13's partial property semantics. Get-only properties signal "I need an implementation" and the framework provides it. Properties with setters signal "I'll manage this myself."

See [Migration Guide](docs/migration/010-property-convention-reversal.md) for detailed instructions.

### Added

- Comprehensive migration guide for property convention reversal
- Updated API documentation with new examples
- Improved error messages for common migration scenarios

### Fixed

- PropertyGenerator now correctly handles the reversed logic for property classification
- All snapshot tests updated to reflect new generated code structure
- Examples updated to use new convention

## [0.0.1] - 2024-10-02

### Added

- Initial source generator implementation
- ComputingContextBase with change tracking
- IncrementalValueProvider and IncrementalValuesProvider
- Strongly-typed change records (zero boxing)
- Mutation methods (Add/Remove/Replace/Set)
- SaveChanges/DiscardChanges infrastructure
- Computation graph integration
- RegisterOutput functionality
- BenchmarkDotNet performance benchmarks
- Comprehensive test suite
- CI/CD pipeline with GitHub Actions

[Unreleased]: https://github.com/pekkah/guacamole/compare/v0.0.1...HEAD
[0.0.1]: https://github.com/pekkah/guacamole/releases/tag/v0.0.1
