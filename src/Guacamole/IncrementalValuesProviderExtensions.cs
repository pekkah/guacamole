using System.Collections.Immutable;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;

namespace Guacamole;

// NodeIdGenerator is defined in IncrementalValueProviderExtensions.cs and shared across both files

/// <summary>
/// Extension methods for IncrementalValuesProvider transformations and collection operations.
/// Provides functional programming style transformations for multiple values with automatic caching.
/// </summary>
public static class IncrementalValuesProviderExtensions
{
    /// <summary>
    /// Transforms each value and flattens the results.
    /// Creates a new provider that depends on the source and produces flattened results.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Transformation function that produces multiple results</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with flattened transformed values</returns>
    public static IncrementalValuesProvider<TResult> SelectMany<TSource, TResult>(
        this IncrementalValuesProvider<TSource> source,
        Func<TSource, IEnumerable<TResult>> selector,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var selectManyNode = new SelectManyValuesNode<TSource, TResult>(source.Node, selector, null);

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<TResult>(selectManyNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Transforms each value and flattens the results with custom equality comparison.
    /// Creates a new provider that depends on the source and produces flattened results.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Transformation function that produces multiple results</param>
    /// <param name="comparer">Custom equality comparer for result values</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with flattened transformed values</returns>
    public static IncrementalValuesProvider<TResult> SelectMany<TSource, TResult>(
        this IncrementalValuesProvider<TSource> source,
        Func<TSource, IEnumerable<TResult>> selector,
        IEqualityComparer<TResult> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(comparer);

        var selectManyNode = new SelectManyValuesNode<TSource, TResult>(source.Node, selector, comparer);

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<TResult>(selectManyNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Collects all values into a single immutable array.
    /// Creates a value provider that depends on all values in the source.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="source">Source values provider</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A value provider with all values collected</returns>
    public static IncrementalValueProvider<ImmutableArray<T>> Collect<T>(
        this IncrementalValuesProvider<T> source,
        ProviderMetadata metadata = default)
    {
        var collectNode = new CollectValuesNode<T>(source.Node);

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValueProvider<ImmutableArray<T>>(collectNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Transforms each value using the specified function.
    /// Creates a new values provider with transformed values.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Transformation function</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with transformed values</returns>
    public static IncrementalValuesProvider<TResult> Select<TSource, TResult>(
        this IncrementalValuesProvider<TSource> source,
        Func<TSource, TResult> selector,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var selectNode = new SelectValuesNode<TSource, TResult>(source.Node, selector, null);

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<TResult>(selectNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Transforms each value using the specified function with custom equality comparison.
    /// Creates a new values provider with transformed values.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Transformation function</param>
    /// <param name="comparer">Custom equality comparer for result values</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with transformed values</returns>
    public static IncrementalValuesProvider<TResult> Select<TSource, TResult>(
        this IncrementalValuesProvider<TSource> source,
        Func<TSource, TResult> selector,
        IEqualityComparer<TResult> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(comparer);

        var selectNode = new SelectValuesNode<TSource, TResult>(source.Node, selector, comparer);

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<TResult>(selectNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Filters values based on a predicate.
    /// Creates a new values provider with filtered values.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with filtered values</returns>
    public static IncrementalValuesProvider<T> Where<T>(
        this IncrementalValuesProvider<T> source,
        Func<T, bool> predicate,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var whereNode = new WhereValuesNode<T>(source.Node, predicate, null);

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<T>(whereNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Filters values based on a predicate with custom equality comparison.
    /// Creates a new values provider with filtered values.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="comparer">Custom equality comparer for values</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with filtered values</returns>
    public static IncrementalValuesProvider<T> Where<T>(
        this IncrementalValuesProvider<T> source,
        Func<T, bool> predicate,
        IEqualityComparer<T> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(comparer);

        var whereNode = new WhereValuesNode<T>(source.Node, predicate, comparer);

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<T>(whereNode, source.Graph, finalMetadata);
    }
}

// Supporting node implementations for extensions
internal sealed class SelectManyValuesNode<TSource, TResult> : ICollectionComputationNode<TResult>
{
    private readonly ICollectionComputationNode<TSource> _source;
    private readonly Func<TSource, IEnumerable<TResult>> _selector;
    private readonly IEqualityComparer<TResult>? _comparer;

    public SelectManyValuesNode(ICollectionComputationNode<TSource> source, Func<TSource, IEnumerable<TResult>> selector, IEqualityComparer<TResult>? comparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _comparer = comparer;
        Id = NodeIdGenerator.Generate("SelectMany", _source.Id);
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(IEnumerable<TResult>);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<TResult> Comparer => _comparer ?? EqualityComparer<TResult>.Default;

    public Result<IEnumerable<TResult>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is IEnumerable<TResult> cached)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = _source.Compute(context);
            if (!sourceResult.IsSuccess)
                return Result<IEnumerable<TResult>, ComputationError>.Error(sourceResult.ErrorValue!);

            var results = sourceResult.Value!.SelectMany(_selector);
            return Result<IEnumerable<TResult>, ComputationError>.Success(results);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class CollectValuesNode<T> : IComputationNode<ImmutableArray<T>>
{
    private readonly ICollectionComputationNode<T> _source;

    public CollectValuesNode(ICollectionComputationNode<T> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Id = NodeIdGenerator.Generate("Collect", _source.Id);
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(ImmutableArray<T>);
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<ImmutableArray<T>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is ImmutableArray<T> cached)
        {
            return Result<ImmutableArray<T>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = _source.Compute(context);
            if (!sourceResult.IsSuccess)
                return Result<ImmutableArray<T>, ComputationError>.Error(sourceResult.ErrorValue!);

            var collected = sourceResult.Value!.ToImmutableArray();
            return Result<ImmutableArray<T>, ComputationError>.Success(collected);
        }
        catch (Exception ex)
        {
            return Result<ImmutableArray<T>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class SelectValuesNode<TSource, TResult> : ICollectionComputationNode<TResult>
{
    private readonly ICollectionComputationNode<TSource> _source;
    private readonly Func<TSource, TResult> _selector;
    private readonly IEqualityComparer<TResult>? _comparer;

    public SelectValuesNode(ICollectionComputationNode<TSource> source, Func<TSource, TResult> selector, IEqualityComparer<TResult>? comparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _comparer = comparer;
        Id = NodeIdGenerator.Generate("SelectValues", _source.Id);
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(IEnumerable<TResult>);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<TResult> Comparer => _comparer ?? EqualityComparer<TResult>.Default;

    public Result<IEnumerable<TResult>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is IEnumerable<TResult> cached)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = _source.Compute(context);
            if (!sourceResult.IsSuccess)
                return Result<IEnumerable<TResult>, ComputationError>.Error(sourceResult.ErrorValue!);

            var results = sourceResult.Value!.Select(_selector);
            return Result<IEnumerable<TResult>, ComputationError>.Success(results);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class WhereValuesNode<T> : ICollectionComputationNode<T>
{
    private readonly ICollectionComputationNode<T> _source;
    private readonly Func<T, bool> _predicate;
    private readonly IEqualityComparer<T>? _comparer;

    public WhereValuesNode(ICollectionComputationNode<T> source, Func<T, bool> predicate, IEqualityComparer<T>? comparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _comparer = comparer;
        Id = NodeIdGenerator.Generate("WhereValues", _source.Id);
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(IEnumerable<T>);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<T> Comparer => _comparer ?? EqualityComparer<T>.Default;

    public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is IEnumerable<T> cached)
        {
            return Result<IEnumerable<T>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = _source.Compute(context);
            if (!sourceResult.IsSuccess)
                return sourceResult;

            var filtered = sourceResult.Value!.Where(_predicate);
            return Result<IEnumerable<T>, ComputationError>.Success(filtered);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<T>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}
