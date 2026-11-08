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
    private static IServiceCollection AddGuacamole(IServiceCollection services) => services.AddGuacamole();

    private static IServiceCollection AddGuacamole(IServiceCollection services, Action<GuacamoleOptions> configure) =>
        services.AddGuacamole(configure);

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
    public void DefaultRegistration_ProvidesSingletonOptions()
    {
        using var provider = AddGuacamole(new ServiceCollection()).BuildServiceProvider();

        var options = provider.GetRequiredService<GuacamoleOptions>();
        Assert.Same(options, provider.GetRequiredService<GuacamoleOptions>());
    }

    [Fact]
    public void RegisteringTwice_KeepsTheFirstConfiguration()
    {
        var services = new ServiceCollection();
        GuacamoleOptions? first = null;
        AddGuacamole(services, o => first = o);
        AddGuacamole(services, _ => { });
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(GuacamoleOptions));
        Assert.Same(first, provider.GetRequiredService<GuacamoleOptions>());
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
}
