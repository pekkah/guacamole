# Data Model: Performance Baseline Benchmarks

**Feature**: 009-benchmarks-we-need
**Date**: 2025-10-02

## Overview
This document defines the data structures and relationships for the benchmark infrastructure. Note: Benchmarks measure existing Guacamole.Core entities; this model describes the benchmark-specific structures, not the core framework.

## Entities

### BenchmarkScenario
Represents a specific benchmark test case configuration.

**Attributes**:
- `Name`: string - Scenario identifier (e.g., "Simple_5Node_Linear", "Complex_20Node_Tree")
- `NodeCount`: int - Number of computation nodes in the graph
- `DepthLevels`: int - Maximum dependency chain depth
- `GraphStructure`: enum {Linear, Tree, DAG} - Topology type
- `IsComplex`: bool - Derived: (NodeCount ≥ 20 && DepthLevels ≥ 5)
- `Description`: string - Human-readable scenario summary

**Relationships**:
- Used by → BenchmarkResult (one-to-many)
- Configured by → ScenarioBuilder (one-to-one)

**Validation Rules**:
- NodeCount must be > 0
- DepthLevels must be > 0
- For complex scenarios: NodeCount ≥ 20 AND DepthLevels ≥ 5 (per clarification)

**State Transitions**:
None - scenarios are immutable configuration

---

### HardwareBaseline
Reference hardware specification for normalization.

**Attributes**:
- `CpuModel`: string - Reference CPU model name
- `CpuScore`: int - PassMark or similar benchmark score
- `MemoryGB`: int - RAM capacity in gigabytes
- `OsVersion`: string - Operating system version
- `LastUpdated`: DateTime - When baseline was established

**Relationships**:
- Referenced by → BenchmarkResult (many-to-one)

**Validation Rules**:
- CpuScore must be > 0
- MemoryGB must be > 0

**State Transitions**:
None - baseline is configuration, not runtime state

---

### BenchmarkResult
Performance measurement outcome for a single benchmark run.

**Attributes**:
- `ScenarioName`: string - Reference to BenchmarkScenario
- `ExecutionTimeNs`: long - Raw execution time in nanoseconds
- `NormalizedTimeNs`: long - Hardware-normalized execution time
- `MemoryAllocatedBytes`: long - Total allocations during execution
- `StandardDeviationPercent`: double - Variance as percentage of mean
- `CacheStatus`: enum {Cold, Warm, Partial} - Cache state during run
- `Timestamp`: DateTime - When measurement was taken
- `HardwareInfo`: string - Current hardware identifier (for correlation)
- `IsValid`: bool - Derived: (StandardDeviationPercent ≤ 10.0)

**Relationships**:
- Measures → BenchmarkScenario (many-to-one)
- Normalized against → HardwareBaseline (many-to-one)

**Validation Rules**:
- ExecutionTimeNs must be > 0
- StandardDeviationPercent must be ≥ 0
- If StandardDeviationPercent > 10.0, mark as invalid (per clarification)
- NormalizedTimeNs = ExecutionTimeNs * (baseline_cpu_score / current_cpu_score)

**State Transitions**:
```
[Created] → [Validated] (when StandardDeviationPercent ≤ 10%)
[Created] → [Invalid] (when StandardDeviationPercent > 10%)
```

---

### CacheBenchmarkComparison
Comparative results between cached and uncached executions.

**Attributes**:
- `ScenarioName`: string - Which scenario was measured
- `ColdCacheTimeNs`: long - Execution time with empty cache
- `WarmCacheTimeNs`: long - Execution time with populated cache
- `ImprovementPercent`: double - Derived: ((Cold - Warm) / Cold) * 100
- `CacheHitRate`: double - Percentage of cache hits (0.0 to 100.0)
- `Timestamp`: DateTime - When comparison was performed

**Relationships**:
- Compares → BenchmarkResult (cold) and BenchmarkResult (warm)

**Validation Rules**:
- ColdCacheTimeNs must be > 0
- WarmCacheTimeNs must be > 0
- CacheHitRate must be between 0.0 and 100.0
- Typically WarmCacheTimeNs < ColdCacheTimeNs (but not enforced - could indicate issue)

**Derived Calculations**:
```csharp
ImprovementPercent = ((ColdCacheTimeNs - WarmCacheTimeNs) / (double)ColdCacheTimeNs) * 100.0
```

---

### ScenarioBuilder
Factory for constructing deterministic test computation graphs.

**Attributes**:
- `Topology`: enum {Linear, Tree, DAG} - Graph structure to build
- `NodeCount`: int - Target number of nodes
- `DepthLevels`: int - Target dependency depth
- `Seed`: int - Deterministic random seed (if any randomness needed)
- `UseCaching`: bool - Whether to enable cache in built graph

**Relationships**:
- Builds → BenchmarkScenario (one-to-many)

**Validation Rules**:
- NodeCount must match topology constraints (e.g., Tree must have exponential growth)
- DepthLevels must be achievable with given NodeCount

**Lifecycle**:
```
[Configured] → [Build()] → [Returns ComputationGraph + BenchmarkScenario]
```

---

## Relationships Diagram

```
┌─────────────────────┐
│ HardwareBaseline    │
│ - CpuScore          │
│ - MemoryGB          │
└──────────┬──────────┘
           │ referenced by
           │
           ↓
┌─────────────────────┐        measures        ┌──────────────────────┐
│ BenchmarkResult     │───────────────────────→│ BenchmarkScenario    │
│ - ExecutionTimeNs   │                        │ - NodeCount          │
│ - NormalizedTimeNs  │                        │ - DepthLevels        │
│ - StdDevPercent     │                        │ - GraphStructure     │
└──────────┬──────────┘                        └──────────┬───────────┘
           │                                              │
           │ compared in                                  │ built by
           │                                              │
           ↓                                              ↓
┌──────────────────────────┐                  ┌────────────────────┐
│ CacheBenchmarkComparison │                  │ ScenarioBuilder    │
│ - ColdCacheTimeNs        │                  │ - Topology         │
│ - WarmCacheTimeNs        │                  │ - NodeCount        │
│ - ImprovementPercent     │                  │ - Seed             │
└──────────────────────────┘                  └────────────────────┘
```

## Implementation Notes

### Immutability
- All measurement results are immutable records (C# record types)
- Scenario builders produce immutable configurations
- Hardware baseline is configuration, not mutable state

### Persistence
- Results written to BenchmarkDotNet standard output formats (JSON, Markdown)
- No database persistence required for baseline establishment
- Future: Store baseline results in git for historical comparison

### Validation
- XUnit tests validate ScenarioBuilders produce graphs matching specifications
- BenchmarkDotNet validates measurement statistical properties
- Post-processing validates variance thresholds

## Constitutional Compliance

### Deterministic Execution (Principle III)
- ScenarioBuilder uses deterministic seeds
- No I/O or timestamps in benchmark hot paths
- Same scenario configuration → same graph structure

### Performance as Correctness (Principle IV)
- StandardDeviationPercent threshold enforces reproducibility requirement
- Hardware normalization enables cross-environment comparison
- Memory diagnostics track allocation goals

### Modern C# (Principle VI)
- Records for immutable result types
- Enums for type-safe state (CacheStatus, GraphStructure)
- Value tuples for lightweight result pairs
