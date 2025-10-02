using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Guacamole.Context;

/// <summary>
/// Contract for computing context lifecycle and operations.
/// Represents the public interface for user-defined contexts.
/// </summary>
public interface IComputingContext : IDisposable
{
    /// <summary>
    /// Change tracker for batching mutations before commit.
    /// </summary>
    IChangeTracker ChangeTracker { get; }

    /// <summary>
    /// Indicates if there are pending changes waiting to be committed.
    /// </summary>
    bool HasPendingChanges { get; }

    /// <summary>
    /// Commits all tracked changes and triggers incremental recomputation.
    /// Fails fast on first error with detailed diagnostics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Task representing the async commit operation</returns>
    /// <exception cref="ContextOperationException">When commit fails</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous version of SaveChangesAsync.
    /// </summary>
    /// <exception cref="ContextOperationException">When commit fails</exception>
    void SaveChanges();

    /// <summary>
    /// Discards all pending changes without committing.
    /// </summary>
    void DiscardChanges();

    /// <summary>
    /// Validates pending changes without committing them.
    /// </summary>
    /// <returns>Validation results with any detected issues</returns>
    IEnumerable<string> ValidatePendingChanges();
}