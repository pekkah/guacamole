using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Guacamole;

/// <summary>
/// Extension methods for registering Guacamole services with Microsoft.Extensions.DependencyInjection.
/// </summary>
/// <remarks>
/// Computing contexts are not configured through dependency injection: each context owns its
/// computation graph and value cache, and is registered like any other service (for example
/// <c>services.AddScoped&lt;MyContext&gt;()</c>). To change a context's cache size, override
/// <see cref="ComputingContextBase.CacheCapacity"/>; to trim unused pipeline nodes automatically,
/// override <see cref="ComputingContextBase.AutoTrimInterval"/>. Metrics are configured globally with
/// <see cref="Diagnostics.MetricsConfiguration.SetGlobal"/>.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Guacamole services to the service collection with default configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>Service collection for fluent chaining</returns>
    public static IServiceCollection AddGuacamole(this IServiceCollection services)
    {
        return services.AddGuacamole(_ => { });
    }

    /// <summary>
    /// Adds Guacamole services to the service collection: registers the configured
    /// <see cref="GuacamoleOptions"/> as a singleton (unless one is already registered).
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Service collection for fluent chaining</returns>
    public static IServiceCollection AddGuacamole(
        this IServiceCollection services,
        Action<GuacamoleOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GuacamoleOptions();
        configure(options);

        services.TryAddSingleton(options);

        return services;
    }
}

/// <summary>
/// Application-wide configuration for Guacamole services registered with
/// <see cref="ServiceCollectionExtensions.AddGuacamole(IServiceCollection, Action{GuacamoleOptions})"/>.
/// </summary>
/// <remarks>
/// There are currently no application-wide settings: per-context settings are virtual members of
/// <see cref="ComputingContextBase"/> and metrics are configured with
/// <see cref="Diagnostics.MetricsConfiguration"/>.
/// </remarks>
public sealed class GuacamoleOptions
{
}
