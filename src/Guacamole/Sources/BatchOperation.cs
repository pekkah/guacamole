using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
    /// <remarks>
    /// The operations are applied in the order they were added, with the same semantics as the
    /// corresponding source methods, and the source raises a single change notification for all of
    /// them (none if none of them changed it). If an operation fails (for example
    /// <see cref="Replace"/> of an item that is not present), the operations before it stay applied
    /// and are notified, the rest are discarded, and the exception is rethrown; the batch counts as
    /// committed either way. For <see cref="IIncrementalSource{T}"/> implementations other than the
    /// sources created by a context, the operations are applied through the source's own methods,
    /// which notify once per change.
    /// </remarks>
    void Commit();

    /// <summary>
    /// Discards all batched operations without applying them.
    /// </summary>
    void Rollback();
}

/// <summary>
/// The kind of a buffered source operation.
/// </summary>
internal enum SourceOperationKind
{
    Push,
    Remove,
    Replace,
}

/// <summary>
/// A buffered source operation: <paramref name="Item"/> is the pushed or removed item, or the item to
/// replace; <paramref name="NewItem"/> is the replacement.
/// </summary>
internal readonly record struct SourceOperation<T>(SourceOperationKind Kind, T Item, T NewItem);

/// <summary>
/// Implementation of batch operations for IncrementalSource.
/// </summary>
/// <typeparam name="T">Type of items in the source</typeparam>
internal sealed class BatchOperation<T> : IBatchOperation<T>
{
    private readonly IIncrementalSource<T> _source;
    private readonly List<SourceOperation<T>> _operations = new();
    private bool _committed;
    private bool _disposed;

    internal BatchOperation(IIncrementalSource<T> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Push(T item)
    {
        ThrowIfDisposed();
        _operations.Add(new SourceOperation<T>(SourceOperationKind.Push, item, default!));
    }

    public void Remove(T item)
    {
        ThrowIfDisposed();
        _operations.Add(new SourceOperation<T>(SourceOperationKind.Remove, item, default!));
    }

    public void Replace(T oldItem, T newItem)
    {
        ThrowIfDisposed();
        _operations.Add(new SourceOperation<T>(SourceOperationKind.Replace, oldItem, newItem));
    }

    public void Commit()
    {
        ThrowIfDisposed();

        if (_committed)
            throw new InvalidOperationException("Batch already committed");

        // Committed even if an operation fails, so that Dispose does not apply the batch again
        _committed = true;
        try
        {
            if (_source is IncrementalSource<T> incremental)
            {
                // All operations in one step, with a single change notification
                incremental.Apply(CollectionsMarshal.AsSpan(_operations));
            }
            else
            {
                foreach (var operation in _operations)
                    ApplyOne(operation);
            }
        }
        finally
        {
            _operations.Clear();
        }
    }

    private void ApplyOne(SourceOperation<T> operation)
    {
        switch (operation.Kind)
        {
            case SourceOperationKind.Push:
                _source.Push(operation.Item);
                break;
            case SourceOperationKind.Remove:
                _source.Remove(operation.Item);
                break;
            default:
                _source.Replace(operation.Item, operation.NewItem);
                break;
        }
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

        try
        {
            // Auto-commit on dispose if not explicitly committed or rolled back
            if (!_committed && _operations.Count > 0)
            {
                Commit();
            }
        }
        finally
        {
            _disposed = true;
        }
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
    /// Pushes multiple items, appending them in enumeration order (items already present are skipped).
    /// </summary>
    /// <remarks>
    /// For the sources created by a context the items are added in one step with a single change
    /// notification (none if no item was added), and a <c>null</c> item is rejected before any item
    /// is added. Other <see cref="IIncrementalSource{T}"/> implementations receive one
    /// <see cref="IIncrementalSource{T}.Push"/> call per item.
    /// </remarks>
    public static void PushRange<T>(this IIncrementalSource<T> source, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(items);

        if (source is IncrementalSource<T> incremental)
        {
            incremental.PushRange(items);
            return;
        }

        using var batch = source.BeginBatch();
        foreach (var item in items)
        {
            batch.Push(item);
        }
        batch.Commit();
    }
}
