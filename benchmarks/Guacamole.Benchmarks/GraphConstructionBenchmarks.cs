using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Guacamole.Pipeline;

namespace Guacamole.Benchmarks;

/// <summary>
/// Building a graph node by node with <c>ComputationGraph.AddNode</c> (issue #76). Each node of
/// the chain depends on the previous one, so a cycle check that walks the graph from every new node
/// makes building the chain quadratic; the time per added node should stay roughly constant.
/// </summary>
[MemoryDiagnoser]
public class GraphConstructionBenchmarks
{
    private ChainNode[] _nodes = null!;

    /// <summary>Length of the chain.</summary>
    [Params(1000, 4000)]
    public int Nodes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _nodes = new ChainNode[Nodes];
        for (var i = 0; i < Nodes; i++)
            _nodes[i] = new ChainNode($"chain-{i}", i == 0 ? [] : [$"chain-{i - 1}"]);
    }

    /// <summary>Adds the chain to an empty graph, each node after the node it depends on.</summary>
    [Benchmark]
    public int BuildChain()
    {
        var graph = new ComputationGraph();
        foreach (var node in _nodes)
            graph = graph.AddNode(node);

        return graph.Nodes.Count;
    }

    private sealed class ChainNode(string id, string[] dependencies) : IComputationNode
    {
        public string Id { get; } = id;

        public Type OutputType => typeof(int);

        public ImmutableHashSet<string> Dependencies { get; } = [.. dependencies];
    }
}
