using System;
using System.Collections.Generic;

namespace Guacamole;

/// <summary>
/// Represents a source of data that can be pushed into the incremental computation graph.
/// Sources are the entry points for external data (file changes, network events, etc.)
/// and automatically invalidate the computation cache when data changes.
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
    /// </summary>
    /// <param name="item">The item to add</param>
    void Push(T item);

    /// <summary>
    /// Removes an item from the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    /// <param name="item">The item to remove</param>
    void Remove(T item);

    /// <summary>
    /// Replaces an item in the source. Thread-safe, triggers invalidation automatically.
    /// </summary>
    /// <param name="oldItem">The item to replace</param>
    /// <param name="newItem">The new item</param>
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
    /// <param name="value">The new value</param>
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
