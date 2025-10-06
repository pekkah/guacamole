using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;
using Guacamole;

namespace Guacamole.Benchmarks;

/// <summary>
/// Benchmarks comparing cached vs uncached query execution.
/// Validates caching performance impact.
/// </summary>
[MemoryDiagnoser]
public class CachingBenchmarks
{
    private IncrementalValueProvider<int> _provider;
    private Infrastructure.BenchmarkScenario? _scenario;

    [GlobalSetup]
    public void Setup()
    {
        (_provider, _scenario) = SimpleScenario.Build();

        // Warm up the cache by running once
        _ = _provider.GetValue();
    }

    [Benchmark]
    public void CompareCachedVsUncached_Cold()
    {
        // Clear cache for cold execution
        _provider.Graph.ValueCache.Clear();

        // Execute with empty cache
        var result = _provider.GetValue();
        _ = result.Value;
    }

    [Benchmark]
    public void CompareCachedVsUncached_Warm()
    {
        // Execute with populated cache (from Setup and previous warm executions)
        var result = _provider.GetValue();
        _ = result.Value;
    }
}
