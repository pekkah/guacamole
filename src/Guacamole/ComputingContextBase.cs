using System;
using System.Threading;
using Guacamole.Core.Pipeline;

namespace Guacamole;

/// <summary>
/// Minimal abstract base class for user-defined incremental computation contexts.
/// Users inherit from this class and define properties as IncrementalValueProvider types.
/// Source generators automatically extend this with mutation methods, change tracking, and computation nodes.
/// </summary>
/// <remarks>
/// This base class provides only the essential infrastructure:
/// - Computation graph for dependency tracking
/// - Thread synchronization via ReaderWriterLockSlim (multiple readers, single writer)
/// - Disposal state management
/// All context-specific functionality (mutation methods, change tracking, SaveChanges) is generated at compile time.
/// </remarks>
public abstract class ComputingContextBase : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Shared computation graph for dependency management and incremental recomputation.
    /// </summary>
    protected ComputationGraph Graph { get; set; }

    /// <summary>
    /// Thread synchronization lock (multiple readers, single writer model).
    /// </summary>
    protected ReaderWriterLockSlim Lock { get; }

    /// <summary>
    /// Indicates if the context has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Creates a new computing context with dependency injection support.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies</param>
    protected ComputingContextBase(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        Graph = new ComputationGraph();
        Lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    }

    /// <summary>
    /// Disposes the context and releases resources.
    /// Generated code will override this to clear change tracking.
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;

        Lock?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Throws if the context has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }
}
