using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// The dependency-injection extensions as documented in their XML docs and the quickstart
/// ("register contexts like any other service").
/// </summary>
/// <remarks>
/// The registration method's name is used in two helpers only (<see cref="AddGuacamole(IServiceCollection)"/>)
/// so a rename touches two lines.
/// </remarks>
public class DependencyInjectionTests
{
    private static IServiceCollection AddGuacamole(IServiceCollection services) => services.AddGuacamoleCore();

    private static IServiceCollection AddGuacamole(IServiceCollection services, Action<GuacamoleCoreOptions> configure) =>
        services.AddGuacamoleCore(configure);

    [Fact]
    public void Registration_ReturnsTheSameCollection_ForChaining()
    {
        var services = new ServiceCollection();

        Assert.Same(services, AddGuacamole(services));
        Assert.Same(services, AddGuacamole(services, _ => { }));
    }

    [Fact]
    public void Registration_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => AddGuacamole(null!));
        Assert.Throws<ArgumentNullException>(() => AddGuacamole(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => AddGuacamole(new ServiceCollection(), null!));
    }

    [Fact]
    public void DefaultRegistration_ProvidesSingletonOptions_AndAFactoryWithoutDefaultTags()
    {
        using var provider = AddGuacamole(new ServiceCollection()).BuildServiceProvider();

        var options = provider.GetRequiredService<GuacamoleCoreOptions>();
        Assert.Same(options, provider.GetRequiredService<GuacamoleCoreOptions>());
        Assert.Empty(options.ProviderOptions.DefaultTags);

        var factory = provider.GetRequiredService<IProviderMetadataFactory>();
        Assert.Same(factory, provider.GetRequiredService<IProviderMetadataFactory>());
        var metadata = factory.Create("parse");
        Assert.Equal("parse", metadata.TrackingName);
        Assert.True(metadata.Tags.IsDefaultOrEmpty);
    }

    [Fact]
    public void Factory_AppliesDefaultTagsFirst_AndThePipelineStage()
    {
        using var provider = AddGuacamole(new ServiceCollection(), o => o.ProviderOptions.DefaultTags = ["app", "env"])
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IProviderMetadataFactory>();

        var metadata = factory.Create("parse", ["x"], "stage");

        Assert.Equal(["app", "env", "x"], metadata.Tags);
        Assert.Equal("stage", metadata.PipelineStage);
        Assert.Equal(["app", "env"], factory.Create("bind").Tags);
    }

    [Fact]
    public void RegisteringTwice_KeepsTheFirstConfiguration()
    {
        var services = new ServiceCollection();
        AddGuacamole(services, o => o.ProviderOptions.DefaultTags = ["first"]);
        AddGuacamole(services, o => o.ProviderOptions.DefaultTags = ["second"]);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(["first"], provider.GetRequiredService<GuacamoleCoreOptions>().ProviderOptions.DefaultTags);
        Assert.Single(services, d => d.ServiceType == typeof(IProviderMetadataFactory));
    }

    [Fact]
    public void FactoryMetadata_CanBePassedToOperators()
    {
        using var provider = AddGuacamole(new ServiceCollection(), o => o.ProviderOptions.DefaultTags = ["app"])
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IProviderMetadataFactory>();
        using var context = new ApiContext();

        var doubled = context.Number.Select(static x => x * 2, factory.Create("double"));

        Assert.Equal("double", doubled.Metadata.TrackingName);
        Assert.Equal(["app"], doubled.Metadata.Tags);
    }

    [Fact]
    public void Contexts_CanBeRegisteredAsOrdinaryServices_AndAreDisposedWithTheirScope()
    {
        var services = new ServiceCollection();
        AddGuacamole(services);
        services.AddScoped<ApiContext>();
        services.AddTransient(_ => new DerivedPropertyContext(bonus: 1));
        using var provider = services.BuildServiceProvider();

        ApiContext scoped;
        using (var scope = provider.CreateScope())
        {
            scoped = scope.ServiceProvider.GetRequiredService<ApiContext>();
            Assert.Same(scoped, scope.ServiceProvider.GetRequiredService<ApiContext>());

            var derived = scope.ServiceProvider.GetRequiredService<DerivedPropertyContext>();
            derived.ItemsSource.Push(new Item("a", 2));
            Assert.Equal(3, derived.Total.Value);
        }

        Assert.True(scoped.IsDisposed);
    }

    [Fact]
    public void DefaultTags_DefaultToEmpty()
    {
        Assert.Equal(ImmutableArray<string>.Empty, new ProviderOptions().DefaultTags);
        Assert.NotNull(new GuacamoleCoreOptions().ProviderOptions);
    }
}
