using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Threading;
using Guacamole.Pipeline;

namespace Guacamole;

/// <summary>
/// Thread-safe implementation of IIncrementalSource.
/// Raises change events when data changes; cache invalidation is handled by the context.
/// </summary>
/// <remarks>
/// <para>
/// Items have set semantics and are enumerated in insertion order. Items are compared with
/// <see cref="DefaultEqualityComparer{T}.Instance"/>: <see cref="EqualityComparer{T}.Default"/>, except
/// that <see cref="ImmutableArray{T}"/> items (and two- or three-component tuples holding one) are
/// compared element-wise, like everywhere else in the pipeline. They are kept in an array of slots in insertion order, with an index from item to
/// slot: <c>Push</c> appends a slot, <c>Replace</c> overwrites the old item's slot (so the new item
/// keeps its position), and <c>Remove</c> empties the slot; empty slots are compacted away once they
/// outnumber the items. All three are O(1) amortized. Removing an item and pushing it again appends it.
/// </para>
/// <para>
/// Mutations take a lock. Reads use an immutable snapshot array, built under the lock on the first
/// read after a change and shared by all reads until the next change, so reading an unchanged
/// source neither locks nor allocates. The snapshot is what the pipeline reads, and is never
/// modified once published.
/// </para>
/// </remarks>
/// <typeparam name="T">Type of items in the source</typeparam>
internal sealed class IncrementalSource<T> : IIncrementalSource<T>
{
    // Empty slots are compacted when there are at least this many and more than live items
    private const int MinCompactionGap = 32;

    // The item comparer (see the remarks)
    private static readonly IEqualityComparer<T> s_comparer = DefaultEqualityComparer<T>.Instance;

    // Whether s_comparer differs from EqualityComparer<T>.Default. When it does not, dictionaries get
    // no comparer and comparisons call EqualityComparer<T>.Default directly, which the JIT devirtualizes.
    private static readonly bool s_structural = !ReferenceEquals(s_comparer, EqualityComparer<T>.Default);

    private readonly object _lock = new();

    // Items are never null (Push and Replace reject null), so T is a valid dictionary key.
#pragma warning disable CS8714
    private readonly Dictionary<T, int> _slotByItem = new(s_structural ? s_comparer : null);
#pragma warning restore CS8714

    // Items in insertion order; slots at index >= _slotCount are unused, and slots of removed items
    // are empty (HasItem is false) until the next compaction
    private Slot[] _slots = [];
    private int _slotCount;

    // The items in order, or null when they changed since the last read (see GetItems)
    private T[]? _snapshot = [];

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

        bool changed;
        lock (_lock)
        {
            changed = PushCore(item);
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Appends the items that are not present yet, in order, with a single change notification
    /// (none if nothing was added). All items are checked for <c>null</c> before any is added.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or one of its items is <c>null</c>.</exception>
    internal void PushRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // Enumerate (user code) outside the lock
        var array = items as T[] ?? items.ToArray();
        foreach (var item in array)
            ArgumentNullException.ThrowIfNull(item, nameof(items));

        var changed = false;
        lock (_lock)
        {
            _slotByItem.EnsureCapacity(_slotByItem.Count + array.Length);
            if (_slotCount + array.Length > _slots.Length)
                Array.Resize(ref _slots, Math.Max(_slotCount + array.Length, _slots.Length * 2));

            foreach (var item in array)
                changed |= PushCore(item);
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Applies <paramref name="operations"/> in order, with the semantics of <see cref="Push"/>,
    /// <see cref="Remove"/> and <see cref="Replace"/>, all or nothing, and raises a single change
    /// notification if the source ends up changed (see <see cref="IBatchOperation{T}.Commit"/>).
    /// </summary>
    /// <remarks>
    /// The operations are first checked against the state they would see (without changing the
    /// source), so an operation that would fail (a <c>null</c> item, <c>Replace</c> of a missing item
    /// or with an item that is present) throws before any operation is applied. Once checked, the
    /// operations cannot fail. When every item they touch ends up present or absent as it was
    /// before (for example a <c>Push</c> of a new item and a <c>Remove</c> of it), the items before
    /// and after are compared, and if they are the same no notification is raised and the previous
    /// snapshot instance stays published.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An item is <c>null</c>; nothing was applied.</exception>
    /// <exception cref="InvalidOperationException">A <c>Replace</c> would fail; nothing was applied.</exception>
    internal void Apply(ReadOnlySpan<SourceOperation<T>> operations)
    {
        bool changed;
        lock (_lock)
        {
            var (effective, membershipChanged) = Check(operations);
            if (!effective)
                return;

            // Every touched item ends up present or absent as before: the order may be unchanged too
            var before = membershipChanged ? null : BuildSnapshot();

            foreach (var operation in operations)
            {
                switch (operation.Kind)
                {
                    case SourceOperationKind.Push:
                        PushCore(operation.Item);
                        break;
                    case SourceOperationKind.Remove:
                        RemoveCore(operation.Item);
                        break;
                    default:
                        if (!ItemEquals(operation.Item, operation.NewItem))
                            ReplaceCore(operation.Item, operation.NewItem);
                        break;
                }
            }

            changed = true;
            if (before is not null)
            {
                var after = BuildSnapshot();
                changed = !SameItems(before, after);

                // The items are the same: keep publishing the previous snapshot instance, so that
                // readers holding it see no change at all
                if (!changed)
                    Volatile.Write(ref _snapshot, before);
            }
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Checks that <paramref name="operations"/> can be applied in order, without changing the
    /// source. Returns whether any of them would change it and whether any item would end up
    /// present when it was absent before, or the reverse. Lock held.
    /// </summary>
    private (bool Effective, bool MembershipChanged) Check(ReadOnlySpan<SourceOperation<T>> operations)
    {
        // The presence of each item touched so far, before the batch and after the operations so far
#pragma warning disable CS8714 // items are checked for null before they are used as keys
        Dictionary<T, (bool Before, bool Now)>? touched = null;
#pragma warning restore CS8714
        var effective = false;

        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case SourceOperationKind.Push:
                    ArgumentNullException.ThrowIfNull(operation.Item, "item");
                    if (!IsPresent(operation.Item))
                    {
                        SetPresent(operation.Item, true);
                        effective = true;
                    }

                    break;
                case SourceOperationKind.Remove:
                    ArgumentNullException.ThrowIfNull(operation.Item, "item");
                    if (IsPresent(operation.Item))
                    {
                        SetPresent(operation.Item, false);
                        effective = true;
                    }

                    break;
                default:
                    ArgumentNullException.ThrowIfNull(operation.Item, "oldItem");
                    ArgumentNullException.ThrowIfNull(operation.NewItem, "newItem");
                    if (ItemEquals(operation.Item, operation.NewItem))
                        break;

                    if (!IsPresent(operation.Item))
                        throw new InvalidOperationException($"Item to replace not found in source '{SourceId}'");
                    if (IsPresent(operation.NewItem))
                        throw new InvalidOperationException($"Replacement item already exists in source '{SourceId}'");

                    SetPresent(operation.Item, false);
                    SetPresent(operation.NewItem, true);
                    effective = true;
                    break;
            }
        }

        var membershipChanged = false;
        if (touched != null)
        {
            foreach (var (before, now) in touched.Values)
                membershipChanged |= before != now;
        }

        return (effective, membershipChanged);

        bool IsPresent(T item) =>
            touched != null && touched.TryGetValue(item, out var state) ? state.Now : _slotByItem.ContainsKey(item);

        void SetPresent(T item, bool present)
        {
            touched ??= new(s_structural ? s_comparer : null);
            var before = touched.TryGetValue(item, out var state) ? state.Before : _slotByItem.ContainsKey(item);
            touched[item] = (before, present);
        }
    }

    /// <summary>
    /// Removes an item from the source. Triggers change notification.
    /// </summary>
    public void Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        bool changed;
        lock (_lock)
        {
            changed = RemoveCore(item);
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

        if (ItemEquals(oldItem, newItem))
            return; // No change

        lock (_lock)
        {
            ReplaceCore(oldItem, newItem);
        }

        NotifyChanged();
    }

    /// <summary>
    /// Clears all items from the source. Triggers change notification.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_slotByItem.Count == 0)
                return;

            _slotByItem.Clear();
            _slots = [];
            _slotCount = 0;
            _snapshot = [];
        }

        NotifyChanged();
    }

    /// <summary>
    /// Gets a thread-safe snapshot of the current items, in insertion order.
    /// </summary>
    /// <remarks>
    /// A read-only view of the shared snapshot array, so that callers cannot modify what the
    /// pipeline reads.
    /// </remarks>
    public IReadOnlyCollection<T> GetSnapshot() => new ReadOnlyCollection<T>(GetItems());

    /// <summary>
    /// The current items in insertion order, as an array shared by all reads until the next change.
    /// Callers must not modify it.
    /// </summary>
    internal T[] GetItems()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null)
            return snapshot;

        lock (_lock)
        {
            return BuildSnapshot();
        }
    }

    /// <summary>
    /// Returns the snapshot of the current items, building and publishing it if the items changed
    /// since it was last built. Lock held.
    /// </summary>
    private T[] BuildSnapshot()
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            snapshot = new T[_slotByItem.Count];
            var count = 0;
            for (var i = 0; i < _slotCount; i++)
            {
                if (_slots[i].HasItem)
                    snapshot[count++] = _slots[i].Item;
            }

            Volatile.Write(ref _snapshot, snapshot);
        }

        return snapshot;
    }

    /// <summary>
    /// Appends <paramref name="item"/> unless it is present; returns whether it was added. Lock held.
    /// </summary>
    private bool PushCore(T item)
    {
        if (!_slotByItem.TryAdd(item, _slotCount))
            return false;

        if (_slotCount == _slots.Length)
            Array.Resize(ref _slots, Math.Max(4, _slots.Length * 2));

        _slots[_slotCount++] = new Slot(item);
        _snapshot = null;
        return true;
    }

    /// <summary>
    /// Removes <paramref name="item"/> if present; returns whether it was removed. Lock held.
    /// </summary>
    private bool RemoveCore(T item)
    {
        if (!_slotByItem.Remove(item, out var slot))
            return false;

        _slots[slot] = default; // also releases the item
        _snapshot = null;

        var empty = _slotCount - _slotByItem.Count;
        if (empty >= MinCompactionGap && empty > _slotByItem.Count)
            Compact();

        return true;
    }

    /// <summary>
    /// Puts <paramref name="newItem"/> in <paramref name="oldItem"/>'s slot. Lock held.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="oldItem"/> is missing or
    /// <paramref name="newItem"/> is present.</exception>
    private void ReplaceCore(T oldItem, T newItem)
    {
        if (!_slotByItem.TryGetValue(oldItem, out var slot))
            throw new InvalidOperationException($"Item to replace not found in source '{SourceId}'");

        if (_slotByItem.ContainsKey(newItem))
            throw new InvalidOperationException($"Replacement item already exists in source '{SourceId}'");

        // The new item takes the old item's place in the order
        _slotByItem.Remove(oldItem);
        _slotByItem.Add(newItem, slot);
        _slots[slot] = new Slot(newItem);
        _snapshot = null;
    }

    /// <summary>
    /// Moves the items to the front of the slot array, keeping their order. Lock held.
    /// </summary>
    private void Compact()
    {
        var count = 0;
        for (var i = 0; i < _slotCount; i++)
        {
            if (!_slots[i].HasItem)
                continue;

            if (count != i)
            {
                _slots[count] = _slots[i];
                _slots[i] = default;
            }

            _slotByItem[_slots[count].Item] = count;
            count++;
        }

        _slotCount = count;
        if (_slots.Length > 4 * Math.Max(count, 4))
            Array.Resize(ref _slots, Math.Max(4, count * 2));
    }

    private static bool ItemEquals(T x, T y) =>
        s_structural ? s_comparer.Equals(x, y) : EqualityComparer<T>.Default.Equals(x, y);

    private static bool SameItems(T[] x, T[] y)
    {
        if (!s_structural)
            return x.AsSpan().SequenceEqual(y, EqualityComparer<T>.Default);

        if (x.Length != y.Length)
            return false;

        for (var i = 0; i < x.Length; i++)
        {
            if (!s_comparer.Equals(x[i], y[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Notifies subscribers that the source has changed.
    /// Cache invalidation is handled by the context via the Changed event.
    /// </summary>
    private void NotifyChanged()
    {
        Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
    }

    private readonly struct Slot(T item)
    {
        public T Item { get; } = item;

        public bool HasItem { get; } = true;
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
            // Sources are the ground truth - always read the current items, never the value cache.
            // The cache is for computed nodes (Select, Combine, etc.) that depend on sources.
            return Result<IEnumerable<T>, ComputationError>.Success(_source.GetItems());
        }
    }
}
