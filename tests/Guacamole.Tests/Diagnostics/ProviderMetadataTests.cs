using System;
using Xunit;

namespace Guacamole.Tests.Diagnostics;

/// <summary>
/// Issue #90: <c>new ProviderMetadata("name")</c> was ambiguous (CS0121) between the positional
/// record constructor and a tracking-name constructor with caller-info parameters, and the source
/// providers passed a description into the caller file path. The caller-info variant is now the
/// <see cref="ProviderMetadata.Named"/> factory.
/// </summary>
public partial class ProviderMetadataTests
{
    [ComputingContext]
    private partial class SourcesContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }

        public partial IncrementalValuesProvider<int> Items { get; }
    }

    [Fact]
    public void Constructor_WithTrackingNameOnly_Compiles_AndHasNoCreationLocation()
    {
        var metadata = new ProviderMetadata("parse");

        Assert.Equal("parse", metadata.TrackingName);
        Assert.Null(metadata.CreationLocation);
    }

    [Fact]
    public void Named_CapturesCallerLocation()
    {
        var metadata = ProviderMetadata.Named("parse");

        Assert.Equal("parse", metadata.TrackingName);
        Assert.NotNull(metadata.CreationLocation);
        Assert.EndsWith(nameof(ProviderMetadataTests) + ".cs", metadata.CreationLocation.Value.FileName);
        Assert.Equal(nameof(Named_CapturesCallerLocation), metadata.CreationLocation.Value.MemberName);
        Assert.True(metadata.CreationLocation.Value.LineNumber > 0);
        Assert.Empty(metadata.Tags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Named_RejectsEmptyName(string name)
    {
        Assert.Throws<ArgumentException>(() => ProviderMetadata.Named(name));
    }

    [Fact]
    public void SourceProviders_HaveTrackingNameAndTags_WithoutBogusLocation()
    {
        using var context = new SourcesContext();

        var value = context.Number.Metadata;
        var items = context.Items.Metadata;

        Assert.Equal("Source: SourcesContext.Number", value.TrackingName);
        Assert.Null(value.CreationLocation);
        Assert.Equal(["source", "value-source"], value.Tags);

        Assert.Equal("Source: SourcesContext.Items", items.TrackingName);
        Assert.Null(items.CreationLocation);
        Assert.Equal(["source", "collection-source"], items.Tags);
    }
}
