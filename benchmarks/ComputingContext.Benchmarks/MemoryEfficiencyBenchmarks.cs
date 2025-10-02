using BenchmarkDotNet.Attributes;

namespace ComputingContext.Benchmarks;

/// <summary>
/// Benchmarks to validate <2x memory overhead requirement.
/// Measures total memory usage compared to baseline data storage.
/// </summary>
[MemoryDiagnoser]
public class MemoryEfficiencyBenchmarks
{
    [Params(100, 1000, 10000)]
    public int ItemCount { get; set; }

    [Benchmark(Baseline = true)]
    public void BaselineMemory()
    {
        // Baseline: raw List<T> storage
        throw new NotImplementedException("Awaiting implementation");
    }

    [Benchmark]
    public void ContextWithTracking()
    {
        // Test: context + change tracking should be <2x baseline
        throw new NotImplementedException("Awaiting implementation");
    }
}