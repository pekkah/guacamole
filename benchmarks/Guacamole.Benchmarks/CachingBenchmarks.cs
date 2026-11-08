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
    private Infrastructure.BenchmarkContext? _context;
    private IncrementalValuesProvider<int> _values;

    [GlobalSetup]
    public void Setup()
    {
        (_provider, _scenario) = SimpleScenario.Build();

        // Warm up the cache by running once
        _ = _provider.GetValue();

        // A collection stage over 100 items, for the warm collection read
        _context = new Infrastructure.BenchmarkContext();
        var source = _context.AddSource<int>("items");
        source.PushRange(Enumerable.Range(0, 100));
        _values = source.AsProvider().Select(static x => x * 2);
        _ = _values.Values;
    }

    [GlobalCleanup]
    public void Cleanup() => _context?.Dispose();

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

    [Benchmark]
    public object CompareCachedVsUncached_WarmValues()
    {
        // A collection read with populated cache: returns the cached values (a read-only view)
        return _values.Values;
    }
}
