// This file contains the complete implementation of the push-based source architecture.
// It's provided as a reference for understanding how sources work in Guacamole.
//
// NOTE: This is example code. The actual implementation is in src/Guacamole/Sources/

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;

namespace Guacamole.Sources;

// ============================================================================
// INTERFACES
// ============================================================================

/// <summary>
/// Represents a source of data that can be pushed into the incremental computation graph.
/// Sources are the entry points for external data (file changes, network events, etc.)
/// and automatically notify the context when data changes.
/// </summary>
/// <typeparam name="T">Type of items this source produces</typeparam>
public interface IIncrementalSource<T>
{
    /// <summary>
    /// Unique identifier for this source (used for graph node registration).
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// The provider that exposes this source's values to the computation pipeline.
    /// </summary>
    IncrementalValuesProvider<T> AsProvider();

    /// <summary>
    /// Adds an item to the source. Thread-safe, triggers notification automatically.
    /// </summary>
    void Push(T item);

    /// <summary>
    /// Removes an item from the source. Thread-safe, triggers notification automatically.
    /// </summary>
    void Remove(T item);

    /// <summary>
    /// Replaces an item in the source. Thread-safe, triggers notification automatically.
    /// </summary>
    void Replace(T oldItem, T newItem);

    /// <summary>
    /// Clears all items from the source. Thread-safe, triggers notification automatically.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets the current snapshot of items (thread-safe read).
    /// </summary>
    IReadOnlyCollection<T> GetSnapshot();

    /// <summary>
    /// Event raised when the source changes.
    /// The context subscribes to this to trigger output handlers automatically.
    /// </summary>
    event EventHandler<SourceChangedEventArgs>? Changed;
}

/// <summary>
/// Represents a single-value source (non-collection).
/// </summary>
/// <typeparam name="T">Type of value this source produces</typeparam>
public interface IIncrementalValueSource<T>
{
    /// <summary>
    /// Unique identifier for this source (used for graph node registration).
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// The provider that exposes this source's value to the computation pipeline.
    /// </summary>
    IncrementalValueProvider<T> AsProvider();

    /// <summary>
    /// Sets the value of the source. Thread-safe, triggers notification automatically.
    /// </summary>
    void Set(T value);

    /// <summary>
    /// Gets the current value (thread-safe read).
    /// </summary>
    T? GetValue();

    /// <summary>
    /// Event raised when the source changes.
    /// The context subscribes to this to trigger output handlers automatically.
    /// </summary>
    event EventHandler<SourceChangedEventArgs>? Changed;
}

/// <summary>
/// Event args for source change notifications.
/// </summary>
public sealed class SourceChangedEventArgs : EventArgs
{
    /// <summary>
    /// The identifier of the source that changed.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// The timestamp when the change occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    public SourceChangedEventArgs(string sourceId, DateTime timestamp)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        Timestamp = timestamp;
    }
}

// ============================================================================
// IMPLEMENTATION: IncrementalSource<T> (Collection)
// ============================================================================

/// <summary>
/// Thread-safe implementation of IIncrementalSource using immutable collections.
/// Raises change events when data changes; cache invalidation is handled by the context.
///
/// ARCHITECTURE NOTE: Sources use a graph provider pattern to avoid storing mutable
/// references to immutable graphs. The context owns the graph; sources access it via
/// a function.
/// </summary>
/// <typeparam name="T">Type of items in the source</typeparam>
public sealed class IncrementalSource<T> : IIncrementalSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private ImmutableHashSet<T> _items = ImmutableHashSet<T>.Empty;
    private readonly Func<ComputationGraph> _graphProvider; // ✅ Provider, not copy
    private readonly ICollectionComputationNode<T> _node;

    public string SourceId { get; }
    public event EventHandler<SourceChangedEventArgs>? Changed;

    /// <summary>
    /// Creates a new incremental source.
    /// </summary>
    /// <param name="sourceId">Unique identifier for this source</param>
    /// <param name="graphProvider">Function that provides access to the current computation graph</param>
    public IncrementalSource(string sourceId, Func<ComputationGraph> graphProvider)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));

        // Create the computation node for this source
        _node = new SourceCollectionNode(sourceId, this);
        // Node is registered by context after source creation
    }

    /// <summary>
    /// Gets the computation node for graph registration.
    /// Called by the context during initialization.
    /// </summary>
    internal ICollectionComputationNode<T> GetNode() => _node;

    /// <summary>
    /// Returns a provider that exposes this source's values to the computation pipeline.
    /// </summary>
    public IncrementalValuesProvider<T> AsProvider()
    {
        return new IncrementalValuesProvider<T>(
            _node,
            _graphProvider(), // ✅ Get current graph from provider
            new ProviderMetadata($"Source: {SourceId}", "Push-based external source"));
    }

    public void Push(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        bool changed = false;
        _lock.EnterWriteLock();
        try
        {
            if (_items.Contains(item))
                return; // Already exists, no-op

            _items = _items.Add(item);
            changed = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            // Raise event - context handles cache invalidation
            Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
        }
    }

    public void Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        bool changed = false;
        _lock.EnterWriteLock();
        try
        {
            var newItems = _items.Remove(item);
            if (newItems == _items)
                return; // Item didn't exist, no change

            _items = newItems;
            changed = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
        }
    }

    public void Replace(T oldItem, T newItem)
    {
        ArgumentNullException.ThrowIfNull(oldItem);
        ArgumentNullException.ThrowIfNull(newItem);

        if (EqualityComparer<T>.Default.Equals(oldItem, newItem))
            return; // No change

        bool changed = false;
        _lock.EnterWriteLock();
        try
        {
            if (!_items.Contains(oldItem))
                throw new InvalidOperationException($"Item to replace not found in source '{SourceId}'");

            if (_items.Contains(newItem))
                throw new InvalidOperationException($"Replacement item already exists in source '{SourceId}'");

            _items = _items.Remove(oldItem).Add(newItem);
            changed = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
        }
    }

    public void Clear()
    {
        bool changed = false;
        _lock.EnterWriteLock();
        try
        {
            if (_items.IsEmpty)
                return;

            _items = ImmutableHashSet<T>.Empty;
            changed = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
        }
    }

    public IReadOnlyCollection<T> GetSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return _items.ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Internal computation node that reads from the source.
    /// This node has no dependencies (sources are the root of the graph).
    /// </summary>
    private sealed class SourceCollectionNode : ICollectionComputationNode<T>
    {
        private readonly IncrementalSource<T> _source;

        public string Id { get; }
        public Type OutputType => typeof(IEnumerable<T>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty; // Sources have no dependencies!

        public SourceCollectionNode(string id, IncrementalSource<T> source)
        {
            Id = id;
            _source = source;
        }

        public Result<IEnumerable<T>, ComputationError> Compute(ComputationContext context)
        {
            // Check cache first
            if (context.CachedValues.TryGetValue(Id, out var cached) && cached is IEnumerable<T> cachedItems)
            {
                return Result<IEnumerable<T>, ComputationError>.Success(cachedItems);
            }

            // Read current snapshot from source
            var items = _source.GetSnapshot();
            return Result<IEnumerable<T>, ComputationError>.Success(items);
        }
    }
}

// ============================================================================
// IMPLEMENTATION: IncrementalValueSource<T> (Single Value)
// ============================================================================

/// <summary>
/// Thread-safe implementation of IIncrementalValueSource for single values.
/// Raises change events when the value changes; cache invalidation is handled by the context.
/// </summary>
/// <typeparam name="T">Type of value in the source</typeparam>
public sealed class IncrementalValueSource<T> : IIncrementalValueSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private T? _value;
    private bool _hasValue;
    private readonly Func<ComputationGraph> _graphProvider;
    private readonly IComputationNode<T> _node;

    public string SourceId { get; }
    public event EventHandler<SourceChangedEventArgs>? Changed;

    public IncrementalValueSource(string sourceId, Func<ComputationGraph> graphProvider)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));

        _node = new SourceValueNode(sourceId, this);
    }

    internal IComputationNode<T> GetNode() => _node;

    public IncrementalValueProvider<T> AsProvider()
    {
        return new IncrementalValueProvider<T>(
            _node,
            _graphProvider(),
            new ProviderMetadata($"Source: {SourceId}", "Push-based single value source"));
    }

    public void Set(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        bool changed = false;
        _lock.EnterWriteLock();
        try
        {
            if (!_hasValue || !EqualityComparer<T>.Default.Equals(_value, value))
            {
                _value = value;
                _hasValue = true;
                changed = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
        }
    }

    public T? GetValue()
    {
        _lock.EnterReadLock();
        try
        {
            return _value;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private sealed class SourceValueNode : IComputationNode<T>
    {
        private readonly IncrementalValueSource<T> _source;

        public string Id { get; }
        public Type OutputType => typeof(T);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;

        public SourceValueNode(string id, IncrementalValueSource<T> source)
        {
            Id = id;
            _source = source;
        }

        public Result<T, ComputationError> Compute(ComputationContext context)
        {
            if (context.CachedValues.TryGetValue(Id, out var cached) && cached is T cachedValue)
            {
                return Result<T, ComputationError>.Success(cachedValue);
            }

            var value = _source.GetValue();
            if (value == null)
            {
                return Result<T, ComputationError>.Error(
                    new ComputationError($"Source '{Id}' has no value set"));
            }

            return Result<T, ComputationError>.Success(value);
        }
    }
}

// ============================================================================
// BATCH OPERATIONS
// ============================================================================

/// <summary>
/// Represents a batch operation that defers notifications until commit.
/// </summary>
public interface IBatchOperation<T> : IDisposable
{
    void Push(T item);
    void Remove(T item);
    void Replace(T oldItem, T newItem);
    void Commit();
    void Rollback();
}

/// <summary>
/// Implementation of batch operations for IncrementalSource.
/// </summary>
public sealed class BatchOperation<T> : IBatchOperation<T>
{
    private readonly IIncrementalSource<T> _source;
    private readonly List<Action> _operations = new();
    private bool _committed;
    private bool _disposed;

    internal BatchOperation(IIncrementalSource<T> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Push(T item)
    {
        ThrowIfDisposed();
        _operations.Add(() => _source.Push(item));
    }

    public void Remove(T item)
    {
        ThrowIfDisposed();
        _operations.Add(() => _source.Remove(item));
    }

    public void Replace(T oldItem, T newItem)
    {
        ThrowIfDisposed();
        _operations.Add(() => _source.Replace(oldItem, newItem));
    }

    public void Commit()
    {
        ThrowIfDisposed();

        if (_committed)
            throw new InvalidOperationException("Batch already committed");

        foreach (var operation in _operations)
        {
            operation();
        }

        _committed = true;
        _operations.Clear();
    }

    public void Rollback()
    {
        ThrowIfDisposed();
        _operations.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Auto-commit on dispose if not explicitly committed or rolled back
        if (!_committed && _operations.Count > 0)
        {
            Commit();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BatchOperation<T>));
    }
}

/// <summary>
/// Extension methods for batch operations on sources.
/// </summary>
public static class IncrementalSourceExtensions
{
    public static IBatchOperation<T> BeginBatch<T>(this IIncrementalSource<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BatchOperation<T>(source);
    }

    public static void PushRange<T>(this IIncrementalSource<T> source, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(items);

        using var batch = source.BeginBatch();
        foreach (var item in items)
        {
            batch.Push(item);
        }
        batch.Commit();
    }
}
