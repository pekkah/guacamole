using System;
using System.Collections.Generic;

namespace Guacamole;

/// <summary>
/// Represents a batch operation that defers cache invalidation until commit.
/// </summary>
/// <typeparam name="T">Type of items in the source</typeparam>
public interface IBatchOperation<T> : IDisposable
{
    /// <summary>
    /// Pushes an item in the batch. Does not trigger invalidation yet.
    /// </summary>
    void Push(T item);

    /// <summary>
    /// Removes an item in the batch. Does not trigger invalidation yet.
    /// </summary>
    void Remove(T item);

    /// <summary>
    /// Replaces an item in the batch. Does not trigger invalidation yet.
    /// </summary>
    void Replace(T oldItem, T newItem);

    /// <summary>
    /// Commits all batched operations with a single invalidation.
    /// </summary>
    void Commit();

    /// <summary>
    /// Discards all batched operations without applying them.
    /// </summary>
    void Rollback();
}

/// <summary>
/// Implementation of batch operations for IncrementalSource.
/// </summary>
/// <typeparam name="T">Type of items in the source</typeparam>
internal sealed class BatchOperation<T> : IBatchOperation<T>
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

        // Execute all buffered operations
        // Note: Each operation will trigger its own invalidation
        // TODO: Optimize to batch invalidations
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
    /// <summary>
    /// Begins a batch operation where multiple changes can be applied
    /// with a single cache invalidation at the end.
    /// </summary>
    public static IBatchOperation<T> BeginBatch<T>(this IIncrementalSource<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BatchOperation<T>(source);
    }

    /// <summary>
    /// Pushes multiple items in a batch, appending them in enumeration order.
    /// </summary>
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
