# Research: Performance Baseline Benchmarks

**Feature**: 009-benchmarks-we-need
**Date**: 2025-10-02

## Overview
This document captures research findings for implementing performance baseline benchmarks using BenchmarkDotNet for the Guacamole incremental computation framework.

## Research Areas

### 1. BenchmarkDotNet Best Practices for .NET 10

**Decision**: Use BenchmarkDotNet latest stable version with .NET 10 RC1 support

**Rationale**:
- Industry standard for .NET performance benchmarking
- Automatic JIT warm-up handling (addresses FR-008)
- Built-in statistical analysis for variance detection
- Hardware information reporting for normalization baseline
- Supports multiple output formats (console, markdown, HTML, CSV)
- Integration with CI/CD pipelines via exit codes

**Key Features to Leverage**:
- `[MemoryDiagnoser]` - Track memory allocations (aligns with constitution's zero-allocation goals)
- `[SimpleJob]` - Configure iteration counts for ≤10% variance target
- `[Params]` - Parameterize scenarios (simple vs complex graphs)
- `BenchmarkSwitcher` - Run all benchmarks from single entry point
- `--filter` - Run subset of benchmarks for targeted measurement

**Alternatives Considered**:
- Manual Stopwatch-based benchmarks: Rejected due to JIT warm-up complexity and lack of statistical rigor
- Custom performance framework: Rejected - reinventing well-tested tooling

### 2. Hardware Normalization Strategies

**Decision**: Use BenchmarkDotNet's hardware detection + custom baseline ratio calculation

**Rationale**:
- BenchmarkDotNet captures CPU, memory, OS information automatically
- Store reference hardware specs in config file
- Calculate normalization factor: `measured_time * (baseline_cpu_score / current_cpu_score)`
- CPU score from PassMark or similar standardized benchmark database
- Memory normalization: linear scaling based on available RAM vs baseline

**Implementation Approach**:
```csharp
// BenchmarkConfig.cs
public class NormalizedConfig : ManualConfig
{
    public static readonly HardwareBaseline Baseline = new()
    {
        CpuScore = 2500, // PassMark score for reference CPU
        MemoryGB = 32
    };

    // Post-process results with normalization
    public override IEnumerable<IExporter> GetExporters()
    {
        yield return new NormalizedExporter(Baseline);
    }
}
```

**Alternatives Considered**:
- No normalization (raw results only): Rejected - doesn't meet FR-004 requirement
- Complex multi-factor normalization: Deferred - start simple, iterate if needed
- Cloud-based normalized environments: Rejected - adds CI complexity and cost

### 3. Computation Graph Test Scenarios

**Decision**: Use builder pattern for deterministic test graph construction

**Rationale**:
- Simple scenario: Linear chain of 5 nodes (provider → compute → compute → compute → output)
- Complex scenario: Tree structure with 20+ nodes, 5+ levels deep
- Reusable builders ensure consistent test data across benchmark runs
- Deterministic seeds for any random elements (ensures reproducibility)

**Complex Scenario Structure**:
```
Root Provider (5 inputs)
├─ Compute Layer 1 (5 nodes, each depends on 1 input)
├─ Compute Layer 2 (5 nodes, each depends on 2 layer-1 nodes)
├─ Compute Layer 3 (3 nodes, each depends on 3 layer-2 nodes)
├─ Compute Layer 4 (2 nodes, cross-dependencies)
└─ Output Layer (1 node, aggregates all results)
Total: 21 nodes, 5 levels deep
```

**Alternatives Considered**:
- Random graph generation: Rejected - hurts reproducibility (≤10% variance requirement)
- Real-world compiler graph: Deferred - premature, establish baseline first
- Graph loading from files: Rejected - adds I/O variance

### 4. Caching Benchmark Methodology

**Decision**: Compare cold-cache vs warm-cache scenarios using same graph

**Rationale**:
- Cold cache: First execution with empty cache
- Warm cache: Second execution with populated cache (no input changes)
- Measure delta to quantify caching benefit
- Use `[IterationSetup]` and `[IterationCleanup]` to control cache state
- Report as percentage improvement: `(cold_time - warm_time) / cold_time * 100`

**Cache Scenarios**:
1. Full cache miss (all nodes recompute)
2. Partial cache hit (50% of nodes cached, others recompute)
3. Full cache hit (100% cached, verify lookup overhead)

**Alternatives Considered**:
- Cache hit rate metrics only: Rejected - time measurement more valuable
- Multiple cache sizes: Deferred - single LRU configuration for baseline
- Cache eviction benchmarks: Deferred - future enhancement

### 5. Variance Control and Statistical Significance

**Decision**: Use BenchmarkDotNet's statistical engine with custom thresholds

**Rationale**:
- `[SimpleJob(warmupCount: 3, targetCount: 10)]` - Balance precision vs runtime
- BenchmarkDotNet calculates standard deviation automatically
- Post-processing validation: fail CI if StdDev > 10% of mean
- Use `[Outliers]` attribute to detect and flag anomalies
- Report median (more robust than mean for skewed distributions)

**Validation Approach**:
```csharp
[Test]
public void Benchmark_Variance_Within_Threshold()
{
    var results = RunBenchmark<SimpleProviderBenchmarks>();
    var variance = results.StdDev / results.Mean;
    Assert.True(variance <= 0.10, $"Variance {variance:P} exceeds 10% threshold");
}
```

**Alternatives Considered**:
- Fixed iteration count: Rejected - BenchmarkDotNet's adaptive iteration is more reliable
- Manual statistical analysis: Rejected - BenchmarkDotNet provides this
- Strict 5% threshold: Rejected - too strict for CI environment variance

## Constitutional Alignment

### Performance as Correctness (Principle IV)
- Benchmarks establish the baseline for "O(changes) complexity verification"
- ≤10% variance threshold ensures performance regressions are detectable
- Hardware normalization allows meaningful cross-environment comparison
- Statistical rigor from BenchmarkDotNet meets "bounded and predictable" requirement

### TDD with Incremental Complexity (Principle V)
- Benchmark validation tests ensure benchmarks compile and execute
- Progression: simple → complex → caching scenarios
- XUnit tests validate scenario builders before benchmark execution
- BenchmarkDotNet is performance testing layer (constitutional requirement)

### Modern C# Features (Principle VI)
- Source generators not needed (benchmarks are manual test code)
- Spans/Memory for zero-allocation verification in diagnostics
- Records for immutable scenario configuration
- Pattern matching for result analysis

## Open Questions
None - all ambiguities resolved during /clarify phase.

## Dependencies
- **BenchmarkDotNet**: Latest stable (.NET 10 compatible)
- **Guacamole.Core**: Existing incremental engine (benchmark target)
- **Microsoft.Extensions.DependencyInjection**: Service setup for scenarios
- **XUnit v3**: Validation tests for benchmark infrastructure

## Next Steps
Proceed to Phase 1: Design contracts for benchmark scenarios and result formats.
