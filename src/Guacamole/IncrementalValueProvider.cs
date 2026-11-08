using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Guacamole.Diagnostics;
using Guacamole.Pipeline;

namespace Guacamole;

/// <summary>
/// Immutable structure representing a single value in a transformation pipeline
/// with automatic caching and incremental computation.
/// Thread-safe without synchronization, supports pipeline transformations via extension methods.
///
/// <para>
/// <b>Equality Requirements:</b> For optimal incremental computation performance,
/// type <typeparamref name="T"/> should implement proper value equality:
/// </para>
/// <list type="bullet">
/// <item><description>Use record types (automatic structural equality)</description></item>
/// <item><description>Use value types with proper Equals() implementation</description></item>
/// <item><description>Implement IEquatable&lt;T&gt; for custom reference types</description></item>
/// <item><description>Override GetHashCode() when overriding Equals()</description></item>
/// </list>
/// <para>
/// Similar to Roslyn's incremental generators, the framework uses EqualityComparer&lt;T&gt;.Default
/// to compare old and new values, unless a comparer is passed to the operator. As an exception,
/// <see cref="System.Collections.Immutable.ImmutableArray{T}"/> results (whose default equality
/// compares the underlying array reference) are compared element-wise, and value tuples of two or
/// three components containing one are compared component-wise. Only when values differ will
/// dependent computations be invalidated and output handlers be triggered.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the value (should implement proper equality for optimal performance)</typeparam>
public readonly record struct IncrementalValueProvider<T>
{
    /// <summary>
    /// Creates a provider for a node of the given graph.
    /// </summary>
    internal IncrementalValueProvider(IComputationNode<T> node, ComputationGraph graph, ProviderMetadata metadata)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Metadata = metadata;
    }

    /// <summary>
    /// Computation node backing this provider (implementation detail).
    /// </summary>
    internal IComputationNode<T> Node { get; }

    /// <summary>
    /// Shared computation graph for dependency tracking (implementation detail).
    /// </summary>
    internal ComputationGraph Graph { get; }

    /// <summary>
    /// Debugging and tracking information.
    /// </summary>
    public ProviderMetadata Metadata { get; }

    /// <summary>
    /// Gets the computed value with automatic caching orchestration and version-based
    /// cache validation. This is the main entry point for retrieving values.
    /// </summary>
    /// <remarks>
    /// Uses version-based lazy validation:
    /// <list type="number">
    /// <item>If no source changed since the cached value was last verified, return it</item>
    /// <item>Otherwise bring each input the cached value was computed from up to date (recursively)
    /// and return the cached value if all of them still have the versions it recorded</item>
    /// <item>Otherwise recompute, compare with the previous value and only increment the
    /// version if the value actually changed</item>
    /// </list>
    /// This enables equality-based cache skipping at every stage: an intermediate stage that
    /// recomputes to an equal value (by its comparer) keeps its version, so the stages downstream
    /// of it stay cached, whether or not the intermediate stage is read itself.
    /// </remarks>
    /// <returns>
    /// Result containing the computed value, an error, or no value
    /// (<see cref="Result{TValue, TError}.IsNone"/>) when a <c>Where</c> upstream rejected its input
    /// </returns>
    /// <exception cref="ObjectDisposedException">The context owning the provider has been disposed
    /// (see <see cref="ComputingContextBase.Dispose"/>).</exception>
    public Result<T, ComputationError> GetValue()
    {
        var graph = Graph;
        if (graph.SharedState.IsDisposed)
            SharedGraphState.ThrowDisposed(Node.Id);

        // Fast path: verified since the last source change
        if (graph.ValueCache.TryGet(Node.Id, out var cached)
            && cached is CachedNodeValue entry
            && entry.VerifiedAtRevision >= graph.SharedState.Revision)
        {
            if (entry.IsNone)
                return Result<T, ComputationError>.None;
            if (entry.Error is { } error)
                return Result<T, ComputationError>.Error(error);
            if (entry.Value is T value)
                return Result<T, ComputationError>.Success(value);
            if (entry.Value is null && default(T) is null)
                return Result<T, ComputationError>.Success(default!);
        }

        return ValueNodeHandle<T>.For(Node, graph).Evaluate(new GraphEvaluator(graph), out _, Metadata);
    }

    /// <summary>
    /// Convenience property to get the computed value directly.
    /// Use <see cref="TryGetValue"/> when the provider may have no value (a <c>Where</c> upstream),
    /// or <see cref="GetValue"/> for Result-based error handling.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The computation failed, or the provider has no value because a <c>Where</c> upstream rejected its input.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The context owning the provider has been disposed.</exception>
    public T Value
    {
        get
        {
            var result = GetValue();
            if (result.IsSuccess)
                return result.ValueOrDefault!;

            throw NoValueException(result);
        }
    }

    /// <summary>
    /// Gets the computed value if there is one.
    /// </summary>
    /// <param name="value">The computed value, or <c>default</c> when there is no value</param>
    /// <returns>
    /// <see langword="true"/> if a value was computed; <see langword="false"/> if the provider has no
    /// value because a <c>Where</c> upstream rejected its input
    /// </returns>
    /// <exception cref="InvalidOperationException">The computation failed.</exception>
    /// <exception cref="ObjectDisposedException">The context owning the provider has been disposed.</exception>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        var result = GetValue();
        if (result.IsSuccess)
        {
            value = result.ValueOrDefault!;
            return true;
        }

        value = default;
        if (result.IsNone)
            return false;

        throw NoValueException(result);
    }

    private static InvalidOperationException NoValueException(Result<T, ComputationError> result) => result.IsNone
        ? new InvalidOperationException(
            "The provider has no value: a Where filter upstream rejected its input. Use TryGetValue() or GetValue() to handle a missing value.")
        : new InvalidOperationException(
            $"Failed to compute value: {result.ErrorValue?.Message ?? "Unknown error"}");
}

/// <summary>
/// Immutable structure representing zero or more values in a transformation pipeline
/// with filtering, collection, and transformation capabilities.
/// Thread-safe collection operations with efficient enumeration and lazy evaluation.
///
/// <para>
/// <b>Equality Requirements:</b> For optimal incremental computation performance,
/// element type <typeparamref name="T"/> should implement proper value equality:
/// </para>
/// <list type="bullet">
/// <item><description>Use record types (automatic structural equality)</description></item>
/// <item><description>Use value types with proper Equals() implementation</description></item>
/// <item><description>Implement IEquatable&lt;T&gt; for custom reference types</description></item>
/// </list>
/// <para>
/// Collection changes are detected item by item with the operator's comparer, by default
/// EqualityComparer&lt;T&gt;.Default (element-wise for <see cref="System.Collections.Immutable.ImmutableArray{T}"/>
/// items, see <see cref="IncrementalValueProvider{T}"/>).
/// Output handlers are only triggered when the collection contents actually change.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the values (should implement proper equality for optimal performance)</typeparam>
public readonly record struct IncrementalValuesProvider<T>
{
    /// <summary>
    /// Creates a provider for a collection node of the given graph.
    /// </summary>
    internal IncrementalValuesProvider(ICollectionComputationNode<T> node, ComputationGraph graph, ProviderMetadata metadata)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Metadata = metadata;
    }

    /// <summary>
    /// Computation node backing this provider (implementation detail).
    /// </summary>
    internal ICollectionComputationNode<T> Node { get; }

    /// <summary>
    /// Shared computation graph for dependency tracking (implementation detail).
    /// </summary>
    internal ComputationGraph Graph { get; }

    /// <summary>
    /// Debugging and tracking information.
    /// </summary>
    public ProviderMetadata Metadata { get; }

    /// <summary>
    /// Gets the computed values with automatic caching orchestration and version-based
    /// cache validation. This is the main entry point for retrieving values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the same version-based lazy validation as <see cref="IncrementalValueProvider{T}.GetValue"/>;
    /// collections are compared element-wise (in order) with the node's comparer.
    /// </para>
    /// <para>
    /// The values are a read-only view (an <see cref="IReadOnlyList{T}"/> with indexed access; casting
    /// it to <see cref="IList{T}"/> and calling a mutator throws <see cref="NotSupportedException"/>) of
    /// the cached result, which the pipeline
    /// shares with downstream stages, <c>Collect()</c> results and source snapshots. It cannot be cast
    /// to an array or modified, stays valid (and unchanged) after later source changes, and reading an
    /// unchanged provider again returns the same view without allocating.
    /// </para>
    /// </remarks>
    /// <returns>Result containing the computed values or error</returns>
    /// <exception cref="ObjectDisposedException">The context owning the provider has been disposed
    /// (see <see cref="ComputingContextBase.Dispose"/>).</exception>
    public Result<IReadOnlyList<T>, ComputationError> GetValues()
    {
        var graph = Graph;
        if (graph.SharedState.IsDisposed)
            SharedGraphState.ThrowDisposed(Node.Id);

        // Fast path: verified since the last source change
        if (graph.ValueCache.TryGet(Node.Id, out var cached)
            && cached is CachedNodeValue entry
            && entry.VerifiedAtRevision >= graph.SharedState.Revision)
        {
            if (entry.IsNone)
                return Result<IReadOnlyList<T>, ComputationError>.None;
            if (entry.Error is { } error)
                return Result<IReadOnlyList<T>, ComputationError>.Error(error);
            if (entry.Value is T[] values)
                return Result<IReadOnlyList<T>, ComputationError>.Success(View(entry, values));
        }

        var result = CollectionNodeHandle<T>.For(Node, graph).Evaluate(new GraphEvaluator(graph), out _, Metadata);
        if (result.IsNone)
            return Result<IReadOnlyList<T>, ComputationError>.None;
        if (result.IsError)
            return Result<IReadOnlyList<T>, ComputationError>.Error(result.ErrorValue!);

        // The handle returns the cached array: hand out the view kept with it
        var computedValues = result.ValueOrDefault!;
        var array = computedValues as T[] ?? computedValues.ToArray();
        if (graph.ValueCache.TryGet(Node.Id, out cached)
            && cached is CachedNodeValue computed
            && ReferenceEquals(computed.Value, array))
        {
            return Result<IReadOnlyList<T>, ComputationError>.Success(View(computed, array));
        }

        return Result<IReadOnlyList<T>, ComputationError>.Success(Wrap(array));
    }

    /// <summary>
    /// Convenience property to get the computed values directly, as a read-only view (see
    /// <see cref="GetValues"/>).
    /// Throws if computation fails. Use GetValues() for Result-based error handling.
    /// </summary>
    /// <exception cref="InvalidOperationException">The computation failed.</exception>
    /// <exception cref="ObjectDisposedException">The context owning the provider has been disposed.</exception>
    public IReadOnlyList<T> Values
    {
        get
        {
            var result = GetValues();
            if (result.IsSuccess)
                return result.ValueOrDefault!;

            throw new InvalidOperationException(
                $"Failed to compute values: {result.ErrorValue?.Message ?? "Unknown error"}");
        }
    }

    // The read-only view of a cached array, created once per cached value
    private static IReadOnlyList<T> View(CachedNodeValue entry, T[] values)
    {
        if (entry.View is ReadOnlyCollection<T> view)
            return view;

        view = Wrap(values);
        entry.View = view;
        return view;
    }

    private static ReadOnlyCollection<T> Wrap(T[] values) =>
        values.Length == 0 ? ReadOnlyCollection<T>.Empty : new ReadOnlyCollection<T>(values);
}

/// <summary>
/// Records computation metrics around a provider's node computation.
/// </summary>
internal static class ComputationMetrics
{
    /// <summary>
    /// Runs <paramref name="compute"/> and, if computation metrics are enabled and listened to, records it in
    /// <see cref="GuacamoleMetrics.ComputationsTotal"/>, <see cref="GuacamoleMetrics.ComputationDuration"/>
    /// and (for an error result) <see cref="GuacamoleMetrics.ComputationErrors"/>.
    /// </summary>
    public static Result<TValue, ComputationError> Measure<TState, TValue>(
        string nodeType,
        ProviderMetadata metadata,
        string providerType,
        Func<TState, ComputationContext, Result<TValue, ComputationError>> compute,
        TState state,
        ComputationContext context)
    {
        // Nothing to record unless something listens (checked first: a field read per instrument)
        var configuration = MetricsConfiguration.Current;
        if (!(GuacamoleMetrics.ComputationsTotal.Enabled
                || GuacamoleMetrics.ComputationDuration.Enabled
                || GuacamoleMetrics.ComputationErrors.Enabled)
            || !configuration.EnableComputationMetrics)
        {
            return compute(state, context);
        }

        var start = Stopwatch.GetTimestamp();
        var result = compute(state, context);
        GuacamoleMetrics.RecordComputation(
            configuration,
            providerType,
            nodeType,
            metadata,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            result.IsError);
        return result;
    }
}
