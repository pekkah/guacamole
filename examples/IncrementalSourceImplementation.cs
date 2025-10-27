using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;

namespace Guacamole.Sources;

/// <summary>
/// Represents a source of data that can be pushed into the incremental computation graph.
/// Sources are the entry points for external data (file changes, network events, etc.)
/// and automatically invalidate the computation cache when data changes.
///
/// This is the proposed replacement for mutation methods (Add/Remove/SaveChanges).
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
    /// Adds an item to the source. Thread-safe, triggers invalidation automatically.
    /// No SaveChanges required - changes flow immediately!
    /// </summary>
    void Push(T item);

    /// <summary>
    /// Removes an item from the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    void Remove(T item);

    /// <summary>
    /// Replaces an item in the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    void Replace(T oldItem, T newItem);

    /// <summary>
    /// Clears all items from the source. Thread-safe, triggers invalidation automatically.
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
public interface IIncrementalValueSource<T>
{
    string SourceId { get; }
    IncrementalValueProvider<T> AsProvider();
    void Set(T value);
    T? GetValue();
    event EventHandler<SourceChangedEventArgs>? Changed;
}

/// <summary>
/// Event args for source change notifications.
/// </summary>
public sealed class SourceChangedEventArgs : EventArgs
{
    public string SourceId { get; }
    public DateTime Timestamp { get; }

    public SourceChangedEventArgs(string sourceId, DateTime timestamp)
    {
        SourceId = sourceId;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Thread-safe implementation of IIncrementalSource using immutable collections.
/// Automatically invalidates the computation graph cache when data changes.
/// </summary>
public sealed class IncrementalSource<T> : IIncrementalSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private ImmutableHashSet<T> _items = ImmutableHashSet<T>.Empty;
    private ComputationGraph _graph;
    private readonly ICollectionComputationNode<T> _node;

    public string SourceId { get; }
    public event EventHandler<SourceChangedEventArgs>? Changed;

    /// <summary>
    /// Creates a new incremental source.
    /// </summary>
    /// <param name="sourceId">Unique identifier for this source</param>
    /// <param name="graph">The computation graph to integrate with</param>
    public IncrementalSource(string sourceId, ComputationGraph graph)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        // Create the computation node for this source
        _node = new SourceCollectionNode<T>(sourceId, this);

        // Register node in the graph
        _graph = _graph.AddNode(_node);
    }

    /// <summary>
    /// Returns a provider that exposes this source's values to the computation pipeline.
    /// </summary>
    public IncrementalValuesProvider<T> AsProvider()
    {
        return new IncrementalValuesProvider<T>(
            _node,
            _graph,
            new ProviderMetadata($"Source: {SourceId}", "Push-based external source"));
    }

    /// <summary>
    /// Pushes an item into the source. Triggers automatic invalidation and recomputation.
    /// </summary>
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
            // Invalidate cache and notify observers
            // This happens IMMEDIATELY - no SaveChanges needed!
            InvalidateCacheAndNotify();
        }
    }

    /// <summary>
    /// Removes an item from the source. Triggers automatic invalidation and recomputation.
    /// </summary>
    public void Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        bool changed = false;

        _lock.EnterWriteLock();
        try
        {
            var newItems = _items.Remove(item);
            if (newItems != _items)
            {
                _items = newItems;
                changed = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            InvalidateCacheAndNotify();
        }
    }

    /// <summary>
    /// Replaces an item in the source. Triggers automatic invalidation and recomputation.
    /// </summary>
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
            InvalidateCacheAndNotify();
        }
    }

    /// <summary>
    /// Clears all items from the source. Triggers automatic invalidation and recomputation.
    /// </summary>
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
            InvalidateCacheAndNotify();
        }
    }

    /// <summary>
    /// Gets a thread-safe snapshot of the current items.
    /// </summary>
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
    /// Invalidates the computation cache and notifies observers.
    /// This causes dependent computations to be recomputed automatically.
    /// </summary>
    private void InvalidateCacheAndNotify()
    {
        // Remove this node from cache
        _graph.ValueCache.Remove(SourceId);

        // Find and invalidate all dependent nodes transitively
        var dependents = FindAllDependents();
        foreach (var dependent in dependents)
        {
            _graph.ValueCache.Remove(dependent);
        }

        // Notify observers (context will trigger output handlers)
        Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
    }

    /// <summary>
    /// Finds all nodes that depend on this source (transitively).
    /// </summary>
    private HashSet<string> FindAllDependents()
    {
        var dependents = new HashSet<string>();
        var toCheck = new Queue<string>();
        toCheck.Enqueue(SourceId);

        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();

            // Find all nodes that directly depend on current
            foreach (var kvp in _graph.Dependencies)
            {
                if (kvp.Value.Contains(current) && !dependents.Contains(kvp.Key))
                {
                    dependents.Add(kvp.Key);
                    toCheck.Enqueue(kvp.Key);
                }
            }
        }

        return dependents;
    }

    /// <summary>
    /// Internal computation node that reads from the source.
    /// This node has no dependencies (sources are the root of the graph).
    /// </summary>
    private sealed class SourceCollectionNode<TItem> : ICollectionComputationNode<TItem>
    {
        private readonly IncrementalSource<TItem> _source;

        public string Id { get; }
        public Type OutputType => typeof(IEnumerable<TItem>);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty; // Sources have no dependencies!

        public SourceCollectionNode(string id, IncrementalSource<TItem> source)
        {
            Id = id;
            _source = source;
        }

        public Result<IEnumerable<TItem>, ComputationError> Compute(ComputationContext context)
        {
            // Check cache first
            if (context.CachedValues.TryGetValue(Id, out var cached) && cached is IEnumerable<TItem> cachedItems)
            {
                return Result<IEnumerable<TItem>, ComputationError>.Success(cachedItems);
            }

            // Read current snapshot from source
            var items = _source.GetSnapshot();
            return Result<IEnumerable<TItem>, ComputationError>.Success(items);
        }
    }
}

/// <summary>
/// Thread-safe implementation of IIncrementalValueSource for single values.
/// </summary>
public sealed class IncrementalValueSource<T> : IIncrementalValueSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private T? _value;
    private ComputationGraph _graph;
    private readonly IComputationNode<T> _node;

    public string SourceId { get; }
    public event EventHandler<SourceChangedEventArgs>? Changed;

    public IncrementalValueSource(string sourceId, ComputationGraph graph)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        _node = new SourceValueNode<T>(sourceId, this);
        _graph = _graph.AddNode(_node);
    }

    public IncrementalValueProvider<T> AsProvider()
    {
        return new IncrementalValueProvider<T>(
            _node,
            _graph,
            new ProviderMetadata($"Source: {SourceId}", "Push-based single value source"));
    }

    public void Set(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        bool changed = false;

        _lock.EnterWriteLock();
        try
        {
            // Only update if value actually changed
            if (!EqualityComparer<T>.Default.Equals(_value, value))
            {
                _value = value;
                changed = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            InvalidateCacheAndNotify();
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

    private void InvalidateCacheAndNotify()
    {
        _graph.ValueCache.Remove(SourceId);

        var dependents = FindAllDependents();
        foreach (var dependent in dependents)
        {
            _graph.ValueCache.Remove(dependent);
        }

        Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
    }

    private HashSet<string> FindAllDependents()
    {
        var dependents = new HashSet<string>();
        var toCheck = new Queue<string>();
        toCheck.Enqueue(SourceId);

        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();

            foreach (var kvp in _graph.Dependencies)
            {
                if (kvp.Value.Contains(current) && !dependents.Contains(kvp.Key))
                {
                    dependents.Add(kvp.Key);
                    toCheck.Enqueue(kvp.Key);
                }
            }
        }

        return dependents;
    }

    private sealed class SourceValueNode<TValue> : IComputationNode<TValue>
    {
        private readonly IncrementalValueSource<TValue> _source;

        public string Id { get; }
        public Type OutputType => typeof(TValue);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty;

        public SourceValueNode(string id, IncrementalValueSource<TValue> source)
        {
            Id = id;
            _source = source;
        }

        public Result<TValue, ComputationError> Compute(ComputationContext context)
        {
            if (context.CachedValues.TryGetValue(Id, out var cached) && cached is TValue cachedValue)
            {
                return Result<TValue, ComputationError>.Success(cachedValue);
            }

            var value = _source.GetValue();
            if (value == null)
            {
                return Result<TValue, ComputationError>.Error(
                    ComputationError.FromMessage($"Source '{Id}' has no value set"));
            }

            return Result<TValue, ComputationError>.Success(value);
        }
    }
}

/// <summary>
/// Extension methods for batch operations on sources.
/// </summary>
public static class IncrementalSourceExtensions
{
    /// <summary>
    /// Begins a batch operation where multiple changes are applied
    /// with a single cache invalidation at the end.
    /// </summary>
    public static IBatchOperation<T> BeginBatch<T>(this IIncrementalSource<T> source)
    {
        return new BatchOperation<T>(source);
    }

    /// <summary>
    /// Pushes multiple items in a batch.
    /// </summary>
    public static void PushRange<T>(this IIncrementalSource<T> source, IEnumerable<T> items)
    {
        using var batch = source.BeginBatch();
        foreach (var item in items)
        {
            batch.Push(item);
        }
        batch.Commit();
    }
}

/// <summary>
/// Represents a batch operation that defers cache invalidation.
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
/// Implementation of batch operations.
/// Changes are buffered and applied with a single invalidation.
/// </summary>
public sealed class BatchOperation<T> : IBatchOperation<T>
{
    private readonly IIncrementalSource<T> _source;
    private readonly List<Action> _operations = new();
    private bool _committed;
    private bool _disposed;

    public BatchOperation(IIncrementalSource<T> source)
    {
        _source = source;
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

        // Execute all buffered operations
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
