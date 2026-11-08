using System.Collections.Immutable;
using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// <see cref="ProviderMetadata"/>, the metadata parameters of the operators ("Optional metadata for
/// the new provider"; operators preserve their input's metadata when none is given) and
/// <see cref="DiagnosticProviderExtensions"/>.
/// </summary>
public class MetadataTests
{
    [Fact]
    public void Operators_WithoutMetadata_PreserveTheInputsMetadata()
    {
        using var context = new ApiContext();
        var named = context.Number.WithTrackingName("number").WithTags("t").WithPipelineStage("stage");
        var namedItems = context.Items.WithTrackingName("items");

        Assert.Equal(named.Metadata, named.Select(static x => x).Metadata);
        Assert.Equal(named.Metadata, named.Where(static x => x > 0).Metadata);
        Assert.Equal(named.Metadata, named.Combine(context.Text).Metadata);
        Assert.Equal(named.Metadata, named.SelectMany(static x => new[] { x }).Metadata);
        Assert.Equal(namedItems.Metadata, namedItems.Select(static i => i).Metadata);
        Assert.Equal(namedItems.Metadata, namedItems.Where(static _ => true).Metadata);
        Assert.Equal(namedItems.Metadata, namedItems.SelectMany(static i => new[] { i }).Metadata);
        Assert.Equal(namedItems.Metadata, namedItems.Collect().Metadata);
        Assert.Equal(namedItems.Metadata, namedItems.Combine(context.Number).Metadata);
        Assert.Equal(namedItems.Metadata, namedItems.Merge(context.OtherItems).Metadata);
    }

    [Fact]
    public void Operators_WithMetadata_UseIt()
    {
        using var context = new ApiContext();
        var metadata = new ProviderMetadata(trackingName: "explicit");

        Assert.Equal(metadata, context.Number.Select(static x => x, metadata).Metadata);
        Assert.Equal(metadata, context.Number.Select(static x => x, EqualityComparer<int>.Default, metadata).Metadata);
        Assert.Equal(metadata, context.Number.Where(static x => x > 0, metadata).Metadata);
        Assert.Equal(metadata, context.Number.Combine(context.Text, metadata).Metadata);
        Assert.Equal(metadata, context.Items.Select(static i => i, metadata).Metadata);
        Assert.Equal(metadata, context.Items.Collect(metadata).Metadata);
        Assert.Equal(metadata, context.Items.Collect(EqualityComparer<Item>.Default, metadata).Metadata);
        Assert.Equal(metadata, context.Items.Combine(context.Number, metadata).Metadata);
        Assert.Equal(metadata, context.Items.Merge(context.OtherItems, metadata).Metadata);
        Assert.Equal(metadata, context.Words.Select(static w => w.ToImmutableArray()).Flatten(metadata).Metadata);
    }

    [Fact]
    public void Operators_WithMetadataThatHasOnlyTagsOrAPipelineStage_UseIt()
    {
        // Metadata passed to an operator is the new provider's metadata, even without a tracking name
        using var context = new ApiContext();
        var stageOnly = default(ProviderMetadata).WithPipelineStage("parse");
        var tagsOnly = default(ProviderMetadata).WithTags("hot");

        Assert.Equal("parse", context.Number.Select(static x => x, stageOnly).Metadata.PipelineStage);
        Assert.Equal(["hot"], context.Items.Select(static i => i, tagsOnly).Metadata.Tags);
        Assert.Equal("parse", context.Items.Collect(stageOnly).Metadata.PipelineStage);
    }

    [Fact]
    public void Metadata_DoesNotAffectNodeIdentityOrValues()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(2);
        var calls = new CallCounter();
        Func<int, int> selector = x => calls.Pass(x * 3);

        var plain = context.Number.Select(selector);
        var named = context.Number.Select(selector, new ProviderMetadata(trackingName: "named"));

        Assert.Equal(6, plain.Value);
        Assert.Equal(6, named.Value);
        Assert.Equal(1, calls.Count); // one shared node
    }

    [Fact]
    public void WithTrackingName_RecordsTheCallersLocation()
    {
        using var context = new ApiContext();

        var named = context.Number.WithTrackingName("n");

        Assert.Equal("n", named.Metadata.TrackingName);
        Assert.NotNull(named.Metadata.CreationLocation);
        Assert.EndsWith("MetadataTests.cs", named.Metadata.CreationLocation.Value.FileName);
        Assert.Equal(nameof(WithTrackingName_RecordsTheCallersLocation), named.Metadata.CreationLocation.Value.MemberName);
    }

    [Fact]
    public void ProviderMetadata_Validation()
    {
        Assert.Throws<ArgumentException>(() => new ProviderMetadata(trackingName: " "));
        Assert.Throws<ArgumentException>(() => default(ProviderMetadata).WithTrackingName(""));
        Assert.Throws<ArgumentNullException>(() => default(ProviderMetadata).WithTrackingName(null!));
        Assert.Throws<ArgumentNullException>(() => default(ProviderMetadata).WithTags(null!));
        Assert.Throws<ArgumentException>(() => default(ProviderMetadata).WithTags("a", "a"));
        Assert.Throws<ArgumentException>(() => default(ProviderMetadata).WithPipelineStage(" "));
        Assert.Throws<ArgumentNullException>(() => default(ProviderMetadata).WithPipelineStage(null!));
    }

    [Fact]
    public void DiagnosticExtensions_ValidateLikeProviderMetadata()
    {
        using var context = new ApiContext();

        Assert.Throws<ArgumentNullException>(() => context.Number.WithTrackingName(null!));
        Assert.Throws<ArgumentException>(() => context.Items.WithTrackingName(" "));
        Assert.Throws<ArgumentException>(() => context.Items.WithTags("x", "x"));
        Assert.Throws<ArgumentException>(() => context.Number.WithPipelineStage(""));
    }

    [Fact]
    public void WithTags_AppendsToExistingTags()
    {
        var metadata = default(ProviderMetadata).WithTags("a").WithTags("b", "c");

        Assert.Equal(["a", "b", "c"], metadata.Tags);
    }

    [Fact]
    public void DiagnosticExtensions_DoNotChangeTheProvidersValues()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(5);

        Assert.Equal(5, context.Number.WithTrackingName("n").WithTags("t").WithPipelineStage("s").Value);
    }

    [Fact]
    public void SourceLocation_ToString_ShowsFileLineAndMember()
    {
        Assert.Equal("File.cs:12 (Member)", new SourceLocation("File.cs", 12, 3, "Member").ToString());
    }
}
