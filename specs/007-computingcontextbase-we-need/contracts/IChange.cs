using System;

namespace Guacamole.Context;

/// <summary>
/// Contract for a single tracked change operation.
/// Represents Add, Remove, or Replace mutations.
/// </summary>
public interface IChange
{
    /// <summary>
    /// Name of the property being modified.
    /// </summary>
    string PropertyName { get; }

    /// <summary>
    /// Type of change operation.
    /// </summary>
    ChangeType ChangeType { get; }

    /// <summary>
    /// When the change was tracked.
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// The item being modified (for Add/Remove) or new item (for Replace).
    /// </summary>
    object Item { get; }

    /// <summary>
    /// The old item being replaced (null for Add/Remove operations).
    /// </summary>
    object? OldItem { get; }

    /// <summary>
    /// Type of the item being modified.
    /// </summary>
    Type ItemType { get; }
}

/// <summary>
/// Enumeration of change operation types.
/// </summary>
public enum ChangeType
{
    Add,
    Remove,
    Replace
}

/// <summary>
/// Strongly-typed change for specific item types.
/// </summary>
/// <typeparam name="T">Type of item being changed</typeparam>
public interface IChange<T> : IChange
{
    /// <summary>
    /// Strongly-typed item being modified.
    /// </summary>
    new T Item { get; }

    /// <summary>
    /// Strongly-typed old item for replace operations.
    /// </summary>
    new T? OldItem { get; }
}