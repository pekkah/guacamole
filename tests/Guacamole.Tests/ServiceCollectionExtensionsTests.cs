using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guacamole.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGuacamoleCore_RegistersOptionsAndMetadataFactory()
    {
        using var provider = new ServiceCollection()
            .AddGuacamoleCore(options => options.ProviderOptions.DefaultTags = ImmutableArray.Create("app"))
            .BuildServiceProvider();

        var options = provider.GetRequiredService<GuacamoleCoreOptions>();
        var factory = provider.GetRequiredService<IProviderMetadataFactory>();

        Assert.Equal(new[] { "app" }, options.ProviderOptions.DefaultTags);
        Assert.Equal(new[] { "app" }, factory.Create("parse").Tags);
        Assert.Equal(new[] { "app", "x" }, factory.Create("parse", ImmutableArray.Create("x")).Tags);
    }
}
