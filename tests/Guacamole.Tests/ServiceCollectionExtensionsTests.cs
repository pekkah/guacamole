using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guacamole.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGuacamole_RegistersConfiguredOptions()
    {
        GuacamoleOptions? configured = null;
        using var provider = new ServiceCollection()
            .AddGuacamole(options => configured = options)
            .BuildServiceProvider();

        Assert.Same(configured, provider.GetRequiredService<GuacamoleOptions>());
    }

    [Fact]
    public void AddGuacamole_CalledTwice_KeepsFirstOptions()
    {
        var services = new ServiceCollection().AddGuacamole();
        var first = Assert.Single(services, d => d.ServiceType == typeof(GuacamoleOptions)).ImplementationInstance;

        services.AddGuacamole();

        Assert.Same(first, Assert.Single(services, d => d.ServiceType == typeof(GuacamoleOptions)).ImplementationInstance);
    }
}
