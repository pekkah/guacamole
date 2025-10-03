using Guacamole.Core.Providers;

namespace Guacamole.Benchmarks.Infrastructure.TestScenarios;

/// <summary>
/// Builds a simple linear computation graph with 5 nodes.
/// Structure: Input → Compute1 → Compute2 → Compute3 → Output
/// </summary>
public static class SimpleScenario
{
    public static (IncrementalValueProvider<int> Provider, BenchmarkScenario Scenario) Build()
    {
        var (provider, nodeCount, maxDepth) = SimpleBenchmarkGraph.BuildSimple(inputValue: 1);

        var scenario = new BenchmarkScenario(
            Name: "Simple_5Node_Linear",
            NodeCount: nodeCount,
            DepthLevels: maxDepth,
            GraphStructure: GraphStructure.Linear,
            Description: "Linear computation graph with 5 nodes for baseline measurements"
        );

        return (provider, scenario);
    }
}
