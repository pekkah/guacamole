using System;
using System.Collections.Generic;

namespace Guacamole;

/// <summary>
/// Represents a source of data that can be pushed into the incremental computation graph.
/// Sources are the entry points for external data (file changes, network events, etc.)
/// and automatically invalidate the computation cache when data changes.
/// </summary>
/// <remarks>
/// A collection source has set semantics (no duplicates, using the item's equality, except that an
/// <see cref="System.Collections.Immutable.ImmutableArray{T}"/> item is compared element-wise and a
/// two- or three-component value tuple holding one component-wise, as in the pipeline's operators)
/// and keeps its items in insertion order: <see cref="GetSnapshot"/> and the provider returned by
/// <see cref="AsProvider"/> enumerate items in the order they were pushed, so downstream results such
/// as <c>Collect()</c> are deterministic. <see cref="Replace"/> keeps the replaced item's position;
/// removing an item and pushing it again moves it to the end.
/// <para>
/// Once the context that created the source has been disposed, the operations that change the source
/// (<see cref="Push"/>, <see cref="Remove"/>, <see cref="Replace"/>, <see cref="Clear"/>, and
/// <c>PushRange</c> and batch <c>Commit</c>) throw <see cref="ObjectDisposedException"/>;
/// <see cref="GetSnapshot"/> still returns the current items.
/// </para>
/// </remarks>
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
    /// Adds an item to the end of the source. Pushing an item that is already present is a no-op.
    /// Thread-safe, triggers invalidation automatically.
    /// </summary>
    /// <param name="item">The item to add</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <c>null</c>.</exception>
    void Push(T item);

    /// <summary>
    /// Removes an item from the source. Removing an item that is not present is a no-op.
    /// Thread-safe, triggers invalidation automatically.
    /// </summary>
    /// <param name="item">The item to remove</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <c>null</c>.</exception>
    void Remove(T item);

    /// <summary>
    /// Replaces an item in the source; the new item takes the old item's position.
    /// Thread-safe, triggers invalidation automatically.
    /// </summary>
    /// <remarks>
    /// Replacing a present item with an equal one is a no-op, without a notification. Replacing an
    /// item that is not present throws, also when <paramref name="newItem"/> equals it.
    /// </remarks>
    /// <param name="oldItem">The item to replace</param>
    /// <param name="newItem">The new item</param>
    /// <exception cref="ArgumentNullException"><paramref name="oldItem"/> or <paramref name="newItem"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="oldItem"/> is not present, or
    /// <paramref name="newItem"/> is already present (nothing is changed).</exception>
    void Replace(T oldItem, T newItem);

    /// <summary>
    /// Clears all items from the source. Clearing an empty source is a no-op.
    /// Thread-safe, triggers invalidation automatically.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets the current snapshot of items in insertion order (thread-safe read). The snapshot is
    /// read-only and does not change when the source changes later.
    /// </summary>
    IReadOnlyCollection<T> GetSnapshot();

    /// <summary>
    /// Event raised when the source changes, once per effective change (operations that leave the
    /// source unchanged raise nothing), on the thread that changed it, after the change was applied.
    /// The context subscribes to this to trigger output handlers automatically.
    /// </summary>
    event EventHandler<SourceChangedEventArgs>? Changed;
}

/// <summary>
/// Represents a single-value source (non-collection).
/// </summary>
/// <remarks>
/// Once the context that created the source has been disposed, <see cref="Set"/> throws
/// <see cref="ObjectDisposedException"/>; <see cref="GetValue"/> still returns the current value.
/// </remarks>
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
    /// Sets the value of the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    /// <remarks>
    /// Setting a value equal to the current one does nothing (no notification, no recomputation).
    /// Values are compared with their default equality, except that an
    /// <see cref="System.Collections.Immutable.ImmutableArray{T}"/> is compared element-wise and a
    /// two- or three-component value tuple holding one is compared component-wise, as in the
    /// pipeline's operators.
    /// </remarks>
    /// <param name="value">The new value</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The context that created the source has been disposed.</exception>
    void Set(T value);

    /// <summary>
    /// Gets the current value (thread-safe read), or <c>default</c> if <see cref="Set"/> was never
    /// called. (The provider returned by <see cref="AsProvider"/> reports a never-set source as a
    /// failed result instead, also for value types.)
    /// </summary>
    T? GetValue();

    /// <summary>
    /// Event raised when the source changes.
    /// The context subscribes to this to trigger output handlers automatically.
    /// </summary>
    event EventHandler<SourceChangedEventArgs>? Changed;
}
