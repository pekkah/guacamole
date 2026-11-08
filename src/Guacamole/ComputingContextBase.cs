using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Guacamole.Pipeline;

namespace Guacamole;

/// <summary>
/// Minimal abstract base class for user-defined incremental computation contexts.
/// Users inherit from this class and define properties as IncrementalValueProvider types.
/// Source generators automatically extend this with source-backed properties.
/// </summary>
/// <remarks>
/// This base class provides only the essential infrastructure:
/// - Computation graph for dependency tracking, with an LRU value cache (see <see cref="CacheCapacity"/>)
/// - Disposal state management
/// - Source change notification handling
/// All context-specific functionality (sources, properties) is generated at compile time.
/// </remarks>
public abstract partial class ComputingContextBase : IDisposable
{
    private volatile bool _disposed;
    private readonly List<OutputRegistrationBase> _outputRegistrations = new();
    private readonly List<Action> _sourceUnsubscriptions = new();
    private readonly object _registrationsLock = new();

    /// <summary>
    /// Shared computation graph for dependency management and incremental recomputation.
    /// </summary>
    internal ComputationGraph Graph { get; private set; }

    /// <summary>
    /// Indicates if the context has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Default maximum number of computed values a context keeps cached.
    /// </summary>
    public const int DefaultCacheCapacity = 10_000;

    /// <summary>
    /// Maximum number of computed values this context keeps in its LRU value cache.
    /// Evicted values are recomputed on the next read. Defaults to <see cref="DefaultCacheCapacity"/>.
    /// </summary>
    /// <remarks>
    /// Override to change the capacity, for example
    /// <c>protected override int CacheCapacity =&gt; 50_000;</c>. The value is read once, by the
    /// <see cref="ComputingContextBase"/> constructor, before the derived class's constructor
    /// and field initializers run, so the override must not depend on instance state.
    /// </remarks>
    protected virtual int CacheCapacity => DefaultCacheCapacity;

    /// <summary>
    /// Enables automatic trimming of unused pipeline nodes: the context runs
    /// <see cref="TrimUnusedNodes"/> itself after every <see cref="AutoTrimInterval"/> pipeline nodes
    /// added to its graph. <c>0</c> (the default) disables automatic trimming.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this in long-running hosts that build pipelines dynamically (capturing lambdas,
    /// per-request pipelines), so that the graph stays bounded without calling
    /// <see cref="TrimUnusedNodes"/> explicitly. Override to enable it, for example
    /// <c>protected override int AutoTrimInterval =&gt; 1_000;</c>. Like <see cref="CacheCapacity"/>,
    /// the value is read once by the <see cref="ComputingContextBase"/> constructor, so the override
    /// must not depend on instance state.
    /// </para>
    /// <para>
    /// Only nodes newly added to the graph by operator calls (or first read) count; reads of
    /// existing pipelines and repeated identical operator calls that reuse a node cost nothing. When
    /// many nodes remain registered, a trim waits for at least half as many new nodes as remained
    /// after the previous trim, not counting the nodes added just before it, so the cost of trimming
    /// stays proportional to the number of nodes added or removed. After a full garbage collection,
    /// the next trim runs after <see cref="AutoTrimInterval"/> new nodes, so the graph shrinks back to
    /// its working set within a few intervals after a burst. A trim runs on the thread that added the
    /// node, never inside a node computation (it is deferred to the next node added outside one) and
    /// never waits for another trim: if one is running, the next added node tries again. While a trim
    /// runs, other threads creating new nodes wait for it; reads do not.
    /// </para>
    /// <para>
    /// As with explicit calls, a trim removes the nodes that have been garbage-collected and
    /// releases the context's own references to the rest, so unreferenced nodes are removed by a
    /// later automatic trim once the garbage collector has collected them. When no new nodes are
    /// added, no trims run, but the graph does not grow either. Nodes that only the context keeps
    /// alive (for example nodes created by identical operator calls in a property getter whose
    /// providers are not stored) may be collected after a trim and are then recreated with an empty
    /// cache by the next identical call; nodes referenced by a provider, an output registration or
    /// a downstream node are never removed, so trimming never affects results.
    /// </para>
    /// </remarks>
    protected virtual int AutoTrimInterval => 0;

    /// <summary>
    /// Removes the pipeline nodes that are no longer used from this context's graph, together with
    /// their cached values and version records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every operator call (<c>Select</c>, <c>Where</c>, <c>Combine</c>, ...) adds a node to the
    /// context's graph. Identical calls reuse their node, but calls with lambdas that capture locals
    /// (a new closure each time) or pipelines built per request add new nodes. A node stays alive as
    /// long as a provider, an output registration or a downstream node references it; the context
    /// itself also keeps the nodes created by operator calls alive, so that repeated identical calls
    /// (for example in property getters) keep sharing one node and its cache.
    /// </para>
    /// <para>
    /// This method
    /// </para>
    /// <list type="number">
    /// <item><description>removes the nodes that have been garbage-collected, with their cached values
    /// and version records, and</description></item>
    /// <item><description>stops keeping the remaining nodes alive itself, so that those nothing else
    /// references can be collected; they are removed by a later call. A node requested again by an
    /// identical operator call before it is collected is kept alive again.</description></item>
    /// </list>
    /// <para>
    /// A node that is still referenced (directly or as an input of a node that is) is never removed,
    /// so trimming never affects results. An unreferenced node that is removed and later requested by
    /// an identical operator call is recreated with an empty cache. Call this periodically (for example
    /// after handling a batch of requests) when pipelines are built dynamically, or override
    /// <see cref="AutoTrimInterval"/> to have the context call it automatically; it is not needed
    /// when pipelines are built once. Thread-safe.
    /// </para>
    /// </remarks>
    /// <returns>The number of nodes removed</returns>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    public int TrimUnusedNodes()
    {
        ThrowIfDisposed();
        return Graph.TrimUnusedNodes();
    }

    /// <summary>
    /// Creates a new computing context.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="CacheCapacity"/> is less than 1, or
    /// <see cref="AutoTrimInterval"/> is negative.</exception>
    protected ComputingContextBase()
    {
        var autoTrimInterval = AutoTrimInterval;
        if (autoTrimInterval < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AutoTrimInterval), autoTrimInterval, "AutoTrimInterval must be 0 (disabled) or positive.");
        }

        Graph = new ComputationGraph(CacheCapacity, autoTrimInterval);
    }

    /// <summary>
    /// Disposes the context: unsubscribes it from its sources and drops its output registrations.
    /// Calling it again does nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A disposed context no longer tracks source changes, so it refuses to be used rather than
    /// return stale values. Afterwards:
    /// </para>
    /// <list type="bullet">
    /// <item><description>reading any of its providers (<see cref="IncrementalValueProvider{T}.GetValue"/>,
    /// <see cref="IncrementalValueProvider{T}.Value"/>, <see cref="IncrementalValueProvider{T}.TryGetValue"/>,
    /// <see cref="IncrementalValuesProvider{T}.GetValues"/>, <see cref="IncrementalValuesProvider{T}.Values"/>),
    /// including pipelines built from them, throws <see cref="ObjectDisposedException"/>;</description></item>
    /// <item><description>changing its sources (<c>Set</c>, <c>Push</c>, <c>PushRange</c>, <c>Remove</c>,
    /// <c>Replace</c>, <c>Clear</c>, <c>BeginBatch</c>, batch <c>Commit</c>) throws
    /// <see cref="ObjectDisposedException"/>, while disposing a pending batch discards its operations
    /// without throwing; reading a source's own current value (<c>GetValue</c>, <c>GetSnapshot</c>)
    /// still works;</description></item>
    /// <item><description><see cref="RegisterOutput{T}(IncrementalValueProvider{T}, Action{T, ComputingContextBase})"/>
    /// and <see cref="TrimUnusedNodes"/> throw <see cref="ObjectDisposedException"/>, and output handlers
    /// are no longer called (as for a disposed registration, a delivery in progress on another thread
    /// may still make one call).</description></item>
    /// </list>
    /// <para>
    /// Reads and changes already in progress on other threads when <c>Dispose</c> is called may still
    /// complete.
    /// </para>
    /// </remarks>
    public virtual void Dispose()
    {
        Action[] unsubscriptions;
        lock (_registrationsLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            unsubscriptions = _sourceUnsubscriptions.ToArray();
            _sourceUnsubscriptions.Clear();
            foreach (var registration in _outputRegistrations)
            {
                registration.MarkDisposed();
            }

            _outputRegistrations.Clear();

            // Providers and sources of this context throw from now on (they share the graph state).
            // After the registrations are marked: a delivery whose provider read throws because of
            // this then sees its registration disposed, and ignores the exception (see OutputGate).
            Graph.SharedState.MarkDisposed();
        }

        // Detach from sources so they neither keep this context alive nor
        // notify it after disposal
        foreach (var unsubscribe in unsubscriptions)
        {
            unsubscribe();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Throws if the context has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>
    /// Creates a collection source owned by this context: registers its node in the
    /// context's graph and subscribes the context to its changes.
    /// Called by the generated constructor for each <see cref="IncrementalValuesProvider{T}"/> input property.
    /// </summary>
    /// <typeparam name="T">The type of items in the source</typeparam>
    /// <param name="sourceId">Unique identifier of the source within this context</param>
    /// <returns>The new source</returns>
    protected IIncrementalSource<T> CreateSource<T>(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var source = new IncrementalSource<T>(sourceId, () => Graph);
        Graph = Graph.AddNode(source.GetNode());
        AddSourceSubscription(() => source.Changed += OnSourceChanged, () => source.Changed -= OnSourceChanged);
        return source;
    }

    /// <summary>
    /// Creates a single-value source owned by this context: registers its node in the
    /// context's graph and subscribes the context to its changes.
    /// Called by the generated constructor for each <see cref="IncrementalValueProvider{T}"/> input property.
    /// </summary>
    /// <typeparam name="T">The type of the source's value</typeparam>
    /// <param name="sourceId">Unique identifier of the source within this context</param>
    /// <returns>The new source</returns>
    protected IIncrementalValueSource<T> CreateValueSource<T>(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var source = new IncrementalValueSource<T>(sourceId, () => Graph);
        Graph = Graph.AddNode(source.GetNode());
        AddSourceSubscription(() => source.Changed += OnSourceChanged, () => source.Changed -= OnSourceChanged);
        return source;
    }

    private void AddSourceSubscription(Action subscribe, Action unsubscribe)
    {
        lock (_registrationsLock)
        {
            ThrowIfDisposed();
            subscribe();
            _sourceUnsubscriptions.Add(unsubscribe);
        }
    }

    /// <summary>
    /// Handles source change events by marking the source version as changed
    /// and triggering output handlers for lazy validation.
    /// </summary>
    /// <remarks>
    /// Uses version-based lazy invalidation instead of eagerly clearing caches.
    /// When GetValue() is called, it will check if dependency versions have changed
    /// and recompute only when necessary. This enables equality-based cache skipping
    /// where unchanged intermediate values prevent downstream recomputation.
    /// </remarks>
    private void OnSourceChanged(object? sender, SourceChangedEventArgs e)
    {
        // A notification can race with Dispose; ignore it once disposed
        if (_disposed)
            return;

        // Mark the source's version as changed - don't clear caches
        // Lazy validation during GetValue() will handle cache freshness
        Graph.MarkSourceChanged(e.SourceId);

        // Trigger all registered output handlers when any source changes
        TriggerOutputs();
    }

    /// <summary>
    /// Registers an output handler that will be called when sources change.
    /// Similar to Roslyn's RegisterSourceOutput - handlers are called after changes occur
    /// and the provider's value is recomputed, and only when the value changed (by the provider's
    /// comparer) since the handler last ran.
    /// The handler is not called while the provider has no value (a <c>Where</c> upstream
    /// rejected its input) or its computation fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler is not called at registration. After each effective change of any source of the
    /// context, the provider's value is delivered to it if that value (including a <c>null</c> value)
    /// differs from the value last delivered to it, by the provider's comparer.
    /// </para>
    /// <para>
    /// <b>Threading and ordering.</b> Sources may change on several threads at once. A value is
    /// delivered synchronously by the thread that changed the source, before its source operation
    /// returns, unless a delivery to this handler is already running (on another thread, or further up
    /// the same thread when a handler changed a source). Then the source operation returns at once,
    /// and the thread that is delivering calls the handler again after it returns, with the value
    /// current at that point. Hence the calls of one handler are serialized (never concurrent with
    /// each other, never re-entrant); a handler never receives the same value twice in a row; each
    /// call reads the provider after the previous call returned, so values arrive in the order they
    /// were read (intermediate values of changes that arrive while it runs may be skipped); and once
    /// all source operations have returned, the last value it received is the provider's current
    /// value. Different handlers may run concurrently with each other, and no lock of the context is
    /// held while a handler runs.
    /// </para>
    /// <para>
    /// The delivering thread keeps delivering while changes keep arriving during its deliveries, so a
    /// source operation that delivers can be held for as long as other threads keep changing sources:
    /// with slow handlers and continuous concurrent writes, its latency is unbounded.
    /// </para>
    /// <para>
    /// <b>Exceptions</b> thrown by handlers surface on the thread that delivered: every handler runs
    /// even if an earlier one throws, and afterwards that thread's source operation throws one
    /// <see cref="AggregateException"/> whose inner exceptions are all the handler exceptions of its
    /// deliveries, including those of deliveries it made for other threads' changes.
    /// </para>
    /// <para>
    /// A handler may change sources itself. The resulting change does not call the same handler from
    /// within itself: it is called again with the new value after it returns. A handler that always
    /// changes a source to a new value therefore loops forever.
    /// </para>
    /// <para>
    /// Disposing the returned registration is thread-safe and idempotent. Deliveries that start after
    /// <c>Dispose</c> returns do not call the handler, and disposing it from within the handler (or on
    /// the thread delivering to it) means it is not called again. A delivery already in progress on
    /// another thread checks for disposal right before calling the handler, so it may still make one
    /// call that begins just after <c>Dispose</c> returns; a call already running completes. Other
    /// handlers, including other registrations of the same handler, are not affected.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of value produced by the provider</typeparam>
    /// <param name="provider">The provider whose output should trigger the handler</param>
    /// <param name="handler">Action to execute with the computed value and context</param>
    /// <returns>
    /// A registration that unregisters this handler when disposed (see the remarks). Disposing the
    /// context unregisters all handlers.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    public IDisposable RegisterOutput<T>(
        IncrementalValueProvider<T> provider,
        Action<T, ComputingContextBase> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return AddOutputRegistration(new OutputRegistration<T>(this, provider, handler));
    }

    /// <summary>
    /// Registers an output handler for collection providers.
    /// Similar to Roslyn's RegisterSourceOutput - handlers are called after changes occur
    /// and the provider's values are recomputed, and only when the values changed (item by item, in
    /// order, by the provider's comparer) since the handler last ran.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection</typeparam>
    /// <param name="provider">The collection provider whose output should trigger the handler</param>
    /// <param name="handler">Action to execute with the computed values (a read-only view, see
    /// <see cref="IncrementalValuesProvider{T}.GetValues"/>) and context</param>
    /// <returns>
    /// A registration that unregisters this handler when disposed (see the remarks of
    /// <see cref="RegisterOutput{T}(IncrementalValueProvider{T}, Action{T, ComputingContextBase})"/>).
    /// Disposing the context unregisters all handlers.
    /// </returns>
    /// <remarks>
    /// Called as described for the single-value overload, including its threading and ordering
    /// guarantees; the values are compared with the last delivered ones item by item, in order, with
    /// the provider's comparer.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    public IDisposable RegisterOutput<T>(
        IncrementalValuesProvider<T> provider,
        Action<IReadOnlyList<T>, ComputingContextBase> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return AddOutputRegistration(new CollectionOutputRegistration<T>(this, provider, handler));
    }

    private OutputRegistrationBase AddOutputRegistration(OutputRegistrationBase registration)
    {
        lock (_registrationsLock)
        {
            ThrowIfDisposed();
            _outputRegistrations.Add(registration);
        }

        return registration;
    }

    private void RemoveOutputRegistration(OutputRegistrationBase registration)
    {
        lock (_registrationsLock)
        {
            _outputRegistrations.Remove(registration);
        }
    }

    /// <summary>
    /// Triggers all registered output handlers by recomputing their providers.
    /// Called automatically when sources change.
    /// </summary>
    /// <remarks>
    /// Every handler runs even if an earlier one throws. If any handler throws,
    /// an <see cref="AggregateException"/> containing all handler exceptions (also those of
    /// deliveries this thread made for other threads' changes, see
    /// <see cref="RegisterOutput{T}(IncrementalValueProvider{T}, Action{T, ComputingContextBase})"/>)
    /// is thrown after all handlers have run.
    /// </remarks>
    /// <exception cref="AggregateException">One or more output handlers threw.</exception>
    protected void TriggerOutputs()
    {
        // Capture registrations under lock, then execute handlers outside lock
        // to avoid holding lock during potentially long-running handler execution
        OutputRegistrationBase[] registrations;

        lock (_registrationsLock)
        {
            registrations = _outputRegistrations.ToArray();
        }

        List<Exception>? exceptions = null;

        // Execute handlers outside the lock
        // (each registration collects its handler's exceptions, including those of deliveries it
        // made for other threads' changes, into the list: one flat AggregateException)
        foreach (var registration in registrations)
        {
            registration.Execute(this, ref exceptions);
        }

        if (exceptions != null)
        {
            throw new AggregateException("One or more output handlers threw an exception.", exceptions);
        }
    }

    // Base class of output registrations, and the handle RegisterOutput returns
    private abstract class OutputRegistrationBase : IDisposable
    {
        private readonly ComputingContextBase _context;
        private readonly OutputGate _gate = new();
        private volatile bool _disposed;

        protected OutputRegistrationBase(ComputingContextBase context)
        {
            _context = context;
        }

        // Checked by the gate before each delivery and right before the handler runs. Set for every
        // registration when the context is disposed.
        public bool IsDisposed => _disposed;

        // Serialized per registration (see OutputGate): Deliver, and the last delivered value it
        // keeps, run on one thread at a time
        public void Execute(ComputingContextBase context, ref List<Exception>? exceptions) =>
            _gate.Run(this, context, ref exceptions);

        // Reads the provider and calls the handler if the value changed. Called only by the gate.
        public abstract void Deliver(ComputingContextBase context);

        // Called by the context's Dispose (under its lock) for every remaining registration
        public void MarkDisposed() => _disposed = true;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _context.RemoveOutputRegistration(this);
        }
    }

    // Registration for single-value providers
    private sealed class OutputRegistration<T> : OutputRegistrationBase
    {
        private readonly IncrementalValueProvider<T> _provider;
        private readonly Action<T, ComputingContextBase> _handler;
        private readonly IEqualityComparer<T> _comparer;
        private T? _lastValue;
        private bool _hasExecuted;

        public OutputRegistration(ComputingContextBase context, IncrementalValueProvider<T> provider, Action<T, ComputingContextBase> handler)
            : base(context)
        {
            _provider = provider;
            _handler = handler;
            // Use the provider's custom comparer (e.g. Select(selector, comparer)) to decide
            // whether a new value is a change worth reporting
            _comparer = provider.Node is IHasComparer<T> withComparer ? withComparer.Comparer : DefaultEqualityComparer<T>.Instance;
        }

        public override void Deliver(ComputingContextBase context)
        {
            if (IsDisposed)
                return;

            Result<T, ComputationError> result;
            try
            {
                result = _provider.GetValue();
            }
            catch (ObjectDisposedException) when (IsDisposed)
            {
                // The context was disposed during this delivery (it marks its registrations disposed
                // before its providers start throwing): the handler is no longer called. Only the
                // provider read is covered, so exceptions thrown by the handler always surface.
                return;
            }

            if (result.IsNone)
            {
                // No value (filtered out by Where): don't run the handler, and run it again
                // when a value reappears, even if it equals the last delivered one
                _hasExecuted = false;
                _lastValue = default;
                return;
            }

            // A null computed value is a value too (not an error and not "no value"): deliver it
            if (result.IsSuccess)
            {
                var value = result.ValueOrDefault!;

                // Only call handler if value changed (Roslyn-style incremental computation)
                if (!_hasExecuted || !_comparer.Equals(_lastValue!, value))
                {
                    if (IsDisposed)
                        return;

                    _lastValue = value;
                    _hasExecuted = true;
                    _handler(value, context);
                }
            }
        }
    }

    // Registration for collection providers
    private sealed class CollectionOutputRegistration<T> : OutputRegistrationBase
    {
        private readonly IncrementalValuesProvider<T> _provider;
        private readonly Action<IReadOnlyList<T>, ComputingContextBase> _handler;
        private readonly IEqualityComparer<T> _comparer;
        private IReadOnlyList<T>? _lastValue;
        private bool _hasExecuted;

        public CollectionOutputRegistration(ComputingContextBase context, IncrementalValuesProvider<T> provider, Action<IReadOnlyList<T>, ComputingContextBase> handler)
            : base(context)
        {
            _provider = provider;
            _handler = handler;
            // Compare items with the provider's custom comparer, if it has one
            _comparer = provider.Node is IHasComparer<T> withComparer ? withComparer.Comparer : DefaultEqualityComparer<T>.Instance;
        }

        public override void Deliver(ComputingContextBase context)
        {
            if (IsDisposed)
                return;

            Result<IReadOnlyList<T>, ComputationError> result;
            try
            {
                result = _provider.GetValues();
            }
            catch (ObjectDisposedException) when (IsDisposed)
            {
                // Disposed during this delivery: see OutputRegistration<T>.Deliver
                return;
            }

            if (result.IsSuccess && result.ValueOrDefault != null)
            {
                // A read-only view of an immutable cached array (see GetValues), so it can be kept
                // for the next comparison without copying it
                var current = result.ValueOrDefault;

                // Compare item by item; only call the handler if the collection changed
                if (!_hasExecuted || _lastValue == null
                    || !(ReferenceEquals(_lastValue, current) || _lastValue.SequenceEqual(current, _comparer)))
                {
                    if (IsDisposed)
                        return;

                    _lastValue = current;
                    _hasExecuted = true;
                    _handler(current, context);
                }
            }
        }
    }
}
