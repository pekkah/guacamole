using System;
using System.Collections.Immutable;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Providers.Extensions;

/// <summary>
/// T013: IncrementalValueProvider extension methods tests (Select, Where, Combine)
/// </summary>
public class ValueProviderExtensionTests
{
    private class TestValueNode : ComputationNodeBase<int>
    {
        private readonly int _value;

        public TestValueNode(string id, int value) : base(id)
        {
            _value = value;
        }

        public override Result<int, ComputationError> Compute(ComputationContext context)
        {
            return Result<int, ComputationError>.Success(_value);
        }
    }

    [Fact]
    public void Select_ShouldTransform_SingleValue()
    {
        // Arrange
        var graph = new ComputationGraph();
        var sourceNode = new TestValueNode("source", 42);
        graph = graph.AddNode(sourceNode);

        var provider = new IncrementalValueProvider<int>(sourceNode, graph, default);

        // Act
        var transformedProvider = provider.Select(x => x * 2);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = transformedProvider.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(84, result.ValueOrDefault);
    }

    [Fact]
    public void Where_ShouldFilter_BasedOnPredicate()
    {
        // Arrange
        var graph = new ComputationGraph();
        var sourceNode = new TestValueNode("source", 42);
        graph = graph.AddNode(sourceNode);

        var provider = new IncrementalValueProvider<int>(sourceNode, graph, default);

        // Act - predicate returns true
        var filteredProvider = provider.Where(x => x > 40);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = filteredProvider.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.ValueOrDefault);
    }

    [Fact]
    public void Where_ShouldProduceNoValue_WhenPredicateRejects()
    {
        // Arrange
        var graph = new ComputationGraph();
        var sourceNode = new TestValueNode("source", 42);
        graph = graph.AddNode(sourceNode);

        var provider = new IncrementalValueProvider<int>(sourceNode, graph, default);

        // Act - predicate returns false
        var filteredProvider = provider.Where(x => x < 40);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = filteredProvider.Node.Compute(context);

        // Assert - a rejected value is "no value", not an error
        Assert.True(result.IsNone);
        Assert.False(result.IsError);
    }

    [Fact]
    public void Combine_ShouldMergeTwoProviders()
    {
        // Arrange
        var graph = new ComputationGraph();
        var firstNode = new TestValueNode("first", 10);
        var secondNode = new TestValueNode("second", 20);
        graph = graph.AddNode(firstNode).AddNode(secondNode);

        var firstProvider = new IncrementalValueProvider<int>(firstNode, graph, default);
        var secondProvider = new IncrementalValueProvider<int>(secondNode, graph, default);

        // Act
        var combinedProvider = firstProvider.Combine(secondProvider);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);
        var result = combinedProvider.Node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal((10, 20), result.ValueOrDefault);
    }
}
