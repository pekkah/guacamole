using System;
using System.Collections.Immutable;
using System.Linq;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Nodes;
using Guacamole.Core.Pipeline;
using Guacamole;
using Xunit;

namespace Guacamole.Core.Tests.Providers.Extensions;

/// <summary>
/// T014: IncrementalValuesProvider extension methods tests (SelectMany, Collect)
/// </summary>
public class ValuesProviderExtensionTests
{
    private class TestCollectionNode : CollectionComputationNodeBase<int>
    {
        private readonly int[] _values;

        public TestCollectionNode(string id, params int[] values) : base(id)
        {
            _values = values;
        }

        public override Result<IEnumerable<int>, ComputationError> Compute(ComputationContext context)
        {
            return Result<IEnumerable<int>, ComputationError>.Success(_values);
        }
    }

    [Fact]
    public void SelectMany_ShouldFlatten_Collections()
    {
        // Arrange
        var graph = new ComputationGraph();
        var sourceNode = new TestCollectionNode("source", 1, 2, 3);
        graph = graph.AddNode(sourceNode);

        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act - transform each value to multiple values
        var flattenedProvider = provider.SelectMany(x => new[] { x, x * 10 });
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = flattenedProvider.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        var values = result.Value!.ToArray();
        Assert.Equal(new[] { 1, 10, 2, 20, 3, 30 }, values);
    }

    [Fact]
    public void Collect_ShouldGatherAllValues()
    {
        // Arrange
        var graph = new ComputationGraph();
        var sourceNode = new TestCollectionNode("source", 1, 2, 3, 4, 5);
        graph = graph.AddNode(sourceNode);

        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act
        var collectedProvider = provider.Collect();
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = collectedProvider.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result.Value!.ToArray());
    }

    [Fact]
    public void Select_ShouldTransform_EachValue()
    {
        // Arrange
        var graph = new ComputationGraph();
        var sourceNode = new TestCollectionNode("source", 1, 2, 3);
        graph = graph.AddNode(sourceNode);

        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act
        var transformedProvider = provider.Select(x => x * 2);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = transformedProvider.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 2, 4, 6 }, result.Value!.ToArray());
    }

    [Fact]
    public void Where_ShouldFilter_Values()
    {
        // Arrange
        var graph = new ComputationGraph();
        var sourceNode = new TestCollectionNode("source", 1, 2, 3, 4, 5);
        graph = graph.AddNode(sourceNode);

        var provider = new IncrementalValuesProvider<int>(sourceNode, graph, default);

        // Act
        var filteredProvider = provider.Where(x => x % 2 == 0);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = filteredProvider.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 2, 4 }, result.Value!.ToArray());
    }
}
