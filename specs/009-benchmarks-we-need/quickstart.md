# Quickstart: Performance Baseline Benchmarks

**Feature**: 009-benchmarks-we-need
**Audience**: Guacamole framework developers and maintainers
**Time to Complete**: ~15 minutes

## Prerequisites

Before running benchmarks, ensure:
- [ ] .NET 10.0 RC1 SDK installed
- [ ] Guacamole repository cloned
- [ ] Solution builds successfully (`dotnet build`)
- [ ] All existing tests pass (`dotnet test`)

## Step 1: Navigate to Benchmarks Project

```bash
cd benchmarks/Guacamole.Benchmarks
```

**Expected**: Directory contains `Guacamole.Benchmarks.csproj`

---

## Step 2: Build Benchmarks in Release Mode

```bash
dotnet build --configuration Release
```

**Expected Output**:
```
Build succeeded.
Guacamole.Benchmarks -> /path/to/bin/Release/net10.0/Guacamole.Benchmarks.dll
```

**Troubleshooting**:
- If build fails with missing BenchmarkDotNet: Run `dotnet restore` first
- If Guacamole.Core not found: Build solution root first

---

## Step 3: Run All Benchmarks

```bash
dotnet run --configuration Release
```

**Expected Behavior**:
1. BenchmarkDotNet prints ASCII art banner
2. Lists discovered benchmarks (Simple, Complex, Caching)
3. Shows "// Validating benchmarks" message
4. Executes warm-up iterations (3x per benchmark)
5. Executes measurement iterations (10x per benchmark)
6. Displays progress: `WorkloadWarmup 1: X iterations`
7. Generates summary table with results

**Expected Duration**: 2-5 minutes (depends on hardware)

**Sample Output**:
```
| Method                      | Mean       | Error     | StdDev    | Allocated |
|---------------------------- |-----------:|----------:|----------:|----------:|
| MeasureBasicProviderExecution| 1.234 μs   | 0.045 μs  | 0.089 μs  | 128 B     |
| MeasureComplexScenario      | 15.678 μs  | 0.523 μs  | 1.023 μs  | 2.5 KB    |
| CompareCachedVsUncached_Cold| 15.234 μs  | 0.412 μs  | 0.834 μs  | 2.4 KB    |
| CompareCachedVsUncached_Warm| 0.523 μs   | 0.018 μs  | 0.036 μs  | 64 B      |
```

---

## Step 4: Review Results

### Console Summary
Look for the summary table (shown above) which includes:
- **Mean**: Average execution time
- **Error**: Standard error of the mean
- **StdDev**: Standard deviation (should be ≤10% of mean)
- **Allocated**: Total memory allocated

### Detailed Reports
Navigate to results directory:
```bash
cd BenchmarkDotNet.Artifacts/results/
ls -la
```

**Expected Files**:
- `Guacamole.Benchmarks.SimpleProviderBenchmarks-report.html`
- `Guacamole.Benchmarks.ComplexGraphBenchmarks-report.html`
- `Guacamole.Benchmarks.CachingBenchmarks-report.html`
- `*-report.json` (machine-readable results)
- `*-report.md` (markdown format for documentation)

**Action**: Open any `.html` file in browser for visual report with charts

---

## Step 5: Validate Variance Threshold

Check that standard deviation is within acceptable range:

```bash
# Parse JSON and validate (requires jq)
cat BenchmarkDotNet.Artifacts/results/*-report.json | \
  jq '.Benchmarks[] | select(.Statistics.StandardDeviation / .Statistics.Mean > 0.10) | .DisplayInfo'
```

**Expected Output**: Empty (no benchmarks exceed 10% variance)

**If Output Shows Results**:
- Hardware may be under load (close other applications)
- Thermal throttling may be occurring (check CPU temperature)
- Re-run benchmarks to verify reproducibility

---

## Step 6: Run Specific Benchmark (Optional)

To run only one benchmark category:

```bash
# Simple scenarios only
dotnet run --configuration Release --filter "*SimpleProvider*"

# Complex scenarios only
dotnet run --configuration Release --filter "*ComplexGraph*"

# Caching scenarios only
dotnet run --configuration Release --filter "*Caching*"
```

**Use Case**: Targeted measurement after code changes

---

## Step 7: Compare Against Baseline (Future)

Once baseline is established and stored in git:

```bash
# Run current benchmarks
dotnet run --configuration Release --exporters json

# Compare against baseline (example script)
python scripts/compare-benchmarks.py \
  --baseline benchmarks/baseline-results.json \
  --current BenchmarkDotNet.Artifacts/results/combined-report.json \
  --threshold 0.10
```

**Expected**: Script reports any regressions exceeding 10% threshold

**Note**: Baseline comparison tooling is a future enhancement

---

## Acceptance Criteria Validation

This quickstart validates the following acceptance scenarios from the spec:

### ✅ Scenario 1: Simple Provider Measurements
**Given** newly initialized project
**When** developer runs benchmark suite
**Then** receives performance measurements for simple provider operations

**Validated By**: Step 3 output shows `MeasureBasicProviderExecution` results

---

### ✅ Scenario 2: Complex Scenario Measurements
**Given** complex computation graph (≥20 nodes, ≥5 levels)
**When** benchmarks execute
**Then** performance metrics captured for complex scenarios

**Validated By**: Step 3 output shows `MeasureComplexScenario` results

---

### ✅ Scenario 3: Cache Impact Measurement
**Given** computation graph with caching enabled
**When** benchmarks run multiple queries
**Then** cache performance impact measured and reported

**Validated By**: Step 3 output shows `CompareCachedVsUncached_Cold` and `_Warm` results demonstrating caching benefit

---

### ✅ Scenario 4: Regression Identification
**Given** benchmark results from different code versions
**When** comparing measurements
**Then** developers can identify performance improvements or regressions

**Validated By**: Step 7 (future) will enable automated baseline comparison

---

## Troubleshooting

### "No benchmarks found"
**Cause**: Project not built in Release mode
**Fix**: Run `dotnet build --configuration Release` before `dotnet run --configuration Release`

### High variance (>10%)
**Causes**:
- Other applications consuming CPU/memory
- Thermal throttling
- Background OS tasks (indexing, updates)

**Fixes**:
- Close unnecessary applications
- Disable background tasks
- Let system cool down
- Re-run benchmarks

### OutOfMemoryException
**Cause**: Complex scenario too large for available RAM
**Fix**: Reduce node count or depth in ScenarioBuilder (adjust complexity definition)

### Benchmarks take too long
**Cause**: Too many iterations configured
**Fix**: Edit `BenchmarkConfig.cs` to reduce `warmupCount` and `targetCount` (trade-off: less statistical confidence)

---

## Next Steps

After establishing baseline:
1. Store results in git (e.g., `benchmarks/baseline-results.json`)
2. Configure CI to run benchmarks on every commit
3. Set up automated regression detection
4. Add new scenarios as framework evolves
5. Use benchmarks to validate optimization efforts

---

## Success Criteria

This quickstart is successful when:
- [ ] All benchmarks execute without errors
- [ ] Standard deviation ≤ 10% for all benchmarks
- [ ] Results exported to HTML/JSON/Markdown formats
- [ ] Caching benchmark shows warm run faster than cold run
- [ ] Developer understands how to run and interpret benchmarks

**Estimated Success Rate**: 95% (assuming prerequisites met and stable hardware)

---

## Constitutional Compliance

### Performance as Correctness (Principle IV)
- Quickstart validates ≤10% variance threshold
- Establishes baseline for future O(changes) complexity verification
- Memory allocations tracked per constitution requirement

### TDD with Incremental Complexity (Principle V)
- Quickstart follows progression: simple → complex → caching
- Each step validates previous step's prerequisites
- Incremental execution allows early failure detection

### Library Architecture (Principle - Justified Deviation)
- Benchmarks project is executable (BenchmarkDotNet requirement)
- Not included in distributed NuGet packages (developer tooling)
- Constitutional exception documented in complexity tracking
