using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Guacamole.Core.Diagnostics;

/// <summary>
/// Debugging and diagnostic information attached to providers for visualization and monitoring.
/// Optional metadata that doesn't affect computation with minimal memory overhead.
/// Performance metrics are now collected automatically via GuacamoleMetrics for enterprise observability.
/// </summary>
/// <param name="TrackingName">Optional human-readable identifier</param>
/// <param name="CreationLocation">Where provider was created</param>
/// <param name="Tags">Arbitrary tags for grouping/filtering</param>
/// <param name="PipelineStage">Optional pipeline stage identifier for metrics correlation</param>
public readonly record struct ProviderMetadata(
    string? TrackingName = null,
    SourceLocation? CreationLocation = null,
    ImmutableArray<string> Tags = default,
    string? PipelineStage = null)
{
    /// <summary>
    /// Creates ProviderMetadata with just a tracking name.
    /// </summary>
    /// <param name="trackingName">Human-readable identifier for debugging</param>
    /// <param name="callerFilePath">Automatically captured file path</param>
    /// <param name="callerLineNumber">Automatically captured line number</param>
    /// <param name="callerMemberName">Automatically captured member name</param>
    public ProviderMetadata(
        string trackingName,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = 0,
        [CallerMemberName] string callerMemberName = "")
        : this(
            TrackingName: ValidateTrackingName(trackingName),
            CreationLocation: new SourceLocation(callerFilePath, callerLineNumber, 0, callerMemberName),
            Tags: ImmutableArray<string>.Empty,
            PipelineStage: null)
    {
    }

    /// <summary>
    /// Validates that tracking name is non-empty if provided.
    /// </summary>
    private static string ValidateTrackingName(string? trackingName)
    {
        if (trackingName is not null && string.IsNullOrWhiteSpace(trackingName))
        {
            throw new ArgumentException("TrackingName must be non-empty when provided", nameof(trackingName));
        }
        return trackingName!;
    }

    /// <summary>
    /// Creates a copy with the specified tracking name.
    /// </summary>
    public ProviderMetadata WithTrackingName(string trackingName)
    {
        return this with { TrackingName = ValidateTrackingName(trackingName) };
    }

    /// <summary>
    /// Creates a copy with additional tags.
    /// </summary>
    public ProviderMetadata WithTags(params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        // Validate no duplicate tags
        var uniqueTags = tags.Distinct().ToArray();
        if (uniqueTags.Length != tags.Length)
        {
            throw new ArgumentException("Tags must not contain duplicates", nameof(tags));
        }

        var newTags = Tags.IsDefault ? ImmutableArray.Create(tags) : Tags.AddRange(tags);
        return this with { Tags = newTags };
    }

    /// <summary>
    /// Creates a copy with the specified pipeline stage identifier.
    /// Used for correlating metrics across pipeline stages.
    /// </summary>
    public ProviderMetadata WithPipelineStage(string pipelineStage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineStage);
        return this with { PipelineStage = pipelineStage };
    }

    /// <summary>
    /// Records provider creation metrics if enabled in configuration.
    /// Called automatically by provider constructors.
    /// </summary>
    internal void RecordCreationMetrics(string providerType, string? transformType = null)
    {
        if (MetricsConfiguration.Current.EnableProviderMetrics)
        {
            GuacamoleMetrics.RecordProviderCreation(providerType, TrackingName, transformType);
        }
    }
}

/// <summary>
/// Source code location information for debugging and diagnostics.
/// </summary>
/// <param name="FileName">Source file name</param>
/// <param name="LineNumber">Line number in source</param>
/// <param name="ColumnNumber">Column number in source</param>
/// <param name="MemberName">Containing member name</param>
public readonly record struct SourceLocation(
    string FileName,
    int LineNumber,
    int ColumnNumber,
    string MemberName)
{
    /// <summary>
    /// Returns a string representation of this source location.
    /// </summary>
    public override string ToString() => $"{FileName}:{LineNumber} ({MemberName})";
}

