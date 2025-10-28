using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Guacamole.Core.Pipeline;
using Guacamole.Sources;

namespace Guacamole;

/// <summary>
/// Minimal abstract base class for user-defined incremental computation contexts.
/// Users inherit from this class and define properties as IncrementalValueProvider types.
/// Source generators automatically extend this with source-backed properties.
/// </summary>
/// <remarks>
/// This base class provides only the essential infrastructure:
/// - Computation graph for dependency tracking
/// - Thread synchronization via ReaderWriterLockSlim (multiple readers, single writer)
/// - Disposal state management
/// - Source change notification handling
/// All context-specific functionality (sources, properties) is generated at compile time.
/// </remarks>
public abstract partial class ComputingContextBase : IDisposable
{
    private bool _disposed;
    private readonly List<IOutputRegistration> _outputRegistrations = new();

    /// <summary>
    /// Shared computation graph for dependency management and incremental recomputation.
    /// Marked as protected internal to allow generated code to update while preventing external modification.
    /// </summary>
    protected internal ComputationGraph Graph { get; set; }

    /// <summary>
    /// Thread synchronization lock (multiple readers, single writer model).
    /// </summary>
    protected ReaderWriterLockSlim Lock { get; }

    /// <summary>
    /// Indicates if the context has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Creates a new computing context.
    /// </summary>
    protected ComputingContextBase()
    {
        Graph = new ComputationGraph();
        Lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    }

    /// <summary>
    /// Disposes the context and releases resources.
    /// Generated code will override this to clear change tracking.
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;

        Lock?.Dispose();
        _disposed = true;

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
    /// Subscribes to a source's change events to automatically trigger output handlers.
    /// Called by generated code to wire up source notifications.
    /// </summary>
    /// <typeparam name="T">The type of items in the source</typeparam>
    /// <param name="source">The source to subscribe to</param>
    protected void SubscribeToSource<T>(IIncrementalSource<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Changed += OnSourceChanged;
    }

    /// <summary>
    /// Subscribes to a value source's change events to automatically trigger output handlers.
    /// Called by generated code to wire up source notifications.
    /// </summary>
    /// <typeparam name="T">The type of value in the source</typeparam>
    /// <param name="source">The source to subscribe to</param>
    protected void SubscribeToSource<T>(IIncrementalValueSource<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Changed += OnSourceChanged;
    }

    /// <summary>
    /// Handles source change events by invalidating cache and triggering output handlers.
    /// </summary>
    private void OnSourceChanged(object? sender, SourceChangedEventArgs e)
    {
        // Invalidate cache for the source and all its dependents
        InvalidateNodeCache(e.SourceId);

        // Trigger all registered output handlers when any source changes
        TriggerOutputs();
    }

    /// <summary>
    /// Invalidates cache for the specified node and all transitive dependents.
    /// Delegates to ComputationGraph which handles both static and runtime dependencies.
    /// </summary>
    private void InvalidateNodeCache(string nodeId)
    {
        // Delegate to graph's InvalidateCacheForNode which properly handles
        // both static and runtime dependencies
        Graph.InvalidateCacheForNode(nodeId);
    }

    /// <summary>
    /// Registers an output handler that will be called when sources change.
    /// Similar to Roslyn's RegisterSourceOutput - handlers are called after changes occur
    /// and the provider's value is recomputed.
    /// </summary>
    /// <typeparam name="T">The type of value produced by the provider</typeparam>
    /// <param name="provider">The provider whose output should trigger the handler</param>
    /// <param name="handler">Action to execute with the computed value and context</param>
    public void RegisterOutput<T>(
        IncrementalValueProvider<T> provider,
        Action<T, ComputingContextBase> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _outputRegistrations.Add(new OutputRegistration<T>(provider, handler));
    }

    /// <summary>
    /// Registers an output handler for collection providers.
    /// Similar to Roslyn's RegisterSourceOutput - handlers are called after changes occur
    /// and the provider's values are recomputed.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection</typeparam>
    /// <param name="provider">The collection provider whose output should trigger the handler</param>
    /// <param name="handler">Action to execute with the computed values and context</param>
    public void RegisterOutput<T>(
        IncrementalValuesProvider<T> provider,
        Action<IEnumerable<T>, ComputingContextBase> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _outputRegistrations.Add(new CollectionOutputRegistration<T>(provider, handler));
    }

    /// <summary>
    /// Triggers all registered output handlers by recomputing their providers.
    /// Called automatically when sources change.
    /// </summary>
    protected void TriggerOutputs()
    {
        // Capture registrations under lock, then execute handlers outside lock
        // to avoid holding lock during potentially long-running handler execution
        IOutputRegistration[] registrations;

        Lock.EnterReadLock();
        try
        {
            registrations = _outputRegistrations.ToArray();
        }
        finally
        {
            Lock.ExitReadLock();
        }

        // Execute handlers outside the lock
        foreach (var registration in registrations)
        {
            registration.Execute(this);
        }
    }

    // Internal interface for output registrations
    private interface IOutputRegistration
    {
        void Execute(ComputingContextBase context);
    }

    // Registration for single-value providers
    private sealed class OutputRegistration<T> : IOutputRegistration
    {
        private readonly IncrementalValueProvider<T> _provider;
        private readonly Action<T, ComputingContextBase> _handler;
        private T? _lastValue;
        private bool _hasExecuted;

        public OutputRegistration(IncrementalValueProvider<T> provider, Action<T, ComputingContextBase> handler)
        {
            _provider = provider;
            _handler = handler;
        }

        public void Execute(ComputingContextBase context)
        {
            var result = _provider.GetValue();
            if (result.IsSuccess && result.Value != null)
            {
                // Only call handler if value changed (Roslyn-style incremental computation)
                if (!_hasExecuted || !EqualityComparer<T>.Default.Equals(_lastValue, result.Value))
                {
                    _lastValue = result.Value;
                    _hasExecuted = true;
                    _handler(result.Value, context);
                }
            }
        }
    }

    // Registration for collection providers
    private sealed class CollectionOutputRegistration<T> : IOutputRegistration
    {
        private readonly IncrementalValuesProvider<T> _provider;
        private readonly Action<IEnumerable<T>, ComputingContextBase> _handler;
        private T[]? _lastValue;
        private bool _hasExecuted;

        public CollectionOutputRegistration(IncrementalValuesProvider<T> provider, Action<IEnumerable<T>, ComputingContextBase> handler)
        {
            _provider = provider;
            _handler = handler;
        }

        public void Execute(ComputingContextBase context)
        {
            var result = _provider.GetValues();
            if (result.IsSuccess && result.Value != null)
            {
                // Materialize to array for comparison (avoid reference equality issues)
                var currentArray = result.Value.ToArray();

                // For collections, compare using SequenceEqual (structural comparison)
                // Only call handler if collection changed
                if (!_hasExecuted || _lastValue == null || !_lastValue.SequenceEqual(currentArray))
                {
                    _lastValue = currentArray;
                    _hasExecuted = true;
                    _handler(result.Value, context);
                }
            }
        }
    }
}
