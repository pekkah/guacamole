using Microsoft.Extensions.DependencyInjection;
using System;

namespace Guacamole.Extensions;

/// <summary>
/// Extension methods for registering computing contexts with dependency injection.
/// Provides fluent API for configuring context lifecycle and dependencies.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds a computing context to the service collection with scoped lifetime.
    /// </summary>
    /// <typeparam name="TContext">The computing context type (must inherit from ComputingContextBase)</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddComputingContext<TContext>(this IServiceCollection services)
        where TContext : ComputingContextBase
    {
        return AddComputingContext<TContext>(services, ServiceLifetime.Scoped);
    }

    /// <summary>
    /// Adds a computing context to the service collection with specified lifetime.
    /// </summary>
    /// <typeparam name="TContext">The computing context type (must inherit from ComputingContextBase)</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="lifetime">The service lifetime (Scoped or Singleton)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddComputingContext<TContext>(
        this IServiceCollection services,
        ServiceLifetime lifetime)
        where TContext : ComputingContextBase
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // Validate lifetime (only Scoped or Singleton allowed for thread-safety guarantees)
        if (lifetime != ServiceLifetime.Scoped && lifetime != ServiceLifetime.Singleton)
        {
            throw new ArgumentException(
                "Computing contexts must be registered as Scoped or Singleton for thread-safety guarantees. " +
                "Transient lifetime is not supported.",
                nameof(lifetime));
        }

        // Register the context with the specified lifetime
        services.Add(new ServiceDescriptor(typeof(TContext), typeof(TContext), lifetime));

        return services;
    }

    /// <summary>
    /// Adds a computing context to the service collection with a factory function.
    /// </summary>
    /// <typeparam name="TContext">The computing context type (must inherit from ComputingContextBase)</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="implementationFactory">Factory function to create the context</param>
    /// <param name="lifetime">The service lifetime (Scoped or Singleton)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddComputingContext<TContext>(
        this IServiceCollection services,
        Func<IServiceProvider, TContext> implementationFactory,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TContext : ComputingContextBase
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (implementationFactory == null)
            throw new ArgumentNullException(nameof(implementationFactory));

        // Validate lifetime
        if (lifetime != ServiceLifetime.Scoped && lifetime != ServiceLifetime.Singleton)
        {
            throw new ArgumentException(
                "Computing contexts must be registered as Scoped or Singleton for thread-safety guarantees.",
                nameof(lifetime));
        }

        // Register with factory
        services.Add(new ServiceDescriptor(typeof(TContext), implementationFactory, lifetime));

        return services;
    }
}

/// <summary>
/// Extension methods for retrieving computing contexts from service providers.
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// Gets a computing context from the service provider.
    /// Throws if the context is not registered.
    /// </summary>
    /// <typeparam name="TContext">The computing context type</typeparam>
    /// <param name="serviceProvider">The service provider</param>
    /// <returns>The computing context instance</returns>
    /// <exception cref="InvalidOperationException">Thrown when the context is not registered</exception>
    public static TContext GetComputingContext<TContext>(this IServiceProvider serviceProvider)
        where TContext : ComputingContextBase
    {
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));

        return serviceProvider.GetRequiredService<TContext>();
    }

    /// <summary>
    /// Tries to get a computing context from the service provider.
    /// Returns null if the context is not registered.
    /// </summary>
    /// <typeparam name="TContext">The computing context type</typeparam>
    /// <param name="serviceProvider">The service provider</param>
    /// <returns>The computing context instance, or null if not registered</returns>
    public static TContext? TryGetComputingContext<TContext>(this IServiceProvider serviceProvider)
        where TContext : ComputingContextBase
    {
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));

        return serviceProvider.GetService<TContext>();
    }
}
