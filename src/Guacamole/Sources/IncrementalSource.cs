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
/// Thread-safe implementation of IIncrementalSource using immutable collections.
/// Automatically invalidates the computation graph cache when data changes.
/// </summary>
/// <typeparam name="T">Type of items in the source</typeparam>
public sealed class IncrementalSource<T> : IIncrementalSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private ImmutableHashSet<T> _items = ImmutableHashSet<T>.Empty;
    private ComputationGraph _graph;
    private readonly ICollectionComputationNode<T> _node;

    /// <summary>
    /// Unique identifier for this source.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Event raised when the source changes.
    /// </summary>
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
        _node = new SourceCollectionNode(sourceId, this);

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
