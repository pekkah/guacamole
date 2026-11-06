using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Pipeline;
using Xunit;

namespace Guacamole.Tests.Providers;

/// <summary>
/// Regression tests: exceptions thrown by user lambdas in collection operators must be
/// reported as error results, not escape later when the result is enumerated.
/// </summary>
public class CollectionOperatorErrorTests
{
    private sealed class ConstantCollectionNode(string id, params int[] values) : ICollectionComputationNode<int>
    {
        public string Id { get; } = id;
        public Type OutputType => typeof(IEnumerable<int>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;
        public Result<IEnumerable<int>, ComputationError> Compute(ComputationContext context)
            => Result<IEnumerable<int>, ComputationError>.Success(values);
    }

    private static IncrementalValuesProvider<int> Source()
    {
        var graph = new ComputationGraph();
        var node = new ConstantCollectionNode("source", 1, 2, 3);
        graph.RegisterNodeIfNeeded(node);
        return new IncrementalValuesProvider<int>(node, graph, default);
    }

    private static int Throw(int _) => throw new InvalidOperationException("boom");

    [Fact]
    public void Select_WithThrowingSelector_ReturnsErrorResult()
    {
        var result = Source().Select(Throw).GetValues();

        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.ErrorValue!.Message);
    }

    [Fact]
    public void Where_WithThrowingPredicate_ReturnsErrorResult()
    {
        var result = Source().Where(x => Throw(x) > 0).GetValues();

        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.ErrorValue!.Message);
    }

    [Fact]
    public void SelectMany_WithThrowingSelector_ReturnsErrorResult()
    {
        var result = Source().SelectMany(x => new[] { Throw(x) }).GetValues();

        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.ErrorValue!.Message);
    }

    [Fact]
    public void SelectMany_WithLazilyThrowingSelector_ReturnsErrorResult()
    {
        static IEnumerable<int> LazyThrow(int x)
        {
            yield return x;
            throw new InvalidOperationException("boom");
        }

        var result = Source().SelectMany(LazyThrow).GetValues();

        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.ErrorValue!.Message);
    }

    [Fact]
    public void Select_WithThrowingSelector_NodeComputeReturnsErrorResult()
    {
        var provider = Source().Select(Throw);

        var result = provider.Node.Compute(new ComputationContext(new Dictionary<string, object>()));

        Assert.False(result.IsSuccess);
    }
}
