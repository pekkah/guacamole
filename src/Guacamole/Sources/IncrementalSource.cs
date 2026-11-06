using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Guacamole.Pipeline;

namespace Guacamole;

/// <summary>
/// Thread-safe implementation of IIncrementalSource using immutable collections.
/// Raises change events when data changes; cache invalidation is handled by the context.
/// </summary>
/// <remarks>
/// Items have set semantics (using <see cref="EqualityComparer{T}.Default"/>) and are enumerated in
/// insertion order: each item is assigned an increasing sequence number when pushed. An index
/// (item to sequence number) answers membership, and a sorted map (sequence number to item) keeps the
/// order, so <c>Push</c>, <c>Remove</c> and <c>Replace</c> are O(log n) and a snapshot is O(n).
/// <c>Replace</c> gives the new item the old item's sequence number, so it keeps its position;
/// removing an item and pushing it again appends it.
/// </remarks>
/// <typeparam name="T">Type of items in the source</typeparam>
internal sealed class IncrementalSource<T> : IIncrementalSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    // Items are never null (Push and Replace reject null), so T is a valid dictionary key.
#pragma warning disable CS8714
    private ImmutableDictionary<T, long> _sequenceByItem = ImmutableDictionary<T, long>.Empty;
#pragma warning restore CS8714
    private ImmutableSortedDictionary<long, T> _itemBySequence = ImmutableSortedDictionary<long, T>.Empty;
    private long _nextSequence;
    private readonly Func<ComputationGraph> _graphProvider;
    private readonly ICollectionComputationNode<T> _node;

    /// <summary>
    /// Unique identifier for this source.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Event raised when the source changes.
    /// The context subscribes to this and handles cache invalidation.
    /// </summary>
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
    }

    /// <summary>
    /// Gets the computation node for graph registration.
    /// Called by the context during initialization.
    /// </summary>
    public ICollectionComputationNode<T> GetNode() => _node;

    /// <summary>
    /// Returns a provider that exposes this source's values to the computation pipeline.
    /// </summary>
    public IncrementalValuesProvider<T> AsProvider()
    {
        return new IncrementalValuesProvider<T>(
            _node,
            _graphProvider(), // Get current graph from provider
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
            if (!_sequenceByItem.ContainsKey(item))
            {
                var sequence = _nextSequence++;
                _sequenceByItem = _sequenceByItem.Add(item, sequence);
                _itemBySequence = _itemBySequence.Add(sequence, item);
                changed = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Removes an item from the source. Triggers change notification.
    /// </summary>
    public void Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        bool changed = false;

        _lock.EnterWriteLock();
        try
        {
            if (_sequenceByItem.TryGetValue(item, out var sequence))
            {
                _sequenceByItem = _sequenceByItem.Remove(item);
                _itemBySequence = _itemBySequence.Remove(sequence);
                changed = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Replaces an item in the source. Triggers change notification.
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
            if (!_sequenceByItem.TryGetValue(oldItem, out var sequence))
                throw new InvalidOperationException($"Item to replace not found in source '{SourceId}'");

            if (_sequenceByItem.ContainsKey(newItem))
                throw new InvalidOperationException($"Replacement item already exists in source '{SourceId}'");

            // The new item takes the old item's place in the order
            _sequenceByItem = _sequenceByItem.Remove(oldItem).Add(newItem, sequence);
            _itemBySequence = _itemBySequence.SetItem(sequence, newItem);
            changed = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Clears all items from the source. Triggers change notification.
    /// </summary>
    public void Clear()
    {
        bool changed = false;

        _lock.EnterWriteLock();
        try
        {
            if (_sequenceByItem.IsEmpty)
                return;

            _sequenceByItem = _sequenceByItem.Clear();
            _itemBySequence = _itemBySequence.Clear();
            changed = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Gets a thread-safe snapshot of the current items, in insertion order.
    /// </summary>
    public IReadOnlyCollection<T> GetSnapshot()
    {
        ImmutableSortedDictionary<long, T> items;

        _lock.EnterReadLock();
        try
        {
            items = _itemBySequence;
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // The map is immutable, so it can be copied outside the lock
        var snapshot = new T[items.Count];
        var i = 0;
        foreach (var (_, item) in items)
        {
            snapshot[i++] = item;
        }

        return snapshot;
    }

    /// <summary>
    /// Notifies subscribers that the source has changed.
    /// Cache invalidation is handled by the context via the Changed event.
    /// </summary>
    private void NotifyChanged()
    {
        Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
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
            // Sources are the ground truth - always read fresh data, never use cache
            // The cache is for computed nodes (Select, Combine, etc.) that depend on sources
            var items = _source.GetSnapshot();
            return Result<IEnumerable<T>, ComputationError>.Success(items);
        }
    }
}
