using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;
using Guacamole;

namespace Guacamole.Benchmarks;

/// <summary>
/// Benchmarks for basic provider operations with simple linear computation graph.
/// Establishes baseline performance measurements.
/// </summary>
[MemoryDiagnoser]
public class SimpleProviderBenchmarks
{
    private IncrementalValueProvider<int> _provider;
    private Infrastructure.BenchmarkScenario? _scenario;

    [GlobalSetup]
    public void Setup()
    {
        (_provider, _scenario) = SimpleScenario.Build();
    }

    [Benchmark]
    public void MeasureBasicProviderExecution()
    {
        // Execute the provider chain
        var result = _provider.GetValue();
        _ = result.Value;
    }
}
