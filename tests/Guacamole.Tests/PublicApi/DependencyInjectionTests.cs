using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// Contexts with dependency injection, as documented in the quickstart: Guacamole has no
/// registration helper, contexts are registered like any other service.
/// </summary>
public class DependencyInjectionTests
{
    [Fact]
    public void Contexts_CanBeRegisteredAsOrdinaryServices_AndAreDisposedWithTheirScope()
    {
        var services = new ServiceCollection();
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
