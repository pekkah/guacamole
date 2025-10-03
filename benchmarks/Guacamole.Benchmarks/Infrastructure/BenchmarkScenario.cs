namespace Guacamole.Benchmarks.Infrastructure;

/// <summary>
/// Represents a specific benchmark test case configuration.
/// </summary>
/// <param name="Name">Scenario identifier (e.g., "Simple_5Node_Linear", "Complex_21Node_Tree")</param>
/// <param name="NodeCount">Number of computation nodes in the graph</param>
/// <param name="DepthLevels">Maximum dependency chain depth</param>
/// <param name="GraphStructure">Topology type (Linear, Tree, DAG)</param>
/// <param name="Description">Human-readable scenario summary</param>
public record BenchmarkScenario(
    string Name,
    int NodeCount,
    int DepthLevels,
    GraphStructure GraphStructure,
    string Description)
{
    /// <summary>
    /// Indicates if this is a complex scenario (NodeCount ≥ 20 AND DepthLevels ≥ 5)
    /// </summary>
    public bool IsComplex => NodeCount >= 20 && DepthLevels >= 5;
}

/// <summary>
/// Graph topology types for benchmarks
/// </summary>
public enum GraphStructure
{
    Linear,
    Tree,
    DAG
}
