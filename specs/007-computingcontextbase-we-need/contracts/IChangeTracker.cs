using System;
using System.Collections.Generic;

namespace Guacamole.Context;

/// <summary>
/// Contract for tracking pending mutations before batch commit.
/// Supports Add/Remove/Replace operations with validation.
/// </summary>
public interface IChangeTracker
{
    /// <summary>
    /// Tracks an add operation for the specified property.
    /// </summary>
    /// <typeparam name="T">Type of item being added</typeparam>
    /// <param name="propertyName">Name of the target property</param>
    /// <param name="item">Item to add</param>
    /// <exception cref="ArgumentNullException">When propertyName or item is null</exception>
    void TrackAdd<T>(string propertyName, T item);

    /// <summary>
    /// Tracks a remove operation for the specified property.
    /// </summary>
    /// <typeparam name="T">Type of item being removed</typeparam>
    /// <param name="propertyName">Name of the target property</param>
    /// <param name="item">Item to remove</param>
    /// <exception cref="ArgumentNullException">When propertyName or item is null</exception>
    void TrackRemove<T>(string propertyName, T item);

    /// <summary>
    /// Tracks a replace operation for the specified property.
    /// </summary>
    /// <typeparam name="T">Type of item being replaced</typeparam>
    /// <param name="propertyName">Name of the target property</param>
    /// <param name="oldItem">Item to replace</param>
    /// <param name="newItem">Replacement item</param>
    /// <exception cref="ArgumentNullException">When propertyName, oldItem, or newItem is null</exception>
    void TrackReplace<T>(string propertyName, T oldItem, T newItem);

    /// <summary>
    /// Gets all pending changes in tracking order.
    /// </summary>
    /// <returns>Enumerable of pending changes</returns>
    IEnumerable<IChange> GetPendingChanges();

    /// <summary>
    /// Indicates if there are any pending changes.
    /// </summary>
    bool HasPendingChanges { get; }

    /// <summary>
    /// Clears all tracked changes without applying them.
    /// </summary>
    void Clear();

    /// <summary>
    /// Validates all pending changes for consistency and conflicts.
    /// </summary>
    /// <returns>Validation errors, empty if all changes are valid</returns>
    IEnumerable<string> ValidateChanges();
}