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

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var selectManyNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectManyValuesNode<TSource, TResult>), [source.Node.Id], selector, null),
            () => new SelectManyValuesNode<TSource, TResult>(source.Node, selector, null));

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

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var selectManyNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectManyValuesNode<TSource, TResult>), [source.Node.Id], selector, comparer),
            () => new SelectManyValuesNode<TSource, TResult>(source.Node, selector, comparer));

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
        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var collectNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(CollectValuesNode<T>), [source.Node.Id]),
            () => new CollectValuesNode<T>(source.Node));

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

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var selectNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectValuesNode<TSource, TResult>), [source.Node.Id], selector, null),
            () => new SelectValuesNode<TSource, TResult>(source.Node, selector, null));

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

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var selectNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(SelectValuesNode<TSource, TResult>), [source.Node.Id], selector, comparer),
            () => new SelectValuesNode<TSource, TResult>(source.Node, selector, comparer));

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

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var whereNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(WhereValuesNode<T>), [source.Node.Id], predicate, null),
            () => new WhereValuesNode<T>(source.Node, predicate, null));

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

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var whereNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(WhereValuesNode<T>), [source.Node.Id], predicate, comparer),
            () => new WhereValuesNode<T>(source.Node, predicate, comparer));

        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<T>(whereNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Combines each value from the values provider with a single value provider.
    /// Creates a new values provider where each value is paired with the value from the right provider.
    /// Similar to Roslyn's IncrementalValuesProvider.Combine method.
    /// </summary>
    /// <typeparam name="TLeft">Left value type (from the collection)</typeparam>
    /// <typeparam name="TRight">Right value type (single value)</typeparam>
    /// <param name="left">Left values provider (collection)</param>
    /// <param name="right">Right value provider (single value)</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new values provider with combined tuples</returns>
    public static IncrementalValuesProvider<(TLeft Left, TRight Right)> Combine<TLeft, TRight>(
        this IncrementalValuesProvider<TLeft> left,
        IncrementalValueProvider<TRight> right,
        ProviderMetadata metadata = default)
    {
        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var combineNode = left.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(CombineValuesWithValueNode<TLeft, TRight>), [left.Node.Id, right.Node.Id]),
            () => new CombineValuesWithValueNode<TLeft, TRight>(left.Node, right.Node));

        // Preserve metadata from left provider if no metadata is explicitly provided
        var finalMetadata = metadata.TrackingName == null && metadata.CreationLocation == null
            ? left.Metadata
            : metadata;

        return new IncrementalValuesProvider<(TLeft, TRight)>(combineNode, left.Graph, finalMetadata);
    }

    /// <summary>
    /// Merges two values providers into a single provider containing all values from both collections.
    /// This is a convenience method that combines Collect + Combine + SelectMany operations.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="first">First values provider</param>
    /// <param name="second">Second values provider</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new values provider with all values from both inputs</returns>
    /// <remarks>
    /// <para>
    /// This method merges two separate value collections into one. The order is first collection followed by second collection.
    /// </para>
    /// <para>
    /// <strong>Performance Note:</strong> This operation has overhead from collecting both providers into arrays
    /// before merging. Each time either input changes, both collections are re-collected and concatenated.
    /// For frequently changing inputs with large collections, consider alternative architectures.
    /// </para>
    /// <para>
    /// <strong>Implementation:</strong>
    /// Equivalent to: <c>first.Collect().Combine(second.Collect()).SelectMany(tuple => tuple.Item1.Concat(tuple.Item2))</c>
    /// </para>
    /// </remarks>
    /// <example>
    /// Merging diagnostics from multiple sources:
    /// <code>
    /// var syntaxDiagnostics = /* IncrementalValuesProvider&lt;Diagnostic&gt; from syntax analysis */;
    /// var semanticDiagnostics = /* IncrementalValuesProvider&lt;Diagnostic&gt; from semantic analysis */;
    ///
    /// // Merge all diagnostics into a single provider
    /// var allDiagnostics = syntaxDiagnostics.Merge(semanticDiagnostics);
    ///
    /// // Can chain multiple merges
    /// var allIssues = syntaxDiagnostics
    ///     .Merge(semanticDiagnostics)
    ///     .Merge(validationDiagnostics);
    /// </code>
    /// </example>
    public static IncrementalValuesProvider<T> Merge<T>(
        this IncrementalValuesProvider<T> first,
        IncrementalValuesProvider<T> second,
        ProviderMetadata metadata = default)
    {
        // Collect both providers to immutable arrays
        var firstCollected = first.Collect();
        var secondCollected = second.Collect();

        // Combine into a tuple
        var combined = firstCollected.Combine(secondCollected);

        // SelectMany to flatten both arrays into a single collection
        return combined.SelectMany(
            tuple => tuple.Item1.Concat(tuple.Item2),
            metadata);
    }

    /// <summary>
    /// Flattens a nested collection from a single value provider into a flat values provider.
    /// Useful after operations that produce nested arrays, such as grouping or batching.
    /// </summary>
    /// <typeparam name="T">Element type</typeparam>
    /// <param name="source">Source provider containing nested arrays</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new values provider with all nested values flattened into a single collection</returns>
    /// <remarks>
    /// <para>
    /// This is a low-cost operation that uses SelectMany internally. It is fully incremental
    /// and does not require collecting values into intermediate arrays.
    /// </para>
    /// <para>
    /// <strong>Performance:</strong> Efficient and incremental-friendly. When the source changes,
    /// only the affected nested array is re-flattened.
    /// </para>
    /// </remarks>
    /// <example>
    /// Flattening grouped results:
    /// <code>
    /// // After grouping operations that produce nested arrays
    /// var grouped = /* IncrementalValueProvider&lt;ImmutableArray&lt;ImmutableArray&lt;File&gt;&gt;&gt; */;
    ///
    /// // Flatten back to individual files
    /// var allFiles = grouped.Flatten();
    /// // Result: IncrementalValuesProvider&lt;File&gt;
    /// </code>
    ///
    /// Common after Collect operations on already-collected data:
    /// <code>
    /// var batches = filesPerFolder.Select(folder => folder.Files.Collect());
    /// // batches is IncrementalValueProvider&lt;ImmutableArray&lt;ImmutableArray&lt;File&gt;&gt;&gt;
    ///
    /// var allFiles = batches.Flatten();
    /// // allFiles is IncrementalValuesProvider&lt;File&gt;
    /// </code>
    /// </example>
    public static IncrementalValuesProvider<T> Flatten<T>(
        this IncrementalValueProvider<ImmutableArray<ImmutableArray<T>>> source,
        ProviderMetadata metadata = default)
    {
        return source.SelectMany(
            nestedArrays => nestedArrays.SelectMany(innerArray => innerArray),
            metadata);
    }

    /// <summary>
    /// Flattens a values provider where each value is itself a collection.
    /// Transforms a collection of collections into a single flat collection.
    /// </summary>
    /// <typeparam name="T">Element type</typeparam>
    /// <param name="source">Source provider where each value is a collection</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new values provider with all nested collections flattened</returns>
    /// <remarks>
    /// <para>
    /// This is a low-cost operation that uses SelectMany internally. It is fully incremental
    /// and processes each collection independently.
    /// </para>
    /// <para>
    /// <strong>Performance:</strong> Efficient and incremental-friendly. Each nested collection
    /// is flattened independently, so changes to one collection don't affect others.
    /// </para>
    /// </remarks>
    /// <example>
    /// Flattening files from multiple folders:
    /// <code>
    /// var folders = /* IncrementalValuesProvider&lt;Folder&gt; */;
    ///
    /// // Each folder contains an array of files
    /// var filesPerFolder = folders.Select(folder => folder.Files);
    /// // filesPerFolder is IncrementalValuesProvider&lt;ImmutableArray&lt;File&gt;&gt;
    ///
    /// // Flatten to get all files
    /// var allFiles = filesPerFolder.Flatten();
    /// // allFiles is IncrementalValuesProvider&lt;File&gt;
    /// </code>
    ///
    /// Processing nested results:
    /// <code>
    /// var results = /* IncrementalValuesProvider&lt;ImmutableArray&lt;Diagnostic&gt;&gt; */;
    /// var allDiagnostics = results.Flatten();
    /// </code>
    /// </example>
    public static IncrementalValuesProvider<T> Flatten<T>(
        this IncrementalValuesProvider<ImmutableArray<T>> source,
        ProviderMetadata metadata = default)
    {
        return source.SelectMany(
            array => array,
            metadata);
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
        Id = NodeIdGenerator.Generate("SelectMany");
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

            // Materialize inside the try so selector exceptions become error results
            var results = sourceResult.Value!.SelectMany(_selector).ToArray();
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
        Id = NodeIdGenerator.Generate("Collect");
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
        Id = NodeIdGenerator.Generate("SelectValues");
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

            // Materialize inside the try so selector exceptions become error results
            var results = sourceResult.Value!.Select(_selector).ToArray();
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
        Id = NodeIdGenerator.Generate("WhereValues");
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

            // Materialize inside the try so predicate exceptions become error results
            var filtered = sourceResult.Value!.Where(_predicate).ToArray();
            return Result<IEnumerable<T>, ComputationError>.Success(filtered);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<T>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class CombineValuesWithValueNode<TLeft, TRight> : ICollectionComputationNode<(TLeft, TRight)>
{
    private readonly ICollectionComputationNode<TLeft> _left;
    private readonly IComputationNode<TRight> _right;

    public CombineValuesWithValueNode(ICollectionComputationNode<TLeft> left, IComputationNode<TRight> right)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        Id = NodeIdGenerator.Generate("CombineValuesWithValue");
        Dependencies = ImmutableHashSet.Create(_left.Id, _right.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(IEnumerable<(TLeft, TRight)>);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<(TLeft, TRight)> Comparer => EqualityComparer<(TLeft, TRight)>.Default;

    public Result<IEnumerable<(TLeft, TRight)>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is IEnumerable<(TLeft, TRight)> cached)
        {
            return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Success(cached);
        }

        try
        {
            // Compute left side (collection)
            var leftResult = _left.Compute(context);
            if (!leftResult.IsSuccess)
                return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Error(leftResult.ErrorValue!);

            // Compute right side (single value)
            var rightResult = _right.Compute(context);
            if (!rightResult.IsSuccess)
                return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Error(rightResult.ErrorValue!);

            // Combine each left value with the single right value
            // Materialize to avoid multiple enumerations when cached result is reused
            var combined = leftResult.Value!.Select(left => (left, rightResult.Value!)).ToArray();
            return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Success(combined);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}
