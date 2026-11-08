# Benchmark baseline (October 2025)

A curated copy of the first committed BenchmarkDotNet reports (spec 009 T025/T030), kept for
reference. They were produced with the default job before the .NET 11 upgrade and before the
equality cut-off, per-item memoization and node trimming changes, so they are not comparable with
current runs. Current results and their interpretation are in the
[Performance Guide](../performance-guide.md#benchmark-results).

Local runs write their reports to `BenchmarkDotNet.Artifacts/`, which is not tracked; see
[Benchmarking](../performance-guide.md#benchmarking).

```
BenchmarkDotNet v0.14.0, Ubuntu 24.04.2 LTS (Noble Numbat) WSL
AMD Ryzen 9 7900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100-rc.1.25451.107
  [Host]     : .NET 10.0.0 (10.0.25.45207), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.0 (10.0.25.45207), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```

## SimpleProviderBenchmarks

| Method                        | Mean     | Error   | StdDev  | Gen0   | Allocated |
|------------------------------ |---------:|--------:|--------:|-------:|----------:|
| MeasureBasicProviderExecution | 189.1 ns | 1.84 ns | 1.72 ns | 0.0291 |     488 B |

## ComplexGraphBenchmarks

| Method                          | Mean     | Error   | StdDev  | Gen0   | Allocated |
|-------------------------------- |---------:|--------:|--------:|-------:|----------:|
| MeasureComplexScenarioExecution | 191.6 ns | 2.27 ns | 2.12 ns | 0.0291 |     488 B |

## CachingBenchmarks

| Method                       | Mean     | Error   | StdDev  | Gen0   | Allocated |
|----------------------------- |---------:|--------:|--------:|-------:|----------:|
| CompareCachedVsUncached_Cold | 658.9 ns | 4.83 ns | 4.28 ns | 0.0563 |     952 B |
| CompareCachedVsUncached_Warm | 195.5 ns | 1.65 ns | 1.54 ns | 0.0291 |     488 B |
