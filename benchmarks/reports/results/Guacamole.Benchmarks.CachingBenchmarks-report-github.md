```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.2 LTS (Noble Numbat) WSL
AMD Ryzen 9 7900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100-rc.1.25451.107
  [Host]     : .NET 10.0.0 (10.0.25.45207), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.0 (10.0.25.45207), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                       | Mean     | Error   | StdDev  | Gen0   | Allocated |
|----------------------------- |---------:|--------:|--------:|-------:|----------:|
| CompareCachedVsUncached_Cold | 658.9 ns | 4.83 ns | 4.28 ns | 0.0563 |     952 B |
| CompareCachedVsUncached_Warm | 195.5 ns | 1.65 ns | 1.54 ns | 0.0291 |     488 B |
