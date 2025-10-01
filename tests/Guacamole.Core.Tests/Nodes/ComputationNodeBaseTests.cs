using System;
using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Nodes;
using Guacamole.Core.Pipeline;
using Xunit;

namespace Guacamole.Core.Tests.Nodes;

/// <summary>
/// Tests for ComputationNodeBase abstract base for generated nodes.
/// Following TDD incremental complexity progression.
/// </summary>
public class ComputationNodeBaseTests
{
    // Concrete test implementation of ComputationNodeBase
    private class TestNode : ComputationNodeBase<int>
    {
        private readonly int _value;

        public TestNode(string id, int value, ImmutableHashSet<string>? dependencies = null)
            : base(id, dependencies)
        {
            _value = value;
        }

        public override Result<int, ComputationError> Compute(ComputationContext context)
        {
            return Result<int, ComputationError>.Success(_value);
        }
    }

    [Fact]
    public void Node_WhenCreated_HasValidMetadata()
    {
        // Arrange & Act
        var node = new TestNode("test-node", 42);

        // Assert
        Assert.Equal("test-node", node.Id);
        Assert.Equal(typeof(int), node.OutputType);
        Assert.Empty(node.Dependencies);
    }

    [Fact]
    public void Node_WithDependencies_TracksRelationships()
    {
        // Arrange
        var dependencies = ImmutableHashSet.Create("dep1", "dep2");

        // Act
        var node = new TestNode("test-node", 42, dependencies);

        // Assert
        Assert.Equal(2, node.Dependencies.Count);
        Assert.Contains("dep1", node.Dependencies);
        Assert.Contains("dep2", node.Dependencies);
    }

    [Fact]
    public void Node_WithNullOrWhiteSpaceId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new TestNode("", 42));
        Assert.Throws<ArgumentException>(() => new TestNode("   ", 42));
    }

    [Fact]
    public void Node_Compute_ReturnsExpectedValue()
    {
        // Arrange
        var node = new TestNode("test-node", 42);
        var context = new ComputationContext(ImmutableDictionary<string, object>.Empty);

        // Act
        var result = node.Compute(context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }
}
