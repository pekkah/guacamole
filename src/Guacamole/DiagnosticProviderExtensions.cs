using System.Runtime.CompilerServices;

namespace Guacamole;

/// <summary>
/// Extension methods for adding diagnostic metadata to providers for debugging and monitoring.
/// Provides developer experience tooling for complex computation pipelines.
/// </summary>
public static class DiagnosticProviderExtensions
{
    /// <summary>
    /// Adds a tracking name to IncrementalValueProvider for debugging purposes.
    /// Creates a new provider with updated metadata that preserves the tracking name across transformations.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="provider">Source provider</param>
    /// <param name="trackingName">Human-readable identifier for debugging</param>
    /// <param name="callerFilePath">Automatically captured file path</param>
    /// <param name="callerLineNumber">Automatically captured line number</param>
    /// <param name="callerMemberName">Automatically captured member name</param>
    /// <returns>Provider with updated diagnostic metadata</returns>
    public static IncrementalValueProvider<T> WithTrackingName<T>(
        this IncrementalValueProvider<T> provider,
        string trackingName,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = 0,
        [CallerMemberName] string callerMemberName = "")
    {
        var newMetadata = provider.Metadata.WithTrackingName(trackingName);

        // Update creation location to track where WithTrackingName was called
        var sourceLocation = new SourceLocation(callerFilePath, callerLineNumber, 0, callerMemberName);
        newMetadata = newMetadata with { CreationLocation = sourceLocation };

        return new IncrementalValueProvider<T>(provider.Node, provider.Graph, newMetadata);
    }

    /// <summary>
    /// Adds a tracking name to IncrementalValuesProvider for debugging purposes.
    /// Creates a new provider with updated metadata that preserves the tracking name across transformations.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="provider">Source provider</param>
    /// <param name="trackingName">Human-readable identifier for debugging</param>
    /// <param name="callerFilePath">Automatically captured file path</param>
    /// <param name="callerLineNumber">Automatically captured line number</param>
    /// <param name="callerMemberName">Automatically captured member name</param>
    /// <returns>Provider with updated diagnostic metadata</returns>
    public static IncrementalValuesProvider<T> WithTrackingName<T>(
        this IncrementalValuesProvider<T> provider,
        string trackingName,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = 0,
        [CallerMemberName] string callerMemberName = "")
    {
        var newMetadata = provider.Metadata.WithTrackingName(trackingName);

        // Update creation location to track where WithTrackingName was called
        var sourceLocation = new SourceLocation(callerFilePath, callerLineNumber, 0, callerMemberName);
        newMetadata = newMetadata with { CreationLocation = sourceLocation };

        return new IncrementalValuesProvider<T>(provider.Node, provider.Graph, newMetadata);
    }

    /// <summary>
    /// Adds tags to IncrementalValueProvider for grouping and filtering during debugging.
    /// Creates a new provider with additional diagnostic tags.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="provider">Source provider</param>
    /// <param name="tags">Tags for grouping/filtering</param>
    /// <returns>Provider with updated diagnostic tags</returns>
    public static IncrementalValueProvider<T> WithTags<T>(
        this IncrementalValueProvider<T> provider,
        params string[] tags)
    {
        var newMetadata = provider.Metadata.WithTags(tags);
        return new IncrementalValueProvider<T>(provider.Node, provider.Graph, newMetadata);
    }

    /// <summary>
    /// Adds tags to IncrementalValuesProvider for grouping and filtering during debugging.
    /// Creates a new provider with additional diagnostic tags.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="provider">Source provider</param>
    /// <param name="tags">Tags for grouping/filtering</param>
    /// <returns>Provider with updated diagnostic tags</returns>
    public static IncrementalValuesProvider<T> WithTags<T>(
        this IncrementalValuesProvider<T> provider,
        params string[] tags)
    {
        var newMetadata = provider.Metadata.WithTags(tags);
        return new IncrementalValuesProvider<T>(provider.Node, provider.Graph, newMetadata);
    }

    /// <summary>
    /// Adds a pipeline stage identifier to IncrementalValueProvider for metrics correlation.
    /// Creates a new provider with pipeline stage metadata for observability across compilation stages.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="provider">Source provider</param>
    /// <param name="pipelineStage">Pipeline stage identifier (e.g., "parsing", "semantic-analysis")</param>
    /// <returns>Provider with updated pipeline stage metadata</returns>
    public static IncrementalValueProvider<T> WithPipelineStage<T>(
        this IncrementalValueProvider<T> provider,
        string pipelineStage)
    {
        var newMetadata = provider.Metadata.WithPipelineStage(pipelineStage);
        return new IncrementalValueProvider<T>(provider.Node, provider.Graph, newMetadata);
    }

    /// <summary>
    /// Adds a pipeline stage identifier to IncrementalValuesProvider for metrics correlation.
    /// Creates a new provider with pipeline stage metadata for observability across compilation stages.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="provider">Source provider</param>
    /// <param name="pipelineStage">Pipeline stage identifier (e.g., "parsing", "semantic-analysis")</param>
    /// <returns>Provider with updated pipeline stage metadata</returns>
    public static IncrementalValuesProvider<T> WithPipelineStage<T>(
        this IncrementalValuesProvider<T> provider,
        string pipelineStage)
    {
        var newMetadata = provider.Metadata.WithPipelineStage(pipelineStage);
        return new IncrementalValuesProvider<T>(provider.Node, provider.Graph, newMetadata);
    }
}
