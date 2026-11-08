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
    /// <exception cref="InvalidOperationException">The batch was committed.</exception>
    /// <exception cref="ObjectDisposedException">The batch was disposed.</exception>
    void Push(T item);

    /// <summary>
    /// Removes an item in the batch. Does not trigger invalidation yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">The batch was committed.</exception>
    /// <exception cref="ObjectDisposedException">The batch was disposed.</exception>
    void Remove(T item);

    /// <summary>
    /// Replaces an item in the batch. Does not trigger invalidation yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">The batch was committed.</exception>
    /// <exception cref="ObjectDisposedException">The batch was disposed.</exception>
    void Replace(T oldItem, T newItem);

    /// <summary>
    /// Commits all batched operations with a single invalidation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operations are applied in the order they were added, with the same semantics as the
    /// corresponding source methods, all or nothing, and the source raises a single change
    /// notification if it ends up changed: none if no operation changed it, or if the operations
    /// cancel out (for example a <see cref="Push"/> of a new item followed by a <see cref="Remove"/>
    /// of it).
    /// </para>
    /// <para>
    /// If an operation would fail (a <c>null</c> item, or <see cref="Replace"/> of an item that is
    /// not present at that point of the batch, or with an item that is), <c>Commit</c> throws without
    /// applying any operation and without a notification. The batch counts as committed either way:
    /// it cannot be committed again, operations added afterwards throw
    /// <see cref="InvalidOperationException"/>, and disposing it does nothing.
    /// </para>
    /// <para>
    /// For <see cref="IIncrementalSource{T}"/> implementations other than the sources created by a
    /// context, the operations are applied one by one through the source's own methods, which notify
    /// once per change; a failing operation then leaves the operations before it applied.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The batch was already committed, or a
    /// <see cref="Replace"/> would fail (nothing was applied).</exception>
    /// <exception cref="ArgumentNullException">An item is <c>null</c> (nothing was applied).</exception>
    /// <exception cref="ObjectDisposedException">The batch was disposed.</exception>
    void Commit();

    /// <summary>
    /// Discards all batched operations without applying them. The batch can be used again
    /// afterwards. Does nothing after <see cref="Commit"/> (nothing is pending then).
    /// </summary>
    /// <exception cref="ObjectDisposedException">The batch was disposed.</exception>
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

    public void Push(T item) => Add(new SourceOperation<T>(SourceOperationKind.Push, item, default!));

    public void Remove(T item) => Add(new SourceOperation<T>(SourceOperationKind.Remove, item, default!));

    public void Replace(T oldItem, T newItem) => Add(new SourceOperation<T>(SourceOperationKind.Replace, oldItem, newItem));

    private void Add(SourceOperation<T> operation)
    {
        ThrowIfDisposed();

        // An operation added after Commit would never be applied
        if (_committed)
            throw new InvalidOperationException("Batch already committed; begin a new batch for further operations");

        _operations.Add(operation);
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
                // All operations in one step, all or nothing, with a single change notification
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

        // After Commit nothing is pending (so that a Rollback in an error handler after a failed
        // Commit does not hide the Commit's exception)
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
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <c>null</c>.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="items"/> or
    /// one of the items is <c>null</c>.</exception>
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
