using System.Collections.Immutable;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Pipeline;

/// <summary>
/// Tests for ComputationGraph basic functionality following TDD progression:
/// Basic → Edge → Incremental → Performance
/// </summary>
public class ComputationGraphTests
{
    /// <summary>
    /// BASIC: Test that ComputationGraph can be created with immutable design
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldBe_Immutable()
    {
        // Act - This will fail until ComputationGraph is implemented
        var graph = new ComputationGraph();

        // Assert
        Assert.NotNull(graph);
        Assert.NotNull(graph.Nodes);
        Assert.NotNull(graph.Dependencies);
        Assert.NotNull(graph.ValueCache);
    }

    /// <summary>
    /// BASIC: Test that empty graph has expected initial state
    /// </summary>
    [Fact]
    public void ComputationGraph_Empty_ShouldHave_InitialState()
    {
        // Act
        var graph = new ComputationGraph();

        // Assert
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Dependencies);
        Assert.Equal(0, graph.ValueCache.CurrentSize);
    }

    /// <summary>
    /// BASIC: Test that graph can add computation nodes
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldAdd_ComputationNodes()
    {
        // Arrange
        var graph = new ComputationGraph();
        var node = new TestComputationNode("test-node", typeof(string));

        // Act
        var updatedGraph = graph.AddNode(node);

        // Assert - Immutable with copy-on-write semantics
        Assert.NotSame(graph, updatedGraph);
        Assert.Empty(graph.Nodes); // Original unchanged
        Assert.Single(updatedGraph.Nodes);
        Assert.True(updatedGraph.Nodes.ContainsKey("test-node"));
    }

    /// <summary>
    /// BASIC: Test that graph can track dependencies between nodes
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldTrack_Dependencies()
    {
        // Arrange
        var graph = new ComputationGraph();
        var nodeA = new TestComputationNode("node-a", typeof(int));
        var nodeB = new TestComputationNode("node-b", typeof(string), dependencies: ["node-a"]);

        // Act
        var updatedGraph = graph
            .AddNode(nodeA)
            .AddNode(nodeB);

        // Assert
        Assert.Contains("node-a", updatedGraph.Dependencies.Keys);
        Assert.Contains("node-b", updatedGraph.Dependencies.Keys);

        // node-b should depend on node-a
        if (updatedGraph.Dependencies.TryGetValue("node-b", out var deps))
        {
            Assert.Contains("node-a", deps);
        }
        else
        {
            Assert.Fail("node-b dependencies not found");
        }
    }

    /// <summary>
    /// EDGE: Test that graph validates NodeId uniqueness
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldValidate_UniqueNodeIds()
    {
        // Arrange
        var graph = new ComputationGraph();
        var node1 = new TestComputationNode("duplicate", typeof(int));
        var node2 = new TestComputationNode("duplicate", typeof(string));

        // Act & Assert
        var graphWithFirst = graph.AddNode(node1);

        // Adding second node with same ID should throw
        Assert.Throws<InvalidOperationException>(() =>
            graphWithFirst.AddNode(node2));
    }

    /// <summary>
    /// EDGE: Test that graph detects circular dependencies at build time
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldDetect_CircularDependencies()
    {
        // Arrange
        var graph = new ComputationGraph();
        var nodeA = new TestComputationNode("node-a", typeof(int), dependencies: ["node-b"]);
        var nodeB = new TestComputationNode("node-b", typeof(string), dependencies: ["node-a"]);

        // Act & Assert - Build-time cycle detection
        var graphWithA = graph.AddNode(nodeA);

        Assert.Throws<InvalidOperationException>(() =>
            graphWithA.AddNode(nodeB));
    }

    /// <summary>
    /// EDGE: Test graph allows nodes whose dependencies are not registered yet
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldHandle_MissingDependencies()
    {
        // Arrange
        var graph = new ComputationGraph();
        var nodeWithMissingDep = new TestComputationNode("dependent", typeof(int),
            dependencies: ["missing-node"]);

        // Act - Should allow adding node with missing dependencies
        var updatedGraph = graph.AddNode(nodeWithMissingDep);

        // Assert - Node should be added
        Assert.Single(updatedGraph.Nodes);
        Assert.Contains("dependent", updatedGraph.Nodes.Keys);
    }

    /// <summary>
    /// INCREMENTAL: Test that graph maintains DAG invariant
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldMaintain_DAGInvariant()
    {
        // Arrange
        var graph = new ComputationGraph();
        var nodeA = new TestComputationNode("a", typeof(int));
        var nodeB = new TestComputationNode("b", typeof(string), dependencies: ["a"]);
        var nodeC = new TestComputationNode("c", typeof(bool), dependencies: ["b"]);

        // Act - Create valid DAG
        var updatedGraph = graph
            .AddNode(nodeA)
            .AddNode(nodeB)
            .AddNode(nodeC);

        // Assert - All nodes of the acyclic chain are accepted
        Assert.Equal(3, updatedGraph.Nodes.Count);
    }

    /// <summary>
    /// INCREMENTAL: Test that cache invalidation works with dependency changes
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldInvalidate_AffectedCachedValues()
    {
        // Arrange
        var graph = new ComputationGraph();
        var nodeA = new TestComputationNode("cache-a", typeof(int));
        var nodeB = new TestComputationNode("cache-b", typeof(string), dependencies: ["cache-a"]);

        var graphWithNodes = graph.AddNode(nodeA).AddNode(nodeB);

        // Cache some values
        var graphWithCache = graphWithNodes.CacheValue("cache-a", 42);
        graphWithCache = graphWithCache.CacheValue("cache-b", "cached");

        // Act - Update nodeA (should invalidate cache-b)
        var updatedNodeA = new TestComputationNode("cache-a", typeof(int));
        var graphAfterUpdate = graphWithCache.UpdateNode(updatedNodeA);

        // Assert - Dependent cached values should be invalidated
        Assert.False(graphAfterUpdate.ValueCache.ContainsKey("cache-b"),
            "Dependent cache should be invalidated");
    }

    /// <summary>
    /// PERFORMANCE: Test that graph operations are efficient
    /// </summary>
    [Fact]
    public void ComputationGraph_Operations_ShouldBe_Efficient()
    {
        // Arrange
        var graph = new ComputationGraph();
        var nodes = Enumerable.Range(1, 100)
            .Select(i => new TestComputationNode($"node-{i}", typeof(int)))
            .ToArray();

        // Act - Measure bulk operations
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var updatedGraph = graph;
        foreach (var node in nodes)
        {
            updatedGraph = updatedGraph.AddNode(node);
        }

        stopwatch.Stop();

        // Assert - Should handle 100 nodes quickly (allowing CI overhead)
        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"Adding 100 nodes took {stopwatch.ElapsedMilliseconds}ms, expected <100ms");
        Assert.Equal(100, updatedGraph.Nodes.Count);
    }

    /// <summary>
    /// PERFORMANCE: Test that cache operations are O(1)
    /// </summary>
    [Fact]
    public void ComputationGraph_CacheOperations_ShouldBe_O1()
    {
        // Arrange
        var graph = new ComputationGraph();
        var node = new TestComputationNode("perf-test", typeof(string));
        var graphWithNode = graph.AddNode(node);

        // Warm up so the measurement covers the operations, not JIT compilation (which can take
        // longer than the budget while other tests run in parallel)
        new ComputationGraph().AddNode(new TestComputationNode("warm-up", typeof(string)))
            .CacheValue("warm-up", "value").ValueCache.TryGet("warm-up", out _);

        // Act - Measure cache operations
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var cachedGraph = graphWithNode.CacheValue("perf-test", "cached-value");
        var hasValue = cachedGraph.ValueCache.TryGet("perf-test", out var value);

        stopwatch.Stop();

        // Assert - Should be very fast (allowing CI overhead)
        Assert.True(stopwatch.ElapsedMilliseconds < 10,
            "Cache operations should be fast (< 10ms)");
        Assert.True(hasValue);
        Assert.Equal("cached-value", value);
    }
}

/// <summary>
/// Test implementation of IComputationNode
/// </summary>
internal class TestComputationNode : IComputationNode
{
    public TestComputationNode(string id, Type outputType, IEnumerable<string>? dependencies = null)
    {
        Id = id;
        OutputType = outputType;
        Dependencies = (dependencies ?? Enumerable.Empty<string>()).ToImmutableHashSet();
    }

    public string Id { get; }
    public Type OutputType { get; }
    public ImmutableHashSet<string> Dependencies { get; }
}

/// <summary>
/// Extension methods for testing ComputationGraph
/// </summary>
internal static class ComputationGraphExtensions
{
    public static ComputationGraph CacheValue(this ComputationGraph graph, string nodeId, object value)
    {
        // This will need to be implemented when ComputationGraph is created
        return graph.WithCachedValue(nodeId, value);
    }

    public static ComputationGraph UpdateNode(this ComputationGraph graph, IComputationNode node)
    {
        // This will need to be implemented when ComputationGraph is created
        return graph.ReplaceNode(node);
    }
}
