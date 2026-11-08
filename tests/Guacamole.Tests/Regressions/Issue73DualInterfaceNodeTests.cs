using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests.Regressions;

/// <summary>
/// Regression tests for issue #73: a node implementing both <see cref="IComputationNode{T}"/> and
/// <see cref="ICollectionComputationNode{T}"/> got a second evaluation handle that was never stored.
/// Reverse edges reference handles weakly, so once that handle was collected, source changes no
/// longer reached the nodes reading the node, and a stamped value downstream could be served stale.
/// Such nodes are now rejected on first use, whichever interface they are read through, and every
/// other node has exactly one handle.
/// </summary>
public class Issue73DualInterfaceNodeTests
{
    /// <summary>
    /// Reads the source: as a single value <c>source + 1</c>, as a collection <c>[source, source + 1]</c>.
    /// </summary>
    private sealed class DualNode(IComputationNode<int> source) : IComputationNode<int>, ICollectionComputationNode<int>
    {
        public string Id { get; } = "dual-" + Guid.NewGuid().ToString("N");

        public Type OutputType => typeof(int);

        public ImmutableHashSet<string> Dependencies { get; } = [source.Id];

        public Result<int, ComputationError> Compute(ComputationContext context)
        {
            var value = context.Read(source);
            return value.IsSuccess ? Result<int, ComputationError>.Success(value.ValueOrDefault + 1) : value;
        }

        Result<IEnumerable<int>, ComputationError> ICollectionComputationNode<int>.Compute(ComputationContext context)
        {
            var value = context.Read(source);
            return value.IsSuccess
                ? Result<IEnumerable<int>, ComputationError>.Success([value.ValueOrDefault, value.ValueOrDefault + 1])
                : Result<IEnumerable<int>, ComputationError>.Error(value.ErrorValue!);
        }
    }

    /// <summary>
    /// A node that implements one result interface, for comparison.
    /// </summary>
    private sealed class PlusOneNode(IComputationNode<int> source) : IComputationNode<int>
    {
        public string Id { get; } = "plus-one-" + Guid.NewGuid().ToString("N");

        public Type OutputType => typeof(int);

        public ImmutableHashSet<string> Dependencies { get; } = [source.Id];

        public Result<int, ComputationError> Compute(ComputationContext context)
        {
            var value = context.Read(source);
            return value.IsSuccess ? Result<int, ComputationError>.Success(value.ValueOrDefault + 1) : value;
        }
    }

    private sealed class Setup
    {
        public Setup()
        {
            Source = new IncrementalValueSource<int>("source", () => Graph);
            Graph = Graph.AddNode(Source.GetNode());
            Source.Set(1);
        }

        public ComputationGraph Graph { get; } = new();

        public IncrementalValueSource<int> Source { get; }

        public void Change(int value)
        {
            Source.Set(value);
            Graph.MarkSourceChanged(Source.GetNode().Id);
        }
    }

    [Fact]
    public void ReadingADualInterfaceNodeAsAValue_IsRejected()
    {
        var setup = new Setup();
        var node = new DualNode(setup.Source.GetNode());
        var provider = new IncrementalValueProvider<int>(node, setup.Graph, default);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetValue());

        Assert.Contains(node.Id, exception.Message);
        Assert.Contains("more than one of IComputationNode<T> and ICollectionComputationNode<T>", exception.Message);
    }

    [Fact]
    public void ReadingADualInterfaceNodeAsACollection_IsRejected()
    {
        var setup = new Setup();
        var node = new DualNode(setup.Source.GetNode());
        var provider = new IncrementalValuesProvider<int>(node, setup.Graph, default);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetValues());

        Assert.Contains(node.Id, exception.Message);
    }

    [Fact]
    public void ReadingADualInterfaceNode_ThroughBothInterfaces_IsRejectedBothTimes()
    {
        var setup = new Setup();
        var node = new DualNode(setup.Source.GetNode());

        Assert.Throws<InvalidOperationException>(() => new IncrementalValuesProvider<int>(node, setup.Graph, default).GetValues());
        Assert.Throws<InvalidOperationException>(() => new IncrementalValueProvider<int>(node, setup.Graph, default).GetValue());

        // No handle was created for it, so no reverse edge can reference a handle that is not kept
        Assert.False(setup.Graph.SharedState.Handles.TryGetValue(node, out _));
    }

    [Fact]
    public void ADownstreamStageReadingADualInterfaceNode_ReportsTheRejectionAsAnError()
    {
        var setup = new Setup();
        var node = new DualNode(setup.Source.GetNode());
        var downstream = new IncrementalValueProvider<int>(node, setup.Graph, default).Select(x => x * 10);

        var result = downstream.GetValue();

        Assert.True(result.IsError);
        Assert.Contains(node.Id, result.ErrorValue!.Message);
    }

    /// <summary>
    /// The scenario of the issue with a node that has one result interface: it is read directly and
    /// as a dependency, its handles are collected if they are not shared, and a change of the source
    /// must still reach the stage reading it (whose value is stamped).
    /// </summary>
    [Fact]
    public void ASingleInterfaceNode_ReadDirectlyAndAsADependency_HasOneHandle_AndKeepsItsEdges()
    {
        var setup = new Setup();
        var node = new PlusOneNode(setup.Source.GetNode());
        var direct = new IncrementalValueProvider<int>(node, setup.Graph, default);
        var downstream = direct.Select(x => x * 10);

        Assert.Equal(2, direct.Value);
        Assert.Equal(20, downstream.Value);
        Assert.True(setup.Graph.SharedState.Handles.TryGetValue(node, out var handle));
        Assert.Same(handle, ValueNodeHandle<int>.For(node, setup.Graph));

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        var sourceState = setup.Graph.Versions.GetState(setup.Source.GetNode().Id);
        var dependents = new List<NodeHandle>();
        sourceState.CopyDependentsTo(dependents);
        Assert.Contains(handle, dependents);

        setup.Change(5);
        Assert.Equal(60, downstream.Value);
        Assert.Equal(6, direct.Value);
    }
}
