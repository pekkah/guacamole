using Microsoft.Extensions.DependencyInjection;
using Guacamole.Engine;
using Guacamole.Caching;
using Guacamole.Dependencies;

namespace Guacamole.Extensions;

/// <summary>
/// Extension methods for registering Guacamole services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Guacamole incremental query engine services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGuacamole(this IServiceCollection services)
    {
        // Register core services
        services.AddSingleton<IQueryEngine, QueryEngine>();
        services.AddSingleton<ICacheManager, CacheManager>();
        services.AddSingleton<IDependencyTracker, DependencyTracker>();

        return services;
    }
}
