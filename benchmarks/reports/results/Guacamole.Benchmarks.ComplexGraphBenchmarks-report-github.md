```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.2 LTS (Noble Numbat) WSL
AMD Ryzen 9 7900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100-rc.1.25451.107
  [Host]     : .NET 10.0.0 (10.0.25.45207), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.0 (10.0.25.45207), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error   | StdDev  | Gen0   | Allocated |
|-------------------------------- |---------:|--------:|--------:|-------:|----------:|
| MeasureComplexScenarioExecution | 191.6 ns | 2.27 ns | 2.12 ns | 0.0291 |     488 B |
