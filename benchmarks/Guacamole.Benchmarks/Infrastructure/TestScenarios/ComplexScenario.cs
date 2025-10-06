using Guacamole;

namespace Guacamole.Benchmarks.Infrastructure.TestScenarios;

/// <summary>
/// Builds a complex tree-structured computation graph with 21 nodes and 5 dependency levels.
/// Structure:
/// Root (5 inputs)
/// ├─ L1: 5 nodes (1 dependency each)
/// ├─ L2: 5 nodes (2 dependencies each)
/// ├─ L3: 3 nodes (3 dependencies each)
/// ├─ L4: 2 nodes (cross-dependencies)
/// └─ L5: 1 output (aggregation)
/// Total: 21 nodes, 5 levels deep
/// </summary>
public static class ComplexScenario
{
    public static (IncrementalValueProvider<int> Provider, BenchmarkScenario Scenario) Build()
    {
        var (provider, nodeCount, maxDepth) = SimpleBenchmarkGraph.BuildComplex();

        var scenario = new BenchmarkScenario(
            Name: "Complex_21Node_Tree",
            NodeCount: nodeCount,
            DepthLevels: maxDepth,
            GraphStructure: GraphStructure.Tree,
            Description: "Tree-structured graph with 21 nodes across 5 dependency levels"
        );

        return (provider, scenario);
    }
}
