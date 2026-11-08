using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Guacamole.Pipeline;

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
    /// <remarks>
    /// Memoized per item: when the stage is recomputed, the selector runs only for new or changed
    /// items. Items are matched by their own equality (<c>EqualityComparer&lt;T&gt;.Default</c>,
    /// element-wise for <see cref="ImmutableArray{T}"/> items), not by a comparer passed to the
    /// operator: an item equal to an item of the previous computation reuses that item's result
    /// without running the selector. So with loose equality (an <c>Equals</c> that ignores members
    /// the selector reads), an item that differs from a previous one only in ignored members gets the
    /// result computed for the previous one. Make the item's equality cover everything the
    /// lambda reads, or project the items to such a type first. Items must also be immutable: an
    /// item mutated in place still equals itself.
    /// </remarks>
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
        var finalMetadata = metadata == default
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
    /// <remarks>
    /// Memoized per item: when the stage is recomputed, the selector runs only for new or changed
    /// items. Items are matched by their own equality (<c>EqualityComparer&lt;T&gt;.Default</c>,
    /// element-wise for <see cref="ImmutableArray{T}"/> items), not by a comparer passed to the
    /// operator: an item equal to an item of the previous computation reuses that item's result
    /// without running the selector. So with loose equality (an <c>Equals</c> that ignores members
    /// the selector reads), an item that differs from a previous one only in ignored members gets the
    /// result computed for the previous one. Make the item's equality cover everything the
    /// lambda reads, or project the items to such a type first. Items must also be immutable: an
    /// item mutated in place still equals itself.
    /// </remarks>
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
        var finalMetadata = metadata == default
            ? source.Metadata
            : metadata;

        return new IncrementalValuesProvider<TResult>(selectManyNode, source.Graph, finalMetadata);
    }

    /// <summary>
    /// Collects all values into a single immutable array.
    /// Creates a value provider that depends on all values in the source.
    /// </summary>
    /// <remarks>
    /// A recomputed array with the same items in the same order as the previous one (compared with
    /// <see cref="EqualityComparer{T}.Default"/>, or element-wise for items that are themselves
    /// immutable arrays) counts as unchanged: the stages downstream of it
    /// stay cached and output handlers registered on it are not called. Use
    /// <see cref="Collect{T}(IncrementalValuesProvider{T}, IEqualityComparer{T}, ProviderMetadata)"/>
    /// to compare the items with another comparer.
    /// </remarks>
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
            new NodeReuseKey(typeof(CollectValuesNode<T>), [source.Node.Id], (object?)null),
            () => new CollectValuesNode<T>(source.Node, null));

        return CreateCollectProvider(source, collectNode, metadata);
    }

    /// <summary>
    /// Collects all values into a single immutable array, comparing the collected items with a
    /// custom equality comparer.
    /// Creates a value provider that depends on all values in the source.
    /// </summary>
    /// <remarks>
    /// A recomputed array with as many items as the previous one, each equal by
    /// <paramref name="comparer"/> to the item at the same position, counts as unchanged: the
    /// stages downstream of it stay cached and output handlers registered on it are not called.
    /// </remarks>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="source">Source values provider</param>
    /// <param name="comparer">Equality comparer for the collected items</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A value provider with all values collected</returns>
    public static IncrementalValueProvider<ImmutableArray<T>> Collect<T>(
        this IncrementalValuesProvider<T> source,
        IEqualityComparer<T> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        // Reuse the node of an identical earlier call (same inputs, delegates and comparer)
        // so pipelines built repeatedly (e.g. in property getters) share one node and cache.
        var collectNode = source.Graph.GetOrCreateNode(
            new NodeReuseKey(typeof(CollectValuesNode<T>), [source.Node.Id], comparer),
            () => new CollectValuesNode<T>(source.Node, comparer));

        return CreateCollectProvider(source, collectNode, metadata);
    }

    private static IncrementalValueProvider<ImmutableArray<T>> CreateCollectProvider<T>(
        IncrementalValuesProvider<T> source,
        CollectValuesNode<T> collectNode,
        ProviderMetadata metadata)
    {
        // Preserve metadata from source if no metadata is explicitly provided
        var finalMetadata = metadata == default
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
    /// <remarks>
    /// Memoized per item: when the stage is recomputed, the selector runs only for new or changed
    /// items. Items are matched by their own equality (<c>EqualityComparer&lt;T&gt;.Default</c>,
    /// element-wise for <see cref="ImmutableArray{T}"/> items), not by a comparer passed to the
    /// operator: an item equal to an item of the previous computation reuses that item's result
    /// without running the selector. So with loose equality (an <c>Equals</c> that ignores members
    /// the selector reads), an item that differs from a previous one only in ignored members gets the
    /// result computed for the previous one. Make the item's equality cover everything the
    /// lambda reads, or project the items to such a type first. Items must also be immutable: an
    /// item mutated in place still equals itself.
    /// </remarks>
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
        var finalMetadata = metadata == default
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
    /// <remarks>
    /// Memoized per item: when the stage is recomputed, the selector runs only for new or changed
    /// items. Items are matched by their own equality (<c>EqualityComparer&lt;T&gt;.Default</c>,
    /// element-wise for <see cref="ImmutableArray{T}"/> items), not by a comparer passed to the
    /// operator: an item equal to an item of the previous computation reuses that item's result
    /// without running the selector. So with loose equality (an <c>Equals</c> that ignores members
    /// the selector reads), an item that differs from a previous one only in ignored members gets the
    /// result computed for the previous one. Make the item's equality cover everything the
    /// lambda reads, or project the items to such a type first. Items must also be immutable: an
    /// item mutated in place still equals itself.
    /// </remarks>
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
        var finalMetadata = metadata == default
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
    /// <remarks>
    /// Memoized per item: when the stage is recomputed, the predicate runs only for new or changed
    /// items. Items are matched by their own equality (<c>EqualityComparer&lt;T&gt;.Default</c>,
    /// element-wise for <see cref="ImmutableArray{T}"/> items), not by a comparer passed to the
    /// operator: an item equal to an item of the previous computation reuses that item's result
    /// without running the predicate. So with loose equality (an <c>Equals</c> that ignores members
    /// the predicate reads), an item that differs from a previous one only in ignored members gets the
    /// result computed for the previous one. Make the item's equality cover everything the
    /// lambda reads, or project the items to such a type first. Items must also be immutable: an
    /// item mutated in place still equals itself.
    /// </remarks>
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
        var finalMetadata = metadata == default
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
    /// <remarks>
    /// Memoized per item: when the stage is recomputed, the predicate runs only for new or changed
    /// items. Items are matched by their own equality (<c>EqualityComparer&lt;T&gt;.Default</c>,
    /// element-wise for <see cref="ImmutableArray{T}"/> items), not by a comparer passed to the
    /// operator: an item equal to an item of the previous computation reuses that item's result
    /// without running the predicate. So with loose equality (an <c>Equals</c> that ignores members
    /// the predicate reads), an item that differs from a previous one only in ignored members gets the
    /// result computed for the previous one. Make the item's equality cover everything the
    /// lambda reads, or project the items to such a type first. Items must also be immutable: an
    /// item mutated in place still equals itself.
    /// </remarks>
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
        var finalMetadata = metadata == default
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
        var finalMetadata = metadata == default
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
    /// Merges two values providers into a single provider containing all values from both collections,
    /// comparing values with a custom equality comparer.
    /// </summary>
    /// <typeparam name="T">Value type</typeparam>
    /// <param name="first">First values provider</param>
    /// <param name="second">Second values provider</param>
    /// <param name="comparer">Custom equality comparer for values: used to decide whether either input,
    /// and the merged result, changed</param>
    /// <param name="metadata">Optional metadata for the new provider</param>
    /// <returns>A new values provider with all values from both inputs (first, then second)</returns>
    /// <remarks>
    /// Equivalent to
    /// <c>first.Collect(comparer).Combine(second.Collect(comparer)).SelectMany(tuple =&gt; tuple.Item1.Concat(tuple.Item2), comparer)</c>;
    /// see <see cref="Merge{T}(IncrementalValuesProvider{T}, IncrementalValuesProvider{T}, ProviderMetadata)"/>.
    /// Identical calls (same inputs and comparer) reuse their nodes.
    /// </remarks>
    public static IncrementalValuesProvider<T> Merge<T>(
        this IncrementalValuesProvider<T> first,
        IncrementalValuesProvider<T> second,
        IEqualityComparer<T> comparer,
        ProviderMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        var combined = first.Collect(comparer).Combine(second.Collect(comparer));

        return combined.SelectMany(
            tuple => tuple.Item1.Concat(tuple.Item2),
            comparer,
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
    /// Equivalent to <c>source.SelectMany(nested =&gt; nested.SelectMany(inner =&gt; inner))</c>: the inner
    /// arrays are concatenated in order. The source is a single value, so whenever it changes the
    /// whole nested array is flattened again (it is one item; there is no per-inner-array
    /// memoization). When nothing upstream changed, the cached result is reused, and if the source
    /// has no value (a <c>Where</c> upstream rejected it) the result has no items.
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
    /// After collecting per-folder arrays:
    /// <code>
    /// var filesPerFolder = folders.Select(folder => folder.Files); // IncrementalValuesProvider&lt;ImmutableArray&lt;File&gt;&gt;
    /// var batches = filesPerFolder.Collect();
    /// // batches is IncrementalValueProvider&lt;ImmutableArray&lt;ImmutableArray&lt;File&gt;&gt;&gt;
    ///
    /// var allFiles = batches.Flatten();
    /// // allFiles is IncrementalValuesProvider&lt;File&gt;
    /// // (filesPerFolder.Flatten() gives the same items with per-folder memoization)
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
    /// Equivalent to <c>source.SelectMany(array =&gt; array)</c>: the arrays are concatenated in the
    /// order of the source's items. Like any collection <c>SelectMany</c>, it is memoized per item,
    /// so a changed array is flattened again while unchanged arrays reuse their previous results.
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
//
// Select, Where, SelectMany and values.Combine(value) record the delta of each output they derive
// from their previous output and their input's delta, and Collect compares through its input's
// delta (see the design note in Pipeline/ArrayDelta.cs). Each of them counts the computations that
// used a delta (DeltaComputations, for tests).

internal sealed class SelectManyValuesNode<TSource, TResult> : ICollectionComputationNode<TResult>, IHasComparer<TResult>, IDeltaSource<TResult>
{
    private readonly ICollectionComputationNode<TSource> _source;
    private readonly Func<TSource, IEnumerable<TResult>> _selector;
    private readonly IEqualityComparer<TResult>? _comparer;
    private readonly ItemMemo<TSource, TResult[]> _memo = new();

    // The latest output and what it was computed from: the input, the per-item results (the memo's
    // values) and the offset of each item's results in the output (one more entry than items)
    private sealed record State(TSource[] Input, TResult[][] PerItem, int[] Offsets, TResult[] Output);

    private State? _state;
    private ArrayDelta<TResult>? _delta;
    private int _deltaComputations;

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
    public IEqualityComparer<TResult> Comparer => _comparer ?? DefaultEqualityComparer<TResult>.Instance;

    internal int DeltaComputations => Volatile.Read(ref _deltaComputations);

    public ArrayDelta<TResult>? GetDelta(TResult[] from, TResult[] to) =>
        Volatile.Read(ref _delta) is { } delta && delta.Describes(from, to) ? delta : null;

    public Result<IEnumerable<TResult>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is IEnumerable<TResult> cached)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = context.Read(_source);
            if (!sourceResult.IsSuccess)
                return Result<IEnumerable<TResult>, ComputationError>.Error(sourceResult.ErrorValue!);

            // Unchanged items reuse their previous results without running the selector.
            // Materialize inside the try so selector exceptions become error results.
            var input = sourceResult.ValueOrDefault as TSource[] ?? sourceResult.ValueOrDefault!.ToArray();
            var perItem = _memo.Apply(input, item => _selector(item).ToArray(), _source, out var step);

            var state = Volatile.Read(ref _state);
            TResult[] output;
            int[] offsets;
            if (step.InputDelta is { } delta
                && state != null
                && ReferenceEquals(state.Input, step.PreviousItems)
                && ReferenceEquals(state.PerItem, step.PreviousValues))
            {
                Interlocked.Increment(ref _deltaComputations);
                output = ApplyDelta(delta.Edits, state, perItem, out offsets, out var edits);
                if (!ReferenceEquals(output, state.Output))
                    Volatile.Write(ref _delta, new ArrayDelta<TResult>(state.Output, output, edits));
            }
            else
            {
                output = Flatten(perItem, out offsets);
                Volatile.Write(ref _delta, null);
            }

            Volatile.Write(ref _state, new State(input, perItem, offsets, output));
            return Result<IEnumerable<TResult>, ComputationError>.Success(output);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }

    private static TResult[] Flatten(TResult[][] perItem, out int[] offsets)
    {
        offsets = new int[perItem.Length + 1];
        var count = 0;
        for (var i = 0; i < perItem.Length; i++)
        {
            offsets[i] = count;
            count += perItem[i].Length;
        }

        offsets[perItem.Length] = count;
        var flattened = new TResult[count];
        for (var i = 0; i < perItem.Length; i++)
            perItem[i].CopyTo(flattened, offsets[i]);

        return flattened;
    }

    /// <summary>
    /// Derives the output from the previous one and the input's edits: the results of unchanged
    /// items are copied in bulk, and an edited item's results replace the previous ones (pairwise,
    /// then removed or inserted as the number of results changes).
    /// </summary>
    private static TResult[] ApplyDelta(ItemEdit[] inputEdits, State state, TResult[][] perItem, out int[] newOffsets, out ItemEdit[] edits)
    {
        var previousPerItem = state.PerItem;
        var previousOffsets = state.Offsets;
        var previous = state.Output;

        var length = previous.Length;
        foreach (var edit in inputEdits)
        {
            if (edit.From >= 0)
                length -= previousPerItem[edit.From].Length;
            if (edit.To >= 0)
                length += perItem[edit.To].Length;
        }

        var output = new TResult[length];
        var offsets = newOffsets = new int[perItem.Length + 1];
        var outputEdits = new List<ItemEdit>();
        int from = 0, to = 0, written = 0;

        foreach (var edit in inputEdits)
        {
            CopyRun(edit.RunBefore(from, to));

            var oldStart = previousOffsets[from];
            var oldResults = edit.From >= 0 ? previousPerItem[edit.From] : [];
            var newResults = edit.To >= 0 ? perItem[edit.To] : [];
            if (edit.To >= 0)
            {
                offsets[to] = written;
                newResults.CopyTo(output, written);
            }

            if (!ReferenceEquals(oldResults, newResults))
            {
                var common = Math.Min(oldResults.Length, newResults.Length);
                for (var k = 0; k < common; k++)
                {
                    if (!Identity<TResult>.Same(oldResults[k], newResults[k]))
                        outputEdits.Add(new ItemEdit(oldStart + k, written + k));
                }

                for (var k = common; k < oldResults.Length; k++)
                    outputEdits.Add(new ItemEdit(oldStart + k, -1));
                for (var k = common; k < newResults.Length; k++)
                    outputEdits.Add(new ItemEdit(-1, written + k));
            }

            written += newResults.Length;
            if (edit.From >= 0)
                from++;
            if (edit.To >= 0)
                to++;
        }

        CopyRun(previousPerItem.Length - from);
        offsets[perItem.Length] = written;

        edits = [.. outputEdits];
        return edits.Length == 0 && length == previous.Length ? previous : output;

        void CopyRun(int run)
        {
            if (run <= 0)
                return;

            var start = previousOffsets[from];
            var count = previousOffsets[from + run] - start;
            Array.Copy(previous, start, output, written, count);

            var shift = written - start;
            if (shift == 0)
            {
                Array.Copy(previousOffsets, from, offsets, to, run);
            }
            else
            {
                for (var k = 0; k < run; k++)
                    offsets[to + k] = previousOffsets[from + k] + shift;
            }

            written += count;
            from += run;
            to += run;
        }
    }
}

/// <summary>
/// Collects a collection into an <see cref="ImmutableArray{T}"/>. Arrays are compared element-wise,
/// in order, with the item comparer, so a re-collect with equal items keeps the stage's version.
/// </summary>
/// <remarks>
/// The collected array wraps the input's array, so when the input recorded the delta between two
/// of its outputs, comparing the two collected arrays only compares the replaced items
/// (<see cref="TryEquals"/>).
/// </remarks>
internal sealed class CollectValuesNode<T> : IComputationNode<ImmutableArray<T>>, IHasComparer<ImmutableArray<T>>, IDeltaComparable<ImmutableArray<T>>
{
    private readonly ICollectionComputationNode<T> _source;
    private readonly IEqualityComparer<T> _itemComparer;

    public CollectValuesNode(ICollectionComputationNode<T> source, IEqualityComparer<T>? itemComparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        var comparer = itemComparer is null
            ? ImmutableArrayEqualityComparer<T>.Default
            : new ImmutableArrayEqualityComparer<T>(itemComparer);
        Comparer = comparer;
        _itemComparer = comparer.ItemComparer;
        Id = NodeIdGenerator.Generate("Collect");
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(ImmutableArray<T>);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<ImmutableArray<T>> Comparer { get; }

    public Result<ImmutableArray<T>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is ImmutableArray<T> cached)
        {
            return Result<ImmutableArray<T>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = context.Read(_source);
            if (!sourceResult.IsSuccess)
                return Result<ImmutableArray<T>, ComputationError>.Error(sourceResult.ErrorValue!);

            // Arrays read from upstream stages and sources are immutable snapshots: share them
            var collected = sourceResult.ValueOrDefault is T[] array
                ? ImmutableCollectionsMarshal.AsImmutableArray(array)
                : sourceResult.ValueOrDefault!.ToImmutableArray();
            return Result<ImmutableArray<T>, ComputationError>.Success(collected);
        }
        catch (Exception ex)
        {
            return Result<ImmutableArray<T>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }

    public bool TryEquals(ImmutableArray<T> previous, ImmutableArray<T> current, out bool equal)
    {
        var from = ImmutableCollectionsMarshal.AsArray(previous);
        var to = ImmutableCollectionsMarshal.AsArray(current);
        if (from is not null && to is not null && ArrayDelta.Get(_source, from, to) is { } delta)
            return ArrayDelta.TryEquals(delta, from, to, _itemComparer, out equal);

        equal = false;
        return false;
    }
}

internal sealed class SelectValuesNode<TSource, TResult> : ICollectionComputationNode<TResult>, IHasComparer<TResult>, IDeltaSource<TResult>
{
    private readonly ICollectionComputationNode<TSource> _source;
    private readonly Func<TSource, TResult> _selector;
    private readonly IEqualityComparer<TResult>? _comparer;
    private readonly ItemMemo<TSource, TResult> _memo = new();
    private ArrayDelta<TResult>? _delta;
    private int _deltaComputations;

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
    public IEqualityComparer<TResult> Comparer => _comparer ?? DefaultEqualityComparer<TResult>.Instance;

    internal int DeltaComputations => Volatile.Read(ref _deltaComputations);

    public ArrayDelta<TResult>? GetDelta(TResult[] from, TResult[] to) =>
        Volatile.Read(ref _delta) is { } delta && delta.Describes(from, to) ? delta : null;

    public Result<IEnumerable<TResult>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is IEnumerable<TResult> cached)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = context.Read(_source);
            if (!sourceResult.IsSuccess)
                return Result<IEnumerable<TResult>, ComputationError>.Error(sourceResult.ErrorValue!);

            // Unchanged items reuse their previous results without running the selector.
            // Materialize inside the try so selector exceptions become error results.
            var results = _memo.Apply(sourceResult.ValueOrDefault!, _selector, _source, out var step);

            // The results are the memo's values, aligned with the input: their delta is the input's,
            // without the replacements by an identical result
            if (step.InputDelta is { } delta)
            {
                Interlocked.Increment(ref _deltaComputations);
                if (!ReferenceEquals(results, step.PreviousValues))
                {
                    var previous = step.PreviousValues!;
                    var edits = Array.FindAll(delta.Edits, e => e.From < 0 || e.To < 0 || !Identity<TResult>.Same(previous[e.From], results[e.To]));
                    Volatile.Write(ref _delta, new ArrayDelta<TResult>(previous, results, edits));
                }
            }
            else
            {
                Volatile.Write(ref _delta, null);
            }

            return Result<IEnumerable<TResult>, ComputationError>.Success(results);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<TResult>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }
}

internal sealed class WhereValuesNode<T> : ICollectionComputationNode<T>, IHasComparer<T>, IDeltaSource<T>
{
    private readonly ICollectionComputationNode<T> _source;
    private readonly Func<T, bool> _predicate;
    private readonly IEqualityComparer<T>? _comparer;
    private readonly ItemMemo<T, bool> _memo = new();

    // The latest output and what it was computed from: the input and the verdicts (the memo's values)
    private sealed record State(T[] Input, bool[] Keep, T[] Output);

    private State? _state;
    private ArrayDelta<T>? _delta;
    private int _deltaComputations;

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
    public IEqualityComparer<T> Comparer => _comparer ?? DefaultEqualityComparer<T>.Instance;

    internal int DeltaComputations => Volatile.Read(ref _deltaComputations);

    public ArrayDelta<T>? GetDelta(T[] from, T[] to) =>
        Volatile.Read(ref _delta) is { } delta && delta.Describes(from, to) ? delta : null;

    public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is IEnumerable<T> cached)
        {
            return Result<IEnumerable<T>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = context.Read(_source);
            if (!sourceResult.IsSuccess)
                return sourceResult;

            // Unchanged items reuse their previous verdicts without running the predicate.
            // Materialize inside the try so predicate exceptions become error results.
            var items = sourceResult.ValueOrDefault as T[] ?? sourceResult.ValueOrDefault!.ToArray();
            var keep = _memo.Apply(items, _predicate, _source, out var step);

            var state = Volatile.Read(ref _state);
            T[] output;
            if (step.InputDelta is { } delta
                && state != null
                && ReferenceEquals(state.Input, step.PreviousItems)
                && ReferenceEquals(state.Keep, step.PreviousValues))
            {
                Interlocked.Increment(ref _deltaComputations);
                output = ApplyDelta(delta.Edits, state, items, keep, out var edits);
                if (!ReferenceEquals(output, state.Output))
                    Volatile.Write(ref _delta, new ArrayDelta<T>(state.Output, output, edits));
            }
            else
            {
                output = Filter(items, keep);
                Volatile.Write(ref _delta, null);
            }

            Volatile.Write(ref _state, new State(items, keep, output));
            return Result<IEnumerable<T>, ComputationError>.Success(output);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<T>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }

    private static T[] Filter(T[] items, bool[] keep)
    {
        var kept = keep.AsSpan().Count(true);

        // Nothing filtered out: the (immutable) input array is the result
        if (kept == items.Length)
            return items;

        var filtered = new T[kept];
        kept = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (keep[i])
                filtered[kept++] = items[i];
        }

        return filtered;
    }

    /// <summary>
    /// Derives the output from the previous one and the input's edits: the kept items of unchanged
    /// runs are copied in bulk (counting the kept flags of the run), and each edited item is
    /// inserted, removed or replaced in the output according to its old and new verdicts. When
    /// every item is kept, the input array is the output, as in <see cref="Filter"/>.
    /// </summary>
    private static T[] ApplyDelta(ItemEdit[] inputEdits, State state, T[] items, bool[] keep, out ItemEdit[] edits)
    {
        var previousKeep = state.Keep;
        var previous = state.Output;

        var length = previous.Length;
        foreach (var edit in inputEdits)
        {
            if (edit.From >= 0 && previousKeep[edit.From])
                length--;
            if (edit.To >= 0 && keep[edit.To])
                length++;
        }

        // Every item kept: the output is the input, and only the edits need to be worked out
        var allKept = length == items.Length;
        var output = allKept ? items : new T[length];
        var outputEdits = new List<ItemEdit>();
        int from = 0, to = 0, outFrom = 0, outTo = 0;

        foreach (var edit in inputEdits)
        {
            CopyRun(edit.RunBefore(from, to));

            var wasKept = edit.From >= 0 && previousKeep[edit.From];
            var isKept = edit.To >= 0 && keep[edit.To];
            if (isKept && !allKept)
                output[outTo] = items[edit.To];

            if (wasKept && isKept)
            {
                if (!Identity<T>.Same(previous[outFrom], items[edit.To]))
                    outputEdits.Add(new ItemEdit(outFrom, outTo));
                outFrom++;
                outTo++;
            }
            else if (wasKept)
            {
                outputEdits.Add(new ItemEdit(outFrom++, -1));
            }
            else if (isKept)
            {
                outputEdits.Add(new ItemEdit(-1, outTo++));
            }

            if (edit.From >= 0)
                from++;
            if (edit.To >= 0)
                to++;
        }

        CopyRun(previousKeep.Length - from);

        edits = [.. outputEdits];
        return edits.Length == 0 && length == previous.Length ? previous : output;

        void CopyRun(int run)
        {
            if (run <= 0)
                return;

            var kept = previousKeep.AsSpan(from, run).Count(true);
            if (!allKept)
                Array.Copy(previous, outFrom, output, outTo, kept);

            outFrom += kept;
            outTo += kept;
            from += run;
            to += run;
        }
    }
}

internal sealed class CombineValuesWithValueNode<TLeft, TRight> : ICollectionComputationNode<(TLeft, TRight)>, IHasComparer<(TLeft, TRight)>, IDeltaSource<(TLeft, TRight)>
{
    private readonly ICollectionComputationNode<TLeft> _left;
    private readonly IComputationNode<TRight> _right;

    // The latest output and the left items and right value it pairs
    private sealed record State(TLeft[] Left, TRight Right, (TLeft, TRight)[] Output);

    private State? _state;
    private ArrayDelta<(TLeft, TRight)>? _delta;
    private int _deltaComputations;

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
    public IEqualityComparer<(TLeft, TRight)> Comparer => DefaultEqualityComparer<(TLeft, TRight)>.Instance;

    internal int DeltaComputations => Volatile.Read(ref _deltaComputations);

    public ArrayDelta<(TLeft, TRight)>? GetDelta((TLeft, TRight)[] from, (TLeft, TRight)[] to) =>
        Volatile.Read(ref _delta) is { } delta && delta.Describes(from, to) ? delta : null;

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
            var leftResult = context.Read(_left);
            if (!leftResult.IsSuccess)
                return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Error(leftResult.ErrorValue!);

            // Compute right side (single value)
            var rightResult = context.Read(_right);
            if (rightResult.IsError)
                return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Error(rightResult.ErrorValue!);

            // No right value (filtered out by Where) means nothing to pair with
            if (rightResult.IsNone)
            {
                Volatile.Write(ref _state, null);
                Volatile.Write(ref _delta, null);
                return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Success(Array.Empty<(TLeft, TRight)>());
            }

            var left = leftResult.ValueOrDefault as TLeft[] ?? leftResult.ValueOrDefault!.ToArray();
            var right = rightResult.ValueOrDefault!;

            // Only the left items changed since the previous output: pair the edited ones
            var state = Volatile.Read(ref _state);
            (TLeft, TRight)[] combined;
            if (state != null
                && SameRight(state.Right, right)
                && ArrayDelta.Get(_left, state.Left, left) is { } delta)
            {
                Interlocked.Increment(ref _deltaComputations);
                combined = ApplyDelta(delta.Edits, state.Output, left, right);
                Volatile.Write(ref _delta, new ArrayDelta<(TLeft, TRight)>(state.Output, combined, delta.Edits));
            }
            else
            {
                // Combine each left value with the single right value
                combined = new (TLeft, TRight)[left.Length];
                for (var i = 0; i < left.Length; i++)
                    combined[i] = (left[i], right);
                Volatile.Write(ref _delta, null);
            }

            Volatile.Write(ref _state, new State(left, right, combined));
            return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Success(combined);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<(TLeft, TRight)>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }

    // The identical right value (see Identity<T>): a value type whose equality is looser than its
    // contents must not count as identical, or the previous pairs would keep the old right value
    private static bool SameRight(TRight previous, TRight current) =>
        Identity<TRight>.Same(previous, current);

    private static (TLeft, TRight)[] ApplyDelta(ItemEdit[] edits, (TLeft, TRight)[] previous, TLeft[] left, TRight right)
    {
        var output = new (TLeft, TRight)[left.Length];
        int from = 0, to = 0;
        foreach (var edit in edits)
        {
            var run = edit.RunBefore(from, to);
            Array.Copy(previous, from, output, to, run);
            from += run;
            to += run;

            if (edit.To >= 0)
            {
                output[to] = (left[to], right);
                to++;
            }

            if (edit.From >= 0)
                from++;
        }

        Array.Copy(previous, from, output, to, previous.Length - from);
        return output;
    }
}
