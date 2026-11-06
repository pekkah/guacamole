# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Breaking

- **Namespaces consolidated.** The `Core` segment is gone and user-facing types moved to `Guacamole`, so `using Guacamole;` is enough for typical code: sources and their extensions (`Guacamole.Sources`), `Result<TValue, TError>` (`Guacamole.Core.Common`), `ComputationError` (`Guacamole.Core.Pipeline`), `ProviderMetadata`/`SourceLocation` (`Guacamole.Core.Diagnostics`) and `ContextOperationException` (`Guacamole.Exceptions`) are now in `Guacamole`. Advanced types moved to `Guacamole.Pipeline` (from `Guacamole.Core.Pipeline` and `Guacamole.Core.Nodes`), `Guacamole.Caching` (from `Guacamole.Core.Caching`) and `Guacamole.Diagnostics` (`GuacamoleMetrics`, `MetricsConfiguration`). Generated code references the new namespaces. See [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md#namespace-consolidation).
- **Internal plumbing is no longer public.** Made `internal`: the computation graph (`ComputationGraph`, including the test-only `ReplaceNode`/`WithCachedValue` and the mutable `ValueCache`), the node types (`IComputationNode`, `IComputationNode<T>`, `ICollectionComputationNode<T>`, `IHasComparer<T>`, `ComputationContext`, `ComputationNodeBase<T>`, `CollectionComputationNodeBase<T>`), `VersionTracker`, `NodeVersionInfo`, the cache (`ICache`, `LRUCache`, `ICacheStatisticsProvider`, `CacheStatistics`), the providers' `Node`/`Graph` properties and constructors, the concrete `IncrementalSource<T>`, `IncrementalValueSource<T>` and `BatchOperation<T>` (use the interfaces), `ComputingContextBase.Graph`/`SubscribeToSource`, the node-based DI factories (`IComputationContextFactory`, `IIncrementalValueProviderFactory`, `IIncrementalValuesProviderFactory`) and the `GuacamoleMetrics` recording helpers. Contexts now create their sources with the new protected `ComputingContextBase.CreateSource<T>`/`CreateValueSource<T>` helpers, which the generated constructor calls. See [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md#internal-types-and-members).
- **Single-value `Where` produces "no value" instead of an error.** `Result<TValue, TError>` has a third state, None (`Result.None`, `IsNone`, `IsError`, three-way `Match`; `default` is None). A rejected value propagates as None through `Select`/`Where`/`Combine` without running their lambdas (errors take precedence in `Combine`), `SelectMany` and `values.Combine(value)` produce no items, and output handlers are not called (they fire again when a value reappears). `IncrementalValueProvider<T>.Value` throws `InvalidOperationException` for no value; the new `TryGetValue(out T)` returns `false` instead. See [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md#single-value-where-produces-no-value).

### Changed

- **Platform upgrade**: all projects target .NET 11 (`net11.0`) with the .NET 11 RC3 SDK pinned in `global.json`, and use `LangVersion=latest` (C# 15). Test projects moved to xUnit v3 on Microsoft.Testing.Platform. Dependencies bumped (Microsoft.Extensions.DependencyInjection 11.0 RC3, Microsoft.SourceLink.GitHub 10.0.403, Microsoft.CodeAnalysis 5.9.0).
- **Release flow**: packages are published to nuget.org only from `v*` version tags (or a manual run of the Publish workflow); the tag name is the package version. Pushes to `main` only build, test and pack. The Publish workflow reuses the CI workflow. Publishing authenticates with nuget.org **trusted publishing** (GitHub OIDC → short-lived API key) instead of a stored `NUGET_API_KEY` secret. Example projects are now part of `guacamole.sln` and built in CI. See [docs/ci-cd.md](docs/ci-cd.md).
- **Source generator rewrite** (`ComputingContextGenerator`):
  - Cacheable incremental pipeline based on `ForAttributeWithMetadataName`, with all semantic analysis producing equatable models; source and diagnostic outputs are separate branches.
  - Generated code fully qualifies all types with `global::`, so item types from other namespaces work without `using` directives.
  - Unique hint names (namespace + containing types + metadata name), so same-named contexts in different namespaces or types no longer collide.
  - Support for nested contexts (containing types re-emitted as partial), generic contexts and contexts in the global namespace; input properties keep their declared accessibility.
  - New diagnostics: **GUAC0006** (context or a containing type is not `partial`) **GUAC0007** (context does not derive from `ComputingContextBase`), **GUAC0008** (context declares its own parameterless constructor) and **GUAC0009** (constructor never chains to `: this()`). GUAC0003 and GUAC0005 are now reported at the offending property. See [docs/generated-code-reference.md](docs/generated-code-reference.md#diagnostics).
  - The disabled legacy generator and its mutation-method/`SaveChanges` code generators were deleted.
- **Breaking: push-based sources replace mutation methods.** Generated `Add{X}`/`Remove{X}`/`Replace{X}`/`Set{X}` methods, `SaveChanges()`, `SaveChangesAsync()`, `DiscardChanges()` and `HasPendingChanges` were removed. Each input property now gets a `{X}Source` accessor (`IIncrementalSource<T>` with `Push`/`Remove`/`Replace`/`Clear`, or `IIncrementalValueSource<T>` with `Set`), and changes take effect immediately. See [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md).
- **Breaking: version-based lazy invalidation.** A source change only increments the source's version; cached values are revalidated when read, and a provider whose recomputed value is equal keeps its version so dependents stay cached.
- **Breaking: property convention reversal.** `partial` get-only provider properties (`{ get; }`) are inputs implemented by the generator; non-`partial` properties with a setter are user-managed. See [docs/migration/010-property-convention-reversal.md](docs/migration/010-property-convention-reversal.md).

### Removed

- Diagnostics **GUAC0001** (duplicate property name) and **GUAC0002** (conflicting type parameters). They could never be reported; their IDs are retired and will not be reused.

### Fixed

- **Node reuse**: identical operator calls (same operator, same ordered inputs, equal delegates and comparers) return the existing node, so pipelines built repeatedly (e.g. in property getters with non-capturing lambdas) share one cache and don't grow the graph.
- **Node ID collisions**: node IDs were derived from the operation name and parent IDs, so two different operator calls on the same parent (for example two `Select`s with different lambdas) shared one cache slot and could return each other's values. Every operator call now creates a node with a unique ID.
- **Caching after source changes**: derived values are kept cached across source changes and are recomputed only when an input they depend on (including inputs reached through intermediate, unread stages) has changed. Dependency versions are snapshotted before computing, so a change that happens during a computation is picked up by the next read.
- **Thread safety**: `LRUCache.TryGet` performs lookup, LRU update and value read atomically; storing a computed value with its version information is atomic; output registration, source subscription and `Dispose()` are synchronized.
- **Output handlers**: all registered handlers run even if one throws; afterwards the exceptions are rethrown together as an `AggregateException`.
- **Dispose**: disposing a context unsubscribes it from its sources (so they neither keep it alive nor notify it) and clears its output registrations; notifications that race with `Dispose()` are ignored.
- **Collection operators**: exceptions thrown by lambdas in collection `Select`/`Where`/`SelectMany` are returned as error results instead of escaping during enumeration.
- **Generated code**: compiles without errors or warnings for item types from other namespaces, nested and generic contexts (verified by snapshot tests that compile the output).

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
