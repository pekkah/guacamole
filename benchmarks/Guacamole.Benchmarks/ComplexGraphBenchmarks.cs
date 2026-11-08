using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;
using Guacamole;

namespace Guacamole.Benchmarks;

/// <summary>
/// Benchmarks for complex computation scenarios with ≥20 nodes and ≥5 dependency levels.
/// Validates O(changes) complexity characteristics.
/// </summary>
[MemoryDiagnoser]
public class ComplexGraphBenchmarks
{
    private IncrementalValueProvider<int> _provider;
    private Infrastructure.BenchmarkScenario? _scenario;

    [GlobalSetup]
    public void Setup()
    {
        (_provider, _scenario) = ComplexScenario.Build();
    }

    [Benchmark]
    public void MeasureComplexScenarioExecution()
    {
        // Execute the provider chain
        var result = _provider.GetValue();
        _ = result.ValueOrDefault;
    }
}
