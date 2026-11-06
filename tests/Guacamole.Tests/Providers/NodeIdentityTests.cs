using System.Collections.Immutable;
using System.Linq;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Providers;

/// <summary>
/// Regression tests: every pipeline operator call must create a node with its own
/// identity, so that different transformations of the same source never share a
/// cache slot, and argument order of binary operators is preserved.
/// </summary>
public partial class NodeIdentityTests
{
    [ComputingContext]
    private partial class NodeIdValueContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> First { get; }
        public partial IncrementalValueProvider<int> Second { get; }
    }

    [ComputingContext]
    private partial class NodeIdValuesContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<int> Left { get; }
        public partial IncrementalValuesProvider<int> Right { get; }
    }

    private sealed class ConstantNode<T>(string id, T value) : IComputationNode<T>
    {
        public string Id { get; } = id;
        public Type OutputType => typeof(T);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;
        public Result<T, ComputationError> Compute(ComputationContext context) => Result<T, ComputationError>.Success(value);
    }

    private sealed class ConstantCollectionNode<T>(string id, params T[] values) : ICollectionComputationNode<T>
    {
        public string Id { get; } = id;
        public Type OutputType => typeof(IEnumerable<T>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;
        public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context) => Result<IEnumerable<T>, ComputationError>.Success(values);
    }

    private static IncrementalValueProvider<T> Constant<T>(ComputationGraph graph, string id, T value)
    {
        var node = new ConstantNode<T>(id, value);
        graph.RegisterNodeIfNeeded(node);
        return new(node, graph, default);
    }

    private static IncrementalValuesProvider<T> Constants<T>(ComputationGraph graph, string id, params T[] values)
    {
        var node = new ConstantCollectionNode<T>(id, values);
        graph.RegisterNodeIfNeeded(node);
        return new(node, graph, default);
    }

    [Fact]
    public void Select_OnCachedRoot_WithDifferentSelectors_DoesNotShareCacheSlot()
    {
        var src = Constant(new ComputationGraph(), "src", 1);

        Assert.Equal(2, src.Select(x => x + 1).Value);
        Assert.Equal(100, src.Select(x => x * 100).Value);
    }

    [Fact]
    public void Where_OnCachedRoot_WithDifferentPredicates_DoesNotShareCacheSlot()
    {
        var src = Constant(new ComputationGraph(), "src", 5);

        Assert.True(src.Where(x => x > 0).GetValue().IsSuccess);
        Assert.False(src.Where(x => x < 0).GetValue().IsSuccess);
    }

    [Fact]
    public void SelectMany_OnCachedRoot_WithDifferentSelectors_DoesNotShareCacheSlot()
    {
        var src = Constant(new ComputationGraph(), "src", 3);

        Assert.Equal(new[] { 3 }, src.SelectMany(x => new[] { x }).Values.ToArray());
        Assert.Equal(new[] { 3, 3 }, src.SelectMany(x => new[] { x, x }).Values.ToArray());
    }

    [Fact]
    public void ValuesOperators_OnCachedRoot_WithDifferentLambdas_DoNotShareCacheSlot()
    {
        var src = Constants(new ComputationGraph(), "src", 1, 2);

        Assert.Equal(new[] { 2, 3 }, src.Select(x => x + 1).Values.ToArray());
        Assert.Equal(new[] { 100, 200 }, src.Select(x => x * 100).Values.ToArray());
        Assert.Equal(new[] { 1 }, src.Where(x => x == 1).Values.ToArray());
        Assert.Equal(new[] { 2 }, src.Where(x => x == 2).Values.ToArray());
        Assert.Equal(new[] { 1, 2 }, src.SelectMany(x => new[] { x }).Values.ToArray());
        Assert.Equal(new[] { 1, 1, 2, 2 }, src.SelectMany(x => new[] { x, x }).Values.ToArray());
        Assert.Equal(new[] { 2, 4 }, src.Select(x => x * 2).Collect().Value.ToArray());
        Assert.Equal(new[] { 3, 6 }, src.Select(x => x * 3).Collect().Value.ToArray());
    }

    [Fact]
    public void Combine_OnCachedRoots_WithSwappedArguments_PreservesArgumentOrder()
    {
        var graph = new ComputationGraph();
        var a = Constant(graph, "a", 1);
        var b = Constant(graph, "b", 2);

        Assert.Equal((1, 2), a.Combine(b).Value);
        Assert.Equal((2, 1), b.Combine(a).Value);
    }

    [Fact]
    public void Merge_OnCachedRoots_WithSwappedArguments_PreservesArgumentOrder()
    {
        var graph = new ComputationGraph();
        var a = Constants(graph, "a", 1);
        var b = Constants(graph, "b", 2);

        Assert.Equal(new[] { 1, 2 }, a.Merge(b).Values.ToArray());
        Assert.Equal(new[] { 2, 1 }, b.Merge(a).Values.ToArray());
    }

    [Fact]
    public void Select_WithDifferentSelectorsOnSameSource_ReturnsIndependentValues()
    {
        using var context = new NodeIdValueContext();
        context.FirstSource.Set(1);

        var plusOne = context.First.Select(x => x + 1);
        var timesHundred = context.First.Select(x => x * 100);

        Assert.Equal(2, plusOne.Value);
        Assert.Equal(100, timesHundred.Value);
        Assert.NotEqual(plusOne.Node.Id, timesHundred.Node.Id);
    }

    [Fact]
    public void Where_WithDifferentPredicatesOnSameSource_ReturnsIndependentResults()
    {
        using var context = new NodeIdValueContext();
        context.FirstSource.Set(5);

        var passes = context.First.Where(x => x > 0);
        var fails = context.First.Where(x => x < 0);

        Assert.True(passes.GetValue().IsSuccess);
        Assert.False(fails.GetValue().IsSuccess);
    }

    [Fact]
    public void SelectValues_WithDifferentSelectorsOnSameSource_ReturnsIndependentValues()
    {
        using var context = new NodeIdValuesContext();
        context.LeftSource.Push(1);
        context.LeftSource.Push(2);

        var plusOne = context.Left.Select(x => x + 1);
        var timesHundred = context.Left.Select(x => x * 100);

        Assert.Equal(new[] { 2, 3 }, plusOne.Values.ToArray());
        Assert.Equal(new[] { 100, 200 }, timesHundred.Values.ToArray());
    }

    [Fact]
    public void WhereValues_WithDifferentPredicatesOnSameSource_ReturnsIndependentValues()
    {
        using var context = new NodeIdValuesContext();
        context.LeftSource.Push(1);
        context.LeftSource.Push(2);

        var odd = context.Left.Where(x => x % 2 == 1);
        var even = context.Left.Where(x => x % 2 == 0);

        Assert.Equal(new[] { 1 }, odd.Values.ToArray());
        Assert.Equal(new[] { 2 }, even.Values.ToArray());
    }

    [Fact]
    public void Combine_WithSwappedArguments_PreservesArgumentOrder()
    {
        using var context = new NodeIdValueContext();
        context.FirstSource.Set(1);
        context.SecondSource.Set(2);

        var ab = context.First.Combine(context.Second);
        var ba = context.Second.Combine(context.First);

        Assert.Equal((1, 2), ab.Value);
        Assert.Equal((2, 1), ba.Value);
    }

    [Fact]
    public void Merge_WithSwappedArguments_PreservesArgumentOrder()
    {
        using var context = new NodeIdValuesContext();
        context.LeftSource.Push(1);
        context.RightSource.Push(2);

        var leftThenRight = context.Left.Merge(context.Right);
        var rightThenLeft = context.Right.Merge(context.Left);

        Assert.Equal(new[] { 1, 2 }, leftThenRight.Values.ToArray());
        Assert.Equal(new[] { 2, 1 }, rightThenLeft.Values.ToArray());
    }
}
