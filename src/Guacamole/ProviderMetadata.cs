using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Guacamole;

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
    /// Creates ProviderMetadata with just a tracking name, recording the caller's location as
    /// <see cref="CreationLocation"/>.
    /// </summary>
    /// <remarks>
    /// Call it with a named argument, <c>new ProviderMetadata(trackingName: "parse")</c>: a single
    /// positional string argument also matches the primary constructor, so <c>new ProviderMetadata("parse")</c>
    /// does not compile (CS0121).
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="trackingName"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="trackingName"/> is empty or whitespace.</exception>
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
    /// Validates that a tracking name is not null, empty or whitespace.
    /// </summary>
    private static string ValidateTrackingName(string trackingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackingName);
        return trackingName;
    }

    /// <summary>
    /// Creates a copy with the specified tracking name.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="trackingName"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="trackingName"/> is empty or whitespace.</exception>
    public ProviderMetadata WithTrackingName(string trackingName)
    {
        return this with { TrackingName = ValidateTrackingName(trackingName) };
    }

    /// <summary>
    /// Creates a copy with additional tags, appended to the existing ones.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tags"/> contains duplicates.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="pipelineStage"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pipelineStage"/> is empty or whitespace.</exception>
    public ProviderMetadata WithPipelineStage(string pipelineStage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineStage);
        return this with { PipelineStage = pipelineStage };
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

