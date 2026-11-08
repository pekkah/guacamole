using System.Collections.Immutable;

namespace Guacamole;

/// <summary>
/// The tags of source providers' metadata. Shared instances, because <see cref="ImmutableArray{T}"/>
/// compares by reference and providers of the same source must be equal.
/// </summary>
internal static class SourceProviderTags
{
    public static readonly ImmutableArray<string> Collection = ImmutableArray.Create("source", "collection-source");

    public static readonly ImmutableArray<string> Value = ImmutableArray.Create("source", "value-source");
}
