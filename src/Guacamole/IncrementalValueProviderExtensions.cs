using System.Collections.Immutable;
using System.Globalization;
using Guacamole.Pipeline;

namespace Guacamole;

/// <summary>
/// Generates unique node IDs for nodes created by pipeline operators.
/// </summary>
/// <remarks>
/// Every distinct operator call (different selector, predicate, comparer or argument
/// order) creates a distinct node, so each node must get its own cache slot. IDs are
/// therefore unique per node instance rather than derived from the operation name and
/// parent IDs. Identical repeated calls reuse one node via
/// <see cref="ComputationGraph.GetOrCreateNode{TNode}"/>, so they also share its ID.
/// </remarks>
internal static class NodeIdGenerator
{
    private static long _counter;

    /// <summary>
    /// Generates a process-wide unique ID for a new node of the given operation.
    /// </summary>
    public static string Generate(string operation)
    {
        var id = Interlocked.Increment(ref _counter);
        return operation + "_" + id.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Extension methods for IncrementalValueProvider transformations and pipeline operations.
/// Provides functional programming style transformations with automatic caching and dependency tracking.
/// </summary>
public static class IncrementalValueProviderExtensions
{
    /// <summary>
    /// Transforms the value using the specified function.
    /// Creates a new provider in the pipeline with automatic dependency tracking.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Transformation function</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with the transformed value</returns>
    public static IncrementalValueProvider<TResult> Select<TSource, TResult>(
        this IncrementalValueProvider<TSource> source,
        Func<TSource, TResult> selector,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var transformNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectValueNode<TSource, TResult>), [source.Node.Id], selector, null),
            () => new SelectValueNode<TSource, TResult>(source.Node, selector, null));

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValueProvider<TResult>(transformNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Transforms the value using the specified function with custom equality comparison.
    /// Creates a new provider in the pipeline with automatic dependency tracking.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Transformation function</param>
    /// <param name="comparer">Custom equality comparer for result values</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with the transformed value</returns>
    public static IncrementalValueProvider<TResult> Select<TSource, TResult>(
        this IncrementalValueProvider<TSource> source,
        Func<TSource, TResult> selector,
        IEqualityComparer<TResult> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(comparer);

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var transformNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectValueNode<TSource, TResult>), [source.Node.Id], selector, comparer),
            () => new SelectValueNode<TSource, TResult>(source.Node, selector, comparer));

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValueProvider<TResult>(transformNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Filters the value based on a predicate.
    /// Creates a provider that may or may not produce a value.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with the filtered value</returns>
    public static IncrementalValueProvider<T> Where<T>(
        this IncrementalValueProvider<T> source,
        Func<T, bool> predicate,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var filterNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(WhereValueNode<T>), [source.Node.Id], predicate, null),
            () => new WhereValueNode<T>(source.Node, predicate, null));

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValueProvider<T>(filterNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Filters the value based on a predicate with custom equality comparison.
    /// Creates a provider that may or may not produce a value.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="comparer">Custom equality comparer for values</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with the filtered value</returns>
    public static IncrementalValueProvider<T> Where<T>(
        this IncrementalValueProvider<T> source,
        Func<T, bool> predicate,
        IEqualityComparer<T> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(comparer);

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var filterNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(WhereValueNode<T>), [source.Node.Id], predicate, comparer),
            () => new WhereValueNode<T>(source.Node, predicate, comparer));

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValueProvider<T>(filterNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Combines two providers into a tuple.
    /// Creates a provider that depends on both input providers.
    /// </summary>
    /// <typeparam name="TFirst">First value type</typeparam>
    /// <typeparam name="TSecond">Second value type</typeparam>
    /// <param name="first">First provider</param>
    /// <param name="second">Second provider</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new provider with the combined tuple</returns>
    public static IncrementalValueProvider<(TFirst, TSecond)> Combine<TFirst, TSecond>(
        this IncrementalValueProvider<TFirst> first,
        IncrementalValueProvider<TSecond> second,
        ProviderMetadata metadata = default)
    {
        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var combineNode = first.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(CombineValueNode<TFirst, TSecond>), [first.Node.Id, second.Node.Id]),
            () => new CombineValueNode<TFirst, TSecond>(first.Node, second.Node));

        // Preserve metadata from first provider if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? first.Metadata
            : metadata;

        return new IncrementalValueProvider<(TFirst, TSecond)>(combineNode, first.Graph, finalMetadata);
    }

    /// <summary>
    /// Transforms a single value into multiple values and returns them as a values provider.
    /// Similar to Roslyn's IncrementalValueProvider.SelectMany.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Function that transforms a single value into multiple results</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new values provider with the flattened results</returns>
    public static IncrementalValuesProvider<TResult> SelectMany<TSource, TResult>(
        this IncrementalValueProvider<TSource> source,
        Func<TSource, IEnumerable<TResult>> selector,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var selectManyNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectManyFromValueNode<TSource, TResult>), [source.Node.Id], selector, null),
            () => new SelectManyFromValueNode<TSource, TResult>(source.Node, selector, null));

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<TResult>(selectManyNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Transforms a single value into multiple values with custom equality comparison.
    /// Similar to Roslyn's IncrementalValueProvider.SelectMany.
    /// </summary>
    /// <typeparam name="TSource">Source value type</typeparam>
    /// <typeparam name="TResult">Result value type</typeparam>
    /// <param name="source">Source provider</param>
    /// <param name="selector">Function that transforms a single value into multiple results</param>
    /// <param name="comparer">Custom equality comparer for result values</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new values provider with the flattened results</returns>
    public static IncrementalValuesProvider<TResult> SelectMany<TSource, TResult>(
        this IncrementalValueProvider<TSource> source,
        Func<TSource, IEnumerable<TResult>> selector,
        IEqualityComparer<TResult> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(comparer);

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var selectManyNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectManyFromValueNode<TSource, TResult>), [source.Node.Id], selector, comparer),
            () => new SelectManyFromValueNode<TSource, TResult>(source.Node, selector, comparer));

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<TResult>(selectManyNode, source.Graph, finalMetadata);
    }
}

// Supporting node implementations for extensions
internal sealed class SelectValueNode<TSource, TResult> : IComputationNode<TResult>
{
    private readonly IComputationNode<TSource> _source;
    private readonly Func<TSource, TResult> _selector;
    private readonly IEqualityComparer<TResult>? _comparer;

    public SelectValueNode(IComputationNode<TSource> source, Func<TSource, TResult> selector, IEqualityComparer<TResult>? comparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _comparer = comparer;
        Id = NodeIdGenerator.Generate("Select");
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(TResult);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<TResult> Comparer => _comparer ?? EqualityComparer<TResult>.Default;

    public Result<TResult, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is TResult cached)
        {
            return Result<TResult, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = _source.Compute(context);
            if (!sourceResult.IsSuccess)
                return Result<TResult, ComputationError>.Error(sourceResult.ErrorValue!);

            var result = _selector(sourceResult.Value!);
            return Result<TResult, ComputationError>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<TResult, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class WhereValueNode<T> : IComputationNode<T>
{
    private readonly IComputationNode<T> _source;
    private readonly Func<T, bool> _predicate;
    private readonly IEqualityComparer<T>? _comparer;

    public WhereValueNode(IComputationNode<T> source, Func<T, bool> predicate, IEqualityComparer<T>? comparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _comparer = comparer;
        Id = NodeIdGenerator.Generate("Where");
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(T);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<T> Comparer => _comparer ?? EqualityComparer<T>.Default;

    public Result<T, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is T cached)
        {
            return Result<T, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = _source.Compute(context);
            if (!sourceResult.IsSuccess)
                return sourceResult;

            if (_predicate(sourceResult.Value!))
                return sourceResult;
            else
                return Result<T, ComputationError>.Error(new ComputationError("Value filtered out by predicate"));
        }
        catch (Exception ex)
        {
            return Result<T, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class CombineValueNode<TFirst, TSecond> : IComputationNode<(TFirst, TSecond)>
{
    private readonly IComputationNode<TFirst> _first;
    private readonly IComputationNode<TSecond> _second;

    public CombineValueNode(IComputationNode<TFirst> first, IComputationNode<TSecond> second)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
        Id = NodeIdGenerator.Generate("Combine");
        Dependencies = ImmutableHashSet.Create(_first.Id, _second.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof((TFirst, TSecond));
    public ImmutableHashSet<string> Dependencies { get; }

    public Result<(TFirst, TSecond), ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue))
        {
            try
            {
                // Try to cast the cached value back to tuple
                var cached = ((TFirst, TSecond))cachedValue;
                return Result<(TFirst, TSecond), ComputationError>.Success(cached);
            }
            catch
            {
                // If cast fails, recompute
            }
        }

        try
        {
            var firstResult = _first.Compute(context);
            if (!firstResult.IsSuccess)
                return Result<(TFirst, TSecond), ComputationError>.Error(firstResult.ErrorValue!);

            var secondResult = _second.Compute(context);
            if (!secondResult.IsSuccess)
                return Result<(TFirst, TSecond), ComputationError>.Error(secondResult.ErrorValue!);

            return Result<(TFirst, TSecond), ComputationError>.Success((firstResult.Value!, secondResult.Value!));
        }
        catch (Exception ex)
        {
            return Result<(TFirst, TSecond), ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class SelectManyFromValueNode<TSource, TResult> : ICollectionComputationNode<TResult>
{
    private readonly IComputationNode<TSource> _source;
    private readonly Func<TSource, IEnumerable<TResult>> _selector;
    private readonly IEqualityComparer<TResult>? _comparer;

    public SelectManyFromValueNode(IComputationNode<TSource> source, Func<TSource, IEnumerable<TResult>> selector, IEqualityComparer<TResult>? comparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _comparer = comparer;
        Id = NodeIdGenerator.Generate("SelectManyFromValue");
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

            // Materialize the selector result to avoid multiple enumerations when cached
            var results = _selector(sourceResult.Value!);
            var materialized = results as TResult[] ?? results.ToArray();
            return Result<IEnumerable<TResult>, ComputationError>.Success(materialized);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}
