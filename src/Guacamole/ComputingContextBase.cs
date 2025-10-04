using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Guacamole.Core.Pipeline;
using Guacamole.Core.Providers;

namespace Guacamole;

/// <summary>
/// Minimal abstract base class for user-defined incremental computation contexts.
/// Users inherit from this class and define properties as IncrementalValueProvider types.
/// Source generators automatically extend this with mutation methods, change tracking, and computation nodes.
/// </summary>
/// <remarks>
/// This base class provides only the essential infrastructure:
/// - Computation graph for dependency tracking
/// - Thread synchronization via ReaderWriterLockSlim (multiple readers, single writer)
/// - Disposal state management
/// All context-specific functionality (mutation methods, change tracking, SaveChanges) is generated at compile time.
/// </remarks>
public abstract partial class ComputingContextBase : IDisposable
{
    private bool _disposed;
    private readonly List<IOutputRegistration> _outputRegistrations = new();

    /// <summary>
    /// Shared computation graph for dependency management and incremental recomputation.
    /// </summary>
    protected ComputationGraph Graph { get; set; }

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
    /// Registers an output handler that will be called when SaveChanges triggers recomputation.
    /// Similar to Roslyn's RegisterSourceOutput - handlers are called after changes are committed
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
    /// Similar to Roslyn's RegisterSourceOutput - handlers are called after changes are committed
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
    /// Called automatically by generated SaveChanges() method.
    /// </summary>
    protected void TriggerOutputs()
    {
        Lock.EnterReadLock();
        try
        {
            foreach (var registration in _outputRegistrations)
            {
                registration.Execute(this);
            }
        }
        finally
        {
            Lock.ExitReadLock();
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
