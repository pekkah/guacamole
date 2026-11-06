using System.Collections.Immutable;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Guacamole;

/// <summary>
/// Extension methods for registering Guacamole services with Microsoft.Extensions.DependencyInjection.
/// </summary>
/// <remarks>
/// Computing contexts are not configured through dependency injection: each context owns its
/// computation graph and value cache. To change a context's cache size, override
/// <see cref="ComputingContextBase.CacheCapacity"/>. Metrics are configured globally with
/// <see cref="Diagnostics.MetricsConfiguration.SetGlobal"/>.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Guacamole services to the service collection with default configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>Service collection for fluent chaining</returns>
    public static IServiceCollection AddGuacamoleCore(this IServiceCollection services)
    {
        return services.AddGuacamoleCore(options => { });
    }

    /// <summary>
    /// Adds Guacamole services to the service collection with custom configuration:
    /// the <see cref="GuacamoleCoreOptions"/> singleton and an <see cref="IProviderMetadataFactory"/>
    /// that applies <see cref="ProviderOptions.DefaultTags"/>.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Service collection for fluent chaining</returns>
    public static IServiceCollection AddGuacamoleCore(
        this IServiceCollection services,
        Action<GuacamoleCoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GuacamoleCoreOptions();
        configure(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IProviderMetadataFactory, ProviderMetadataFactory>();

        return services;
    }
}

/// <summary>
/// Configuration options for Guacamole services.
/// </summary>
public sealed class GuacamoleCoreOptions
{
    /// <summary>
    /// Provider configuration options.
    /// </summary>
    public ProviderOptions ProviderOptions { get; set; } = new();
}

/// <summary>
/// Configuration options for provider services.
/// </summary>
public sealed class ProviderOptions
{
    /// <summary>
    /// Tags that <see cref="IProviderMetadataFactory"/> adds to all metadata it creates.
    /// </summary>
    public ImmutableArray<string> DefaultTags { get; set; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Factory for creating provider metadata.
/// </summary>
public interface IProviderMetadataFactory
{
    /// <summary>
    /// Creates provider metadata with the configured default tags.
    /// </summary>
    ProviderMetadata Create(string trackingName);

    /// <summary>
    /// Creates provider metadata with the configured default tags followed by <paramref name="tags"/>.
    /// </summary>
    ProviderMetadata Create(string trackingName, ImmutableArray<string> tags, string? pipelineStage = null);
}

internal sealed class ProviderMetadataFactory : IProviderMetadataFactory
{
    private readonly ProviderOptions _options;

    public ProviderMetadataFactory(GuacamoleCoreOptions coreOptions)
    {
        _options = coreOptions?.ProviderOptions ?? throw new ArgumentNullException(nameof(coreOptions));
    }

    public ProviderMetadata Create(string trackingName)
    {
        var metadata = new ProviderMetadata(TrackingName: trackingName);

        // Apply default tags if configured
        if (!_options.DefaultTags.IsEmpty)
        {
            metadata = metadata.WithTags(_options.DefaultTags.ToArray());
        }

        return metadata;
    }

    public ProviderMetadata Create(string trackingName, ImmutableArray<string> tags, string? pipelineStage = null)
    {
        var allTags = _options.DefaultTags.IsEmpty ? tags : _options.DefaultTags.AddRange(tags);
        return new ProviderMetadata(trackingName, null, allTags, pipelineStage);
    }
}
