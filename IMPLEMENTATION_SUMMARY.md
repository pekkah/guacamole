# Implementation Summary (Historical)

This file used to contain a long status report written while the push-based architecture was being implemented (October 2025). That report had become self-contradictory and described intermediate states that no longer exist (for example eager cache invalidation, a separate `SourcePropertyGenerator`, and tests "not yet updated"). It has been replaced by this short summary. The original text is available in the git history of this file.

## Current state

- **API**: Data enters a `[ComputingContext]` through generated push-based sources (`{Property}Source.Push/Remove/Replace/Clear` for collections, `{Property}Source.Set` for single values). The former mutation methods and `SaveChanges()` were removed. See [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md).
- **Caching**: Lazy, version-based. A source change increments only that source's version; providers revalidate their cached values when read and keep their version when a recomputed value is equal to the previous one. See [docs/performance-guide.md](docs/performance-guide.md).
- **Source generator**: A single incremental generator (`ComputingContextGenerator`, built on `ForAttributeWithMetadataName`) emits the constructor, the input property implementations and the source accessors, and reports diagnostics GUAC0003-GUAC0007. See [docs/generated-code-reference.md](docs/generated-code-reference.md).
- **Changes since the original report**: see [CHANGELOG.md](CHANGELOG.md).

## Where to look

- [README.md](README.md) - overview, quick start, thread-safety notes
- [docs/quickstart.md](docs/quickstart.md) - step-by-step guide
- [examples/](examples/README.md) - runnable examples
- [PUSH_BASED_ARCHITECTURE_RESEARCH.md](PUSH_BASED_ARCHITECTURE_RESEARCH.md) - original design research (historical; parts of it differ from the implementation)
