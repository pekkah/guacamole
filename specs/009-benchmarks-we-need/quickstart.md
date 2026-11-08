# Quickstart: Performance Baseline Benchmarks

**Feature**: 009-benchmarks-we-need
**Audience**: Guacamole framework developers and maintainers
**Time to Complete**: ~15 minutes

## Prerequisites

Before running benchmarks, ensure:
- [ ] .NET SDK from `global.json` installed (11.0 RC3)
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
Guacamole.Benchmarks -> /path/to/bin/Release/net11.0/Guacamole.Benchmarks.dll
```

**Troubleshooting**:
- If build fails with missing BenchmarkDotNet: Run `dotnet restore` first
- If the `Guacamole` project is not found: build the solution root first

---

## Step 3: Run All Benchmarks

```bash
dotnet run --configuration Release
```

**Expected Behavior**:
1. BenchmarkDotNet prints ASCII art banner
2. Without `--filter`, asks which benchmark class to run (`SimpleProviderBenchmarks`,
   `ComplexGraphBenchmarks`, `CachingBenchmarks`, `IncrementalUpdateBenchmarks`,
   `CollectionChangeBenchmarks`, `MemoryOverheadBenchmarks`); pass `--filter "*"` to run all
3. Shows "// Validating benchmarks" message
4. Executes warm-up and measurement iterations (BenchmarkDotNet's default job decides how many)
5. Uses a separate process per benchmark (add `--inProcess` where spawning one does not work)
6. Displays progress: `WorkloadWarmup 1: X iterations`
7. Generates summary table with results

**Expected Duration**: a few minutes for the original three classes; the parameterized classes take longer (`--job short` for a quick look)

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
`Program.cs` writes BenchmarkDotNet's artifacts to `../reports` relative to the current
directory, i.e. `benchmarks/reports/` when run from the project directory (not
`BenchmarkDotNet.Artifacts/`). Navigate to the results directory:
```bash
cd ../reports/results/
ls -la
```

**Expected Files** (one set per benchmark class that ran, e.g. `CachingBenchmarks`):
- `Guacamole.Benchmarks.<Class>-report.html`
- `Guacamole.Benchmarks.<Class>-report.csv` (machine-readable results)
- `Guacamole.Benchmarks.<Class>-report-github.md` (markdown table for documentation)

These are BenchmarkDotNet's default exporters. No JSON report is produced unless you add
`--exporters json`. Log files go to `benchmarks/reports/` itself.

**Action**: Open any `.html` file in browser for visual report with charts

---

## Step 5: Validate Variance Threshold

Compare the `StdDev` column with `Mean` in the summary table or the `-report-github.md` file.
There is no automated variance gate (spec tasks T010/T022 were superseded): BenchmarkDotNet
already reports `Error`/`StdDev` and warns about multimodal or noisy distributions.

**Expected**: StdDev well below 10% of Mean on an idle machine with the default job
(`--job short` runs only 3 iterations, so expect wider margins)

**If Output Shows Results**:
- Hardware may be under load (close other applications)
- Thermal throttling may be occurring (check CPU temperature)
- Re-run benchmarks to verify reproducibility

---

## Step 6: Run Specific Benchmark (Optional)

To run only one benchmark category:

```bash
# Simple scenarios only
dotnet run --configuration Release -- --filter "*SimpleProvider*"

# Complex scenarios only
dotnet run --configuration Release -- --filter "*ComplexGraph*"

# Caching scenarios only
dotnet run --configuration Release -- --filter "*Caching*"

# Re-evaluation after changes and memory overhead (spec 006 T031-T033)
dotnet run --configuration Release -- --filter "*IncrementalUpdate*" "*CollectionChange*" "*MemoryOverhead*"

# Quick, in-process run (fewer iterations, wider error margins)
dotnet run --configuration Release -- --filter "*" --job short --inProcess
```

**Use Case**: Targeted measurement after code changes

---

## Step 7: Compare Against Baseline (Future)

Once baseline is established and stored in git:

The committed baseline is the set of reports in `benchmarks/reports/results/`. Compare a new
run against them by hand (same machine and job where possible). There is no comparison script;
automated regression detection is not planned.

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

### "No benchmarks found" / nothing runs
**Cause**: the filter matched nothing, or BenchmarkDotNet refused a non-optimized (Debug) build
**Fix**: Run with `--configuration Release` and check the `--filter` pattern (`--list flat` lists all benchmarks)

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
**Fix**: Pass `--job short` (or `--job dry` just to check that they run) and a narrower `--filter` (trade-off: less statistical confidence)

---

## Next Steps

After establishing baseline:
1. Store results in git (`benchmarks/reports/results/`)
2. Configure CI to run benchmarks on every commit
3. Set up automated regression detection
4. Add new scenarios as framework evolves
5. Use benchmarks to validate optimization efforts

---

## Success Criteria

This quickstart is successful when:
- [ ] All benchmarks execute without errors
- [ ] Standard deviation well below 10% of the mean (default job, idle machine)
- [ ] Results exported to HTML/CSV/Markdown formats in `benchmarks/reports/results/`
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
