using Guacamole.Core.Pipeline;
using Guacamole.Core.Providers;
using Guacamole.Core.Providers.Extensions;
using Guacamole.Core.Common;
using Guacamole.Core.Nodes;
using Guacamole.Core.Diagnostics;
using System.Collections.Immutable;

namespace Guacamole.Benchmarks.Infrastructure;

/// <summary>
/// Helper to build simple benchmark computation graphs using providers.
/// </summary>
public static class SimpleBenchmarkGraph
{
    /// <summary>
    /// Creates a simple 5-node linear provider chain for benchmarking.
    /// Input -> Compute1 -> Compute2 -> Compute3 -> Output
    /// </summary>
    public static (IncrementalValueProvider<int> Provider, int NodeCount, int MaxDepth) BuildSimple(int inputValue)
    {
        var graph = new ComputationGraph();

        // Input node
        var inputNode = new ConstantNode("Input", inputValue);
        graph = graph.AddNode(inputNode);
        var input = new IncrementalValueProvider<int>(inputNode, graph, default);

        // Chain transformations
        var compute1 = input.Select(static x => x * 2);
        var compute2 = compute1.Select(static x => x * 2);
        var compute3 = compute2.Select(static x => x * 2);
        var output = compute3.Select(static x => x + 1);

        return (output, NodeCount: 5, MaxDepth: 4);
    }

    /// <summary>
    /// Creates a complex 21-node tree for benchmarking.
    /// </summary>
    public static (IncrementalValueProvider<int> Provider, int NodeCount, int MaxDepth) BuildComplex()
    {
        var graph = new ComputationGraph();

        // Create 5 input nodes
        var inputs = new List<IncrementalValueProvider<int>>();
        for (int i = 0; i < 5; i++)
        {
            var inputNode = new ConstantNode($"Root{i}", i);
            graph = graph.AddNode(inputNode);
            inputs.Add(new IncrementalValueProvider<int>(inputNode, graph, default));
        }

        // Layer 1: 5 nodes
        var l1 = inputs.Select(p => p.Select(x => x * 2)).ToList();

        // Layer 2: 5 nodes (combine pairs)
        var l2 = new List<IncrementalValueProvider<int>>();
        for (int i = 0; i < 5; i++)
        {
            var combined = l1[i].Combine(l1[(i + 1) % 5]).Select(tuple => tuple.Item1 + tuple.Item2);
            l2.Add(combined);
        }

        // Layer 3: 3 nodes
        var l3_0 = l2[0].Combine(l2[1]).Select(tuple => tuple.Item1 + tuple.Item2);
        var l3_1 = l2[1].Combine(l2[2]).Select(tuple => tuple.Item1 + tuple.Item2);
        var l3_2 = l2[2].Combine(l2[3]).Select(tuple => tuple.Item1 + tuple.Item2);

        // Layer 4: 2 nodes
        var l4_0 = l3_0.Combine(l3_1).Select(tuple => tuple.Item1 + tuple.Item2);
        var l4_1 = l3_1.Combine(l3_2).Select(tuple => tuple.Item1 + tuple.Item2);

        // Layer 5: Output
        var output = l4_0.Combine(l4_1).Select(tuple => tuple.Item1 + tuple.Item2);

        return (output, NodeCount: 21, MaxDepth: 5);
    }
}

/// <summary>
/// Simple constant value node for benchmarks.
/// </summary>
internal class ConstantNode : ComputationNodeBase<int>
{
    private readonly int _value;

    public ConstantNode(string id, int value)
        : base(id, ImmutableHashSet<string>.Empty)
    {
        _value = value;
    }

    public override Result<int, ComputationError> Compute(ComputationContext context)
    {
        return Result<int, ComputationError>.Success(_value);
    }
}
