using System.Collections.Immutable;
using Guacamole.Pipeline;
using Guacamole.Tests.Common;
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
    /// EDGE (#76): the cycle check skips the walk when no node depends on the new ID yet, so it must
    /// still catch every cycle through a node whose ID other nodes already declare.
    /// </summary>
    [Fact]
    public void ComputationGraph_ShouldDetect_CyclesThroughNodesDeclaredBeforeTheyAreAdded()
    {
        var graph = new ComputationGraph();

        // A node depending on itself
        Assert.Throws<InvalidOperationException>(() =>
            graph.AddNode(new TestComputationNode("self", typeof(int), dependencies: ["self"])));

        // A longer cycle, closed by the last node: c -> b -> a -> c
        var withChain = graph
            .AddNode(new TestComputationNode("a", typeof(int), dependencies: ["c"]))
            .AddNode(new TestComputationNode("b", typeof(int), dependencies: ["a"]));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            withChain.AddNode(new TestComputationNode("c", typeof(int), dependencies: ["b"])));
        Assert.Contains("'c'", exception.Message);

        // Through one of several dependencies, and a branch
        var branched = graph
            .AddNode(new TestComputationNode("root", typeof(int)))
            .AddNode(new TestComputationNode("left", typeof(int), dependencies: ["root", "late"]))
            .AddNode(new TestComputationNode("right", typeof(int), dependencies: ["root"]))
            .AddNode(new TestComputationNode("join", typeof(int), dependencies: ["left", "right"]));
        Assert.Throws<InvalidOperationException>(() =>
            branched.AddNode(new TestComputationNode("late", typeof(int), dependencies: ["root", "join"])));

        // A node others depend on that closes no cycle is accepted, and indexed in reverse
        var accepted = branched.AddNode(new TestComputationNode("late", typeof(int), dependencies: ["root"]));
        Assert.Equal(5, accepted.Nodes.Count);
        Assert.Equal(new[] { "left" }, accepted.Dependents["late"]);
        Assert.Equal(new[] { "join" }, accepted.Dependents["right"]);
        Assert.Equal(new[] { "late", "left", "right" }, accepted.Dependents["root"].Order(StringComparer.Ordinal));
        Assert.Equal(6, accepted.DependencyCount);
    }

    /// <summary>
    /// PERFORMANCE (#76): building a chain (each node depending on the previous one) costs the same per
    /// node at 100 and 3,200 nodes, whether the nodes are added after or before their dependencies;
    /// the cycle check walked everything upstream of each new node, so building it was quadratic.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputationGraph_BuildingAChain_IsNotQuadratic(bool dependentsFirst)
    {
        ScalingAssert.PerOperationTimeScales(
            size =>
            {
                var nodes = Enumerable.Range(0, size)
                    .Select(i => dependentsFirst
                        ? new TestComputationNode($"chain-{i}", typeof(int), i == size - 1 ? null : [$"chain-{i + 1}"])
                        : new TestComputationNode($"chain-{i}", typeof(int), i == 0 ? null : [$"chain-{i - 1}"]))
                    .ToArray();
                return (() =>
                {
                    var built = new ComputationGraph();
                    foreach (var node in nodes)
                        built = built.AddNode(node);
                    Assert.Equal(size - 1, built.DependencyCount);
                }, nodes.Length);
            },
            smallSize: 100,
            largeSize: 3_200,
            maxRatio: 8,
            dependentsFirst ? "AddNode of a chain, dependents first" : "AddNode of a chain");
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
    /// Invalidation follows the edges recorded as stages read their inputs, so it reaches computed
    /// runtime stages (which are not in <see cref="ComputationGraph.Dependencies"/>), and only them.
    /// </summary>
    [Fact]
    public void InvalidateCacheForNode_RemovesTheValuesOfTheComputedStagesDownstream()
    {
        var graph = new ComputationGraph();
        var source = new IncrementalValueSource<int>("invalidate-source", () => graph);
        var other = new IncrementalValueSource<int>("invalidate-other", () => graph);
        graph = graph.AddNode(source.GetNode()).AddNode(other.GetNode());
        source.Set(1);
        other.Set(10);
        var first = source.AsProvider().Select(x => x + 1);
        var second = first.Select(x => x * 2);
        var unrelated = other.AsProvider().Select(x => x - 1);
        Assert.Equal(4, second.Value);
        Assert.Equal(9, unrelated.Value);
        Assert.True(graph.ValueCache.ContainsKey(first.Node.Id));

        graph.InvalidateCacheForNode(source.GetNode().Id);

        Assert.False(graph.ValueCache.ContainsKey(first.Node.Id));
        Assert.False(graph.ValueCache.ContainsKey(second.Node.Id));
        Assert.True(graph.ValueCache.ContainsKey(unrelated.Node.Id));
        Assert.Equal(4, second.Value);
    }

    /// <summary>
    /// PERFORMANCE (#71): invalidating a node costs the same whether the rest of the graph has 100 or
    /// 3,200 nodes (it scanned the dependencies of every node, about 32x).
    /// </summary>
    [Fact]
    public void InvalidateCacheForNode_DoesNotScanTheRestOfTheGraph()
    {
        const int Operations = 200;
        ScalingAssert.PerOperationTimeScales(
            size =>
            {
                var graph = new ComputationGraph();
                var target = new IncrementalValueSource<int>("target", () => graph);
                var other = new IncrementalValueSource<int>("other", () => graph);
                graph = graph.AddNode(target.GetNode()).AddNode(other.GetNode());
                for (var i = 0; i < size; i++)
                    graph = graph.AddNode(new TestComputationNode($"static-{i}", typeof(int), ["other"]));

                target.Set(1);
                var stage = target.AsProvider().Select(x => x + 1);
                var otherProvider = other.AsProvider();
                for (var i = 0; i < size; i++)
                {
                    var offset = i;
                    _ = otherProvider.Select(x => x + offset);
                }

                Assert.Equal(2, stage.Value);
                return (() =>
                {
                    for (var i = 0; i < Operations; i++)
                        graph.InvalidateCacheForNode("target");
                }, Operations);
            },
            smallSize: 100,
            largeSize: 3_200,
            maxRatio: 8,
            "ComputationGraph.InvalidateCacheForNode");
    }

    /// <summary>
    /// PERFORMANCE: Test that graph operations are efficient: the time per AddNode does not grow
    /// with the size of the graph beyond the logarithmic cost of its immutable maps (compared at 100
    /// and 3,200 nodes; a cycle check or copy that is linear in the graph would be about 32x).
    /// </summary>
    [Fact]
    public void ComputationGraph_Operations_ShouldBe_Efficient()
    {
        ScalingAssert.PerOperationTimeScales(
            size =>
            {
                var nodes = Enumerable.Range(1, size)
                    .Select(i => new TestComputationNode($"node-{i}", typeof(int)))
                    .ToArray();
                return (() =>
                {
                    var built = new ComputationGraph();
                    foreach (var node in nodes)
                        built = built.AddNode(node);
                }, nodes.Length);
            },
            smallSize: 100,
            largeSize: 3_200,
            maxRatio: 8,
            "ComputationGraph.AddNode");

        // And the nodes are added
        var updatedGraph = new ComputationGraph();
        foreach (var node in Enumerable.Range(1, 100).Select(i => new TestComputationNode($"node-{i}", typeof(int))))
            updatedGraph = updatedGraph.AddNode(node);

        Assert.Equal(100, updatedGraph.Nodes.Count);
    }

    /// <summary>
    /// PERFORMANCE: Test that cache operations are O(1): caching and reading a value costs the same
    /// whether the cache holds 1,000 or 32,000 values (O(n) would be about 32x).
    /// </summary>
    [Fact]
    public void ComputationGraph_CacheOperations_ShouldBe_O1()
    {
        const int Operations = 1_000;
        ScalingAssert.PerOperationTimeScales(
            size =>
            {
                var cacheGraph = new ComputationGraph(cacheCapacity: size);
                var keys = Enumerable.Range(0, size).Select(i => $"node-{i}").ToArray();
                foreach (var key in keys)
                    cacheGraph.CacheValue(key, key);

                // Overwrite and read back values spread over the whole cache
                var touched = Enumerable.Range(0, Operations).Select(i => keys[(int)((long)i * size / Operations)]).ToArray();
                return (() =>
                {
                    foreach (var key in touched)
                    {
                        cacheGraph.CacheValue(key, key);
                        cacheGraph.ValueCache.TryGet(key, out _);
                    }
                }, Operations);
            },
            smallSize: 1_000,
            largeSize: 32_000,
            maxRatio: 8,
            "ComputationGraph cache set and get");

        // And the value is cached
        var graph = new ComputationGraph();
        var graphWithNode = graph.AddNode(new TestComputationNode("perf-test", typeof(string)));
        var cachedGraph = graphWithNode.CacheValue("perf-test", "cached-value");
        var hasValue = cachedGraph.ValueCache.TryGet("perf-test", out var value);

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
