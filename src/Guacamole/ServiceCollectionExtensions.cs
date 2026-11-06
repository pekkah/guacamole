using System.Collections.Immutable;
using Guacamole.Caching;
using Guacamole.Pipeline;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Guacamole;

/// <summary>
/// Extension methods for registering Guacamole core services with Microsoft.Extensions.DependencyInjection.
/// Provides fluent configuration of incremental computation pipeline with proper lifetime management.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Guacamole core services to the service collection with default configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>Service collection for fluent chaining</returns>
    public static IServiceCollection AddGuacamoleCore(this IServiceCollection services)
    {
        return services.AddGuacamoleCore(options => { });
    }

    /// <summary>
    /// Adds Guacamole core services to the service collection with custom configuration.
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

        // Register core configuration
        services.TryAddSingleton(options);

        // Register caching services
        services.AddGuacamoleCache(options.CacheOptions);

        // Register computation pipeline services
        services.AddGuacamolePipeline(options.PipelineOptions);

        // Register provider factory services
        services.AddGuacamoleProviders(options.ProviderOptions);

        return services;
    }

    /// <summary>
    /// Adds caching services to the service collection.
    /// </summary>
    internal static IServiceCollection AddGuacamoleCache(
        this IServiceCollection services,
        CacheOptions options)
    {
        // Register LRU cache as singleton - create once and reuse the same instance
        // NOTE: The cache instance is shared across all graphs. Each ComputationGraph
        // gets its own instance via scoped registration in AddGuacamolePipeline.
        services.TryAddSingleton<ICache<string, object>>(serviceProvider =>
        {
            var cache = new LRUCache<string, object>(options.DefaultCapacity);
            return cache;
        });

        // Register cache statistics provider
        services.TryAddSingleton<ICacheStatisticsProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ICache<string, object>>() as ICacheStatisticsProvider
            ?? throw new InvalidOperationException("Cache implementation must support statistics"));

        return services;
    }

    /// <summary>
    /// Adds computation pipeline services to the service collection.
    /// </summary>
    internal static IServiceCollection AddGuacamolePipeline(
        this IServiceCollection services,
        PipelineOptions options)
    {
        // Register computation graph as scoped to avoid shared mutable state across contexts
        // Each scoped service gets its own graph instance
        // NOTE: The graph creates its own cache internally with default capacity
        services.TryAddScoped<ComputationGraph>();

        // Register computation context factory
        services.TryAddScoped<IComputationContextFactory, ComputationContextFactory>();

        return services;
    }

    /// <summary>
    /// Adds provider factory services to the service collection.
    /// </summary>
    internal static IServiceCollection AddGuacamoleProviders(
        this IServiceCollection services,
        ProviderOptions options)
    {
        // Register provider factories
        services.TryAddScoped<IIncrementalValueProviderFactory, IncrementalValueProviderFactory>();
        services.TryAddScoped<IIncrementalValuesProviderFactory, IncrementalValuesProviderFactory>();

        // Register provider metadata factory
        services.TryAddSingleton<IProviderMetadataFactory, ProviderMetadataFactory>();

        return services;
    }
}

/// <summary>
/// Configuration options for Guacamole core services.
/// </summary>
public sealed class GuacamoleCoreOptions
{
    /// <summary>
    /// Cache configuration options.
    /// </summary>
    public CacheOptions CacheOptions { get; set; } = new();

    /// <summary>
    /// Pipeline configuration options.
    /// </summary>
    public PipelineOptions PipelineOptions { get; set; } = new();

    /// <summary>
    /// Provider configuration options.
    /// </summary>
    public ProviderOptions ProviderOptions { get; set; } = new();
}

/// <summary>
/// Configuration options for caching services.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// Default cache capacity for LRU cache.
    /// </summary>
    public int DefaultCapacity { get; set; } = 1000;

    /// <summary>
    /// Enable cache statistics tracking.
    /// </summary>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// Cache cleanup interval in milliseconds.
    /// </summary>
    public int CleanupIntervalMs { get; set; } = 60000; // 1 minute
}

/// <summary>
/// Configuration options for computation pipeline.
/// </summary>
public sealed class PipelineOptions
{
    /// <summary>
    /// Maximum depth for dependency resolution to prevent infinite loops.
    /// </summary>
    public int MaxDependencyDepth { get; set; } = 100;

    /// <summary>
    /// Enable performance metrics collection.
    /// </summary>
    public bool EnablePerformanceMetrics { get; set; } = true;

    /// <summary>
    /// Timeout for individual computations in milliseconds.
    /// </summary>
    public int ComputationTimeoutMs { get; set; } = 30000; // 30 seconds
}

/// <summary>
/// Configuration options for provider services.
/// </summary>
public sealed class ProviderOptions
{
    /// <summary>
    /// Enable automatic provider metadata tracking.
    /// </summary>
    public bool EnableMetadataTracking { get; set; } = true;

    /// <summary>
    /// Default tags to apply to all providers.
    /// </summary>
    public ImmutableArray<string> DefaultTags { get; set; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Enable provider performance profiling.
    /// </summary>
    public bool EnableProfiling { get; set; } = false;
}

// Supporting interfaces and implementations for service registration

/// <summary>
/// Factory for creating computation contexts.
/// </summary>
public interface IComputationContextFactory
{
    /// <summary>
    /// Creates a computation context with the specified cached values.
    /// </summary>
    ComputationContext Create(IReadOnlyDictionary<string, object> cachedValues, CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating IncrementalValueProvider instances.
/// </summary>
public interface IIncrementalValueProviderFactory
{
    /// <summary>
    /// Creates an IncrementalValueProvider with the specified node and metadata.
    /// </summary>
    IncrementalValueProvider<T> Create<T>(IComputationNode<T> node, ProviderMetadata metadata = default);
}

/// <summary>
/// Factory for creating IncrementalValuesProvider instances.
/// </summary>
public interface IIncrementalValuesProviderFactory
{
    /// <summary>
    /// Creates an IncrementalValuesProvider with the specified node and metadata.
    /// </summary>
    IncrementalValuesProvider<T> Create<T>(ICollectionComputationNode<T> node, ProviderMetadata metadata = default);
}

/// <summary>
/// Factory for creating provider metadata.
/// </summary>
public interface IProviderMetadataFactory
{
    /// <summary>
    /// Creates provider metadata with automatic caller information.
    /// </summary>
    ProviderMetadata Create(string trackingName);

    /// <summary>
    /// Creates provider metadata with custom configuration.
    /// </summary>
    ProviderMetadata Create(string trackingName, ImmutableArray<string> tags, string? pipelineStage = null);
}


// Concrete implementations

internal sealed class ComputationContextFactory : IComputationContextFactory
{
    public ComputationContext Create(IReadOnlyDictionary<string, object> cachedValues, CancellationToken cancellationToken = default)
    {
        return new ComputationContext(cachedValues, cancellationToken);
    }
}

internal sealed class IncrementalValueProviderFactory : IIncrementalValueProviderFactory
{
    private readonly ComputationGraph _graph;
    private readonly ProviderOptions _options;

    public IncrementalValueProviderFactory(ComputationGraph graph, GuacamoleCoreOptions coreOptions)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _options = coreOptions?.ProviderOptions ?? throw new ArgumentNullException(nameof(coreOptions));
    }

    public IncrementalValueProvider<T> Create<T>(IComputationNode<T> node, ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Apply default tags if metadata tracking is enabled
        if (_options.EnableMetadataTracking && !_options.DefaultTags.IsEmpty)
        {
            metadata = metadata.WithTags(_options.DefaultTags.ToArray());
        }

        return new IncrementalValueProvider<T>(node, _graph, metadata);
    }
}

internal sealed class IncrementalValuesProviderFactory : IIncrementalValuesProviderFactory
{
    private readonly ComputationGraph _graph;
    private readonly ProviderOptions _options;

    public IncrementalValuesProviderFactory(ComputationGraph graph, GuacamoleCoreOptions coreOptions)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _options = coreOptions?.ProviderOptions ?? throw new ArgumentNullException(nameof(coreOptions));
    }

    public IncrementalValuesProvider<T> Create<T>(ICollectionComputationNode<T> node, ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Apply default tags if metadata tracking is enabled
        if (_options.EnableMetadataTracking && !_options.DefaultTags.IsEmpty)
        {
            metadata = metadata.WithTags(_options.DefaultTags.ToArray());
        }

        return new IncrementalValuesProvider<T>(node, _graph, metadata);
    }
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
