# Contract: Benchmark Execution

**Feature**: 009-benchmarks-we-need
**Type**: Behavioral Contract
**Scope**: BenchmarkDotNet integration and execution flow

## Purpose
Define the expected behavior and output format for benchmark execution, ensuring consistency across runs and environments.

## Contract Specifications

### Benchmark Runner Entry Point

**Operation**: `Program.Main(string[] args)`

**Preconditions**:
- BenchmarkDotNet NuGet package installed
- Guacamole.Core assembly available
- Optional: `--filter` argument for selective execution

**Postconditions**:
- Exit code 0 if all benchmarks completed successfully
- Exit code != 0 if any benchmark failed or variance exceeded threshold
- Results written to `BenchmarkDotNet.Artifacts/results/` directory

**Output Formats**:
- Console: Human-readable summary table
- Markdown: `*-report.md` files
- JSON: `*-report.json` for programmatic analysis
- HTML: `*-report.html` for visual inspection

**Error Conditions**:
- Missing dependencies → Exit code 1, error message to stderr
- Benchmark compilation failure → Exit code 2, compiler errors
- Variance threshold exceeded → Exit code 3, statistical report

---

### Simple Provider Benchmark

**Operation**: `SimpleProviderBenchmarks.MeasureBasicProviderExecution()`

**Scenario Description**:
Linear computation graph with 5 nodes:
```
Input → Compute1 → Compute2 → Compute3 → Output
```

**Preconditions**:
- Clean DI container for each iteration
- Empty cache state
- Deterministic input data (seed=42)

**Expected Behavior**:
1. Register 5 providers in DI container
2. Build computation graph with linear dependencies
3. Execute query from Input to Output
4. Measure end-to-end execution time

**Success Criteria**:
- Execution completes without exceptions
- StandardDeviationPercent ≤ 10%
- Memory allocations tracked and reported
- Result value matches expected output

**Performance Baseline**:
- Target: Establish baseline (no specific threshold yet)
- Warn if: Variance > 10%
- Fail if: Execution throws exception

---

### Complex Graph Benchmark

**Operation**: `ComplexGraphBenchmarks.MeasureComplexScenarioExecution()`

**Scenario Description**:
Tree-structured computation graph with ≥20 nodes, ≥5 dependency levels:
```
Root (5 inputs)
├─ L1: 5 nodes (1 dependency each)
├─ L2: 5 nodes (2 dependencies each)
├─ L3: 3 nodes (3 dependencies each)
├─ L4: 2 nodes (cross-dependencies)
└─ L5: 1 output (aggregation)
Total: 21 nodes, 5 levels
```

**Preconditions**:
- Clean DI container
- Empty cache
- Deterministic scenario builder (TreeTopology, nodeCount=21, depth=5)

**Expected Behavior**:
1. ScenarioBuilder constructs graph matching specification
2. All 21 nodes registered in correct dependency order
3. Query execution traverses full graph
4. Incremental engine executes only necessary nodes

**Success Criteria**:
- Graph structure validated (21 nodes, 5 levels)
- Execution completes without stack overflow
- StandardDeviationPercent ≤ 10%
- O(changes) complexity observable (no redundant computation)

**Performance Baseline**:
- Target: Establish baseline for complex scenario
- Expected: Slower than simple but not exponentially
- Warn if: Execution time > 10x simple scenario (indicates issue)

---

### Caching Benchmark

**Operation**: `CachingBenchmarks.CompareC achedVsUncached()`

**Scenario Description**:
Same graph executed twice:
1. Cold cache: First execution, all nodes compute
2. Warm cache: Second execution, no input changes, all nodes cached

**Preconditions**:
- Graph built once, used for both runs
- Cache explicitly cleared before cold run
- Cache explicitly populated (via cold run) before warm run

**Expected Behavior**:
Cold Cache Run:
1. Execute query with empty cache
2. All nodes compute and cache results
3. Record execution time (ColdCacheTimeNs)

Warm Cache Run:
1. Execute same query (no input changes)
2. All nodes retrieve from cache (no recomputation)
3. Record execution time (WarmCacheTimeNs)

Comparison:
```
ImprovementPercent = ((Cold - Warm) / Cold) * 100.0
```

**Success Criteria**:
- WarmCacheTimeNs < ColdCacheTimeNs (caching provides benefit)
- ImprovementPercent > 50% (at minimum)
- Cache hit rate = 100% for warm run
- StandardDeviationPercent ≤ 10% for both runs

**Performance Baseline**:
- Target: Quantify caching benefit
- Expected: Warm run significantly faster (75-90% improvement)
- Warn if: Improvement < 50% (cache may not be working correctly)
- Fail if: Warm run slower than cold (indicates cache overhead issue)

---

### Hardware Normalization

**Operation**: `NormalizedExporter.PostProcessResults(BenchmarkReport report)`

**Input**: Raw BenchmarkDotNet results with hardware information

**Preconditions**:
- HardwareBaseline configuration available
- BenchmarkDotNet captured hardware info (CPU, RAM, OS)

**Expected Behavior**:
1. Extract current hardware CpuScore (from PassMark database or similar)
2. Calculate normalization factor: `baseline_score / current_score`
3. Apply to all execution times: `normalized_time = raw_time * factor`
4. Append normalized results to output formats

**Postconditions**:
- Original raw results preserved
- Normalized results appended with suffix `_normalized`
- Normalization factor documented in report
- Baseline hardware specs included for reference

**Success Criteria**:
- Normalization factor calculated successfully
- Normalized results mathematically correct
- Both raw and normalized results available for analysis

**Error Handling**:
- If CpuScore not found → Log warning, skip normalization (use raw only)
- If baseline not configured → Use raw results as-is

---

## Validation Tests

### Contract Test: Benchmark Execution Succeeds

**Test**: `BenchmarkValidationTests.AllBenchmarksExecuteWithoutErrors()`

**Assertion**:
```csharp
var result = Program.Main(new[] { "--filter", "*" });
Assert.Equal(0, result); // Exit code 0 = success
```

**Purpose**: Ensure benchmarks compile and run (smoke test)

---

### Contract Test: Variance Within Threshold

**Test**: `BenchmarkValidationTests.VarianceWithinAcceptableRange()`

**Assertion**:
```csharp
var results = ParseBenchmarkResults("results.json");
foreach (var result in results)
{
    var variance = result.StandardDeviation / result.Mean;
    Assert.True(variance <= 0.10,
        $"{result.BenchmarkName}: Variance {variance:P} exceeds 10% threshold");
}
```

**Purpose**: Enforce FR-009 reproducibility requirement

---

### Contract Test: Complex Scenario Graph Structure

**Test**: `ScenarioBuilderTests.ComplexScenario_CreatesCorrectStructure()`

**Assertion**:
```csharp
var scenario = new ScenarioBuilder(TreeTopology, nodeCount: 21, depth: 5).Build();
Assert.Equal(21, scenario.Graph.Nodes.Count);
Assert.Equal(5, scenario.Graph.MaxDepth);
Assert.True(scenario.IsComplex); // nodeCount ≥ 20 AND depth ≥ 5
```

**Purpose**: Validate test scenario matches specification from clarifications

---

### Contract Test: Caching Improves Performance

**Test**: `CachingBenchmarks.WarmCacheFasterThanCold()`

**Assertion**:
```csharp
var (coldTime, warmTime) = ExecuteBothCacheScenarios();
Assert.True(warmTime < coldTime,
    $"Warm cache ({warmTime}ns) not faster than cold ({coldTime}ns)");

var improvement = ((coldTime - warmTime) / (double)coldTime) * 100.0;
Assert.True(improvement >= 50.0,
    $"Cache improvement {improvement:F1}% below 50% minimum");
```

**Purpose**: Validate caching provides measurable benefit (FR-003)

---

## Contract Lifecycle

### Setup Phase
1. Install BenchmarkDotNet NuGet package
2. Configure HardwareBaseline in config
3. Create ScenarioBuilders for each test case
4. Generate validation tests (contract tests above)

### Execution Phase
1. Run `dotnet run --configuration Release` (benchmarks project)
2. BenchmarkDotNet executes all benchmarks
3. Statistical analysis performed automatically
4. Results exported to multiple formats

### Validation Phase
1. Parse exported results (JSON format)
2. Validate variance thresholds
3. Compare against historical baselines (if exist)
4. Fail CI if variance or performance regressions detected

### Maintenance Phase
1. Update baseline as hardware changes
2. Add new scenarios as framework evolves
3. Archive results for regression tracking

---

## Constitutional Compliance

### Deterministic Execution (Principle III)
- All scenarios use deterministic seeds
- No timestamps or random values in benchmark hot paths
- Same inputs → same results (within variance threshold)

### Performance as Correctness (Principle IV)
- Variance threshold enforced as correctness requirement
- Baseline measurements establish performance expectations
- Regressions detected and failed

### TDD Compliance (Principle V)
- Contract tests run before benchmarks (failing tests initially)
- Incremental complexity: simple → complex → caching
- XUnit validates infrastructure, BenchmarkDotNet measures performance
