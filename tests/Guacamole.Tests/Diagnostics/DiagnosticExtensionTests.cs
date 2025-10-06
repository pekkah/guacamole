using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;
using Guacamole.Core.Providers;
using Guacamole.Core.Providers.Extensions;
using Xunit;

namespace Guacamole.Core.Tests.Diagnostics;

/// <summary>
/// T015: Diagnostic extensions tests (WithTrackingName, WithTags, WithMetrics)
/// </summary>
public class DiagnosticExtensionTests
{
    /// <summary>
    /// BASIC: Test WithTrackingName adds debugging identifier to IncrementalValueProvider
    /// </summary>
    [Fact]
    public void WithTrackingName_ShouldAdd_DebuggingIdentifier()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", 42);
        var trackingName = "my-debug-provider";

        // Act
        var trackedProvider = provider.WithTrackingName(trackingName);

        // Assert
        Assert.Equal(trackingName, trackedProvider.Metadata.TrackingName);
        Assert.NotNull(trackedProvider.Metadata.CreationLocation);
        Assert.Equal("WithTrackingName_ShouldAdd_DebuggingIdentifier", trackedProvider.Metadata.CreationLocation!.Value.MemberName);

        // Original provider should be unchanged
        Assert.Null(provider.Metadata.TrackingName);

        // Node and graph should be preserved
        Assert.Equal(provider.Node, trackedProvider.Node);
        Assert.Equal(provider.Graph, trackedProvider.Graph);
    }

    /// <summary>
    /// BASIC: Test WithTrackingName adds debugging identifier to IncrementalValuesProvider
    /// </summary>
    [Fact]
    public void WithTrackingName_ShouldAdd_DebuggingIdentifierToValuesProvider()
    {
        // Arrange
        var provider = CreateValuesProvider("test-values", [1, 2, 3]);
        var trackingName = "my-debug-values";

        // Act
        var trackedProvider = provider.WithTrackingName(trackingName);

        // Assert
        Assert.Equal(trackingName, trackedProvider.Metadata.TrackingName);
        Assert.NotNull(trackedProvider.Metadata.CreationLocation);

        // Original provider should be unchanged
        Assert.Null(provider.Metadata.TrackingName);

        // Node and graph should be preserved
        Assert.Equal(provider.Node, trackedProvider.Node);
        Assert.Equal(provider.Graph, trackedProvider.Graph);
    }

    /// <summary>
    /// BASIC: Test WithTags adds diagnostic tags to providers
    /// </summary>
    [Fact]
    public void WithTags_ShouldAdd_DiagnosticTags()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", "value");
        var tags = new[] { "analysis", "type-checking", "performance" };

        // Act
        var taggedProvider = provider.WithTags(tags);

        // Assert
        Assert.Equal(tags, taggedProvider.Metadata.Tags);

        // Original provider should be unchanged
        Assert.True(provider.Metadata.Tags.IsDefaultOrEmpty);

        // Node and graph should be preserved
        Assert.Equal(provider.Node, taggedProvider.Node);
        Assert.Equal(provider.Graph, taggedProvider.Graph);
    }

    /// <summary>
    /// EDGE: Test duplicate tags are rejected
    /// </summary>
    [Fact]
    public void WithTags_ShouldReject_DuplicateTags()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", "value");
        var duplicateTags = new[] { "tag1", "tag2", "tag1" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => provider.WithTags(duplicateTags));
    }

    /// <summary>
    /// BASIC: Test WithPipelineStage adds pipeline correlation data to providers
    /// </summary>
    [Fact]
    public void WithPipelineStage_ShouldAdd_PipelineCorrelationData()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", 123);
        var pipelineStage = "semantic-analysis";

        // Act
        var stageProvider = provider.WithPipelineStage(pipelineStage);

        // Assert
        Assert.Equal(pipelineStage, stageProvider.Metadata.PipelineStage);

        // Original provider should be unchanged
        Assert.Null(provider.Metadata.PipelineStage);

        // Node and graph should be preserved
        Assert.Equal(provider.Node, stageProvider.Node);
        Assert.Equal(provider.Graph, stageProvider.Graph);
    }

    /// <summary>
    /// INCREMENTAL: Test chaining diagnostic extensions
    /// </summary>
    [Fact]
    public void DiagnosticExtensions_ShouldBeChainable()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", "chained");
        var pipelineStage = "optimization-phase";

        // Act
        var enhancedProvider = provider
            .WithTrackingName("chained-provider")
            .WithTags("pipeline", "enhanced")
            .WithPipelineStage(pipelineStage);

        // Assert
        Assert.Equal("chained-provider", enhancedProvider.Metadata.TrackingName);
        Assert.Equal(new[] { "pipeline", "enhanced" }, enhancedProvider.Metadata.Tags);
        Assert.Equal(pipelineStage, enhancedProvider.Metadata.PipelineStage);

        // Node and graph should be preserved through chain
        Assert.Equal(provider.Node, enhancedProvider.Node);
        Assert.Equal(provider.Graph, enhancedProvider.Graph);
    }

    /// <summary>
    /// EDGE: Test empty tracking name validation
    /// </summary>
    [Fact]
    public void WithTrackingName_ShouldValidate_NonEmptyName()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", "value");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => provider.WithTrackingName(""));
        Assert.Throws<ArgumentException>(() => provider.WithTrackingName("   "));
    }

    /// <summary>
    /// EDGE: Test empty pipeline stage validation
    /// </summary>
    [Fact]
    public void WithPipelineStage_ShouldValidate_NonEmptyStage()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", "value");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => provider.WithPipelineStage(""));
        Assert.Throws<ArgumentException>(() => provider.WithPipelineStage("   "));
    }

    /// <summary>
    /// INCREMENTAL: Test metadata preservation across transformations
    /// </summary>
    [Fact]
    public void DiagnosticMetadata_ShouldPreserve_AcrossTransformations()
    {
        // Arrange
        var provider = CreateValueProvider("test-node", 10)
            .WithTrackingName("original-provider")
            .WithTags("source");

        // Act - Transform the provider (using Select extension)
        var transformedProvider = provider.Select(x => x * 2);

        // Assert - Original metadata should be preserved in new provider
        Assert.Equal("original-provider", transformedProvider.Metadata.TrackingName);
        Assert.Equal(new[] { "source" }, transformedProvider.Metadata.Tags);
    }

    /// <summary>
    /// Helper method to create IncrementalValueProvider for testing
    /// </summary>
    private static IncrementalValueProvider<T> CreateValueProvider<T>(string nodeId, T value)
    {
        var node = new TestComputationNode<T>(nodeId, value);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata();
        return new IncrementalValueProvider<T>(node, graph, metadata);
    }

    /// <summary>
    /// Helper method to create IncrementalValuesProvider for testing
    /// </summary>
    private static IncrementalValuesProvider<T> CreateValuesProvider<T>(string nodeId, T[] values)
    {
        var node = new TestValuesNode<T>(nodeId, values);
        var graph = new ComputationGraph();
        var metadata = new ProviderMetadata();
        return new IncrementalValuesProvider<T>(node, graph, metadata);
    }
}

/// <summary>
/// Test implementation of IComputationNode for single values
/// </summary>
internal class TestComputationNode<T> : IComputationNode<T>
{
    private readonly T _value;

    public TestComputationNode(string id, T value)
    {
        Id = id;
        _value = value;
        OutputType = typeof(T);
        Dependencies = ImmutableHashSet<string>.Empty;
    }

    public string Id { get; }
    public Type OutputType { get; }
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<T, ComputationError> Compute(ComputationContext context)
    {
        return Result<T, ComputationError>.Success(_value);
    }
}

/// <summary>
/// Test implementation of ICollectionComputationNode for multiple values (reused from existing tests)
/// </summary>
internal class TestValuesNode<T> : ICollectionComputationNode<T>
{
    public TestValuesNode(string id, T[] values)
    {
        Id = id;
        Values = values;
        OutputType = typeof(IEnumerable<T>);
        Dependencies = ImmutableHashSet<string>.Empty;
    }

    public string Id { get; }
    public T[] Values { get; }
    public Type OutputType { get; }
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context)
    {
        return Result<IEnumerable<T>, ComputationError>.Success(Values);
    }
}
