using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// What <c>[ComputingContext]</c> generates, as seen by a user: input properties backed by sources,
/// <c>{Property}Source</c> accessors, source IDs and the parameterless constructor
/// (README "Source Generation", docs/generated-code-reference.md).
/// </summary>
public partial class GeneratedContextTests
{
    [ComputingContext]
    internal partial class AccessibilityContext : ComputingContextBase
    {
        internal partial IncrementalValuesProvider<string> InternalItems { get; }
        private partial IncrementalValueProvider<int> PrivateNumber { get; }

        public void SetPrivate(int value) => PrivateNumberSource.Set(value);

        public int ReadPrivate() => PrivateNumber.Value;
    }

    [ComputingContext]
    internal partial class EmptyContext : ComputingContextBase
    {
    }

    [Fact]
    public void SourceIds_AreClassNameDotPropertyName()
    {
        using var context = new ApiContext();

        Assert.Equal("ApiContext.Items", context.ItemsSource.SourceId);
        Assert.Equal("ApiContext.Number", context.NumberSource.SourceId);
    }

    [Fact]
    public void CollectionInput_ReturnsTheItemsPushedThroughItsSourceAccessor()
    {
        using var context = new ApiContext();

        Assert.Empty(context.Items.Values);

        context.ItemsSource.Push(new Item("a", 1));
        context.ItemsSource.Push(new Item("b", 2));

        Assert.Equal([new Item("a", 1), new Item("b", 2)], context.Items.Values);
        Assert.Equal(context.ItemsSource.GetSnapshot(), context.Items.Values);
    }

    [Fact]
    public void ValueInput_ReturnsTheValueSetThroughItsSourceAccessor()
    {
        using var context = new ApiContext();

        context.TextSource.Set("hello");

        Assert.Equal("hello", context.Text.Value);
        Assert.Equal("hello", ResultAssert.Success(context.Text.GetValue()));
        Assert.Equal("hello", context.TextSource.GetValue());
    }

    [Fact]
    public void ValueInput_ReadBeforeSet_IsAFailedResult()
    {
        using var context = new ApiContext();

        var error = ResultAssert.Error(context.Text.GetValue());

        Assert.Contains("never been initialized", error.Message);
        Assert.Throws<InvalidOperationException>(() => context.Text.Value);
        Assert.Throws<InvalidOperationException>(() => context.Text.TryGetValue(out _));
    }

    [Fact]
    public void ValueTypeInput_ReadBeforeSet_IsAFailedResult_NotDefault()
    {
        // quickstart: "Reading a single-value input before anything was Set returns a failed Result"
        // holds for value types too: an unset int input must not read as 0.
        using var context = new ApiContext();

        var error = ResultAssert.Error(context.Number.GetValue());
        Assert.Contains("never been initialized", error.Message);

        context.NumberSource.Set(0);
        Assert.Equal(0, context.Number.Value);
    }

    [Fact]
    public void Property_ReturnsAProviderOfTheSameSource_EveryTime()
    {
        using var context = new ApiContext();

        // Equal providers: the same node, so pipelines built from either share caches
        Assert.Equal(context.Items, context.Items);
        Assert.Equal(context.ItemsSource.AsProvider(), context.Items);
        Assert.Equal(context.Number, context.NumberSource.AsProvider());
    }

    [Fact]
    public void SourceProviders_CarrySourceMetadata()
    {
        using var context = new ApiContext();

        Assert.Equal("Source: ApiContext.Items", context.Items.Metadata.TrackingName);
        Assert.Equal("Source: ApiContext.Number", context.Number.Metadata.TrackingName);
    }

    [Fact]
    public void EachContextInstance_HasItsOwnSources()
    {
        using var first = new ApiContext();
        using var second = new ApiContext();

        first.ItemsSource.Push(new Item("a", 1));
        first.NumberSource.Set(1);
        second.NumberSource.Set(2);

        Assert.Single(first.Items.Values);
        Assert.Empty(second.Items.Values);
        Assert.Equal(1, first.Number.Value);
        Assert.Equal(2, second.Number.Value);
    }

    [Fact]
    public void NonPublicInputs_KeepTheirAccessibility_AndWork()
    {
        using var context = new AccessibilityContext();

        context.InternalItemsSource.Push("x");
        context.SetPrivate(7);

        Assert.Equal(["x"], context.InternalItems.Values);
        Assert.Equal(7, context.ReadPrivate());

        var privateSource = typeof(AccessibilityContext).GetProperty(
            "PrivateNumberSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(privateSource);
        Assert.True(privateSource.GetMethod!.IsPrivate);
    }

    [Fact]
    public void ContextWithoutInputs_HasAGeneratedPublicConstructor()
    {
        using var context = new EmptyContext();

        Assert.False(context.IsDisposed);
        Assert.NotNull(typeof(EmptyContext).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void DerivedProperty_AssignedInChainedConstructor_UsesTheGeneratedSources()
    {
        using var context = new DerivedPropertyContext(bonus: 100);

        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2)]);

        Assert.Equal(103, context.Total.Value);
    }
}
