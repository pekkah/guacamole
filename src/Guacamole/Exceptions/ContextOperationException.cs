using System;

namespace Guacamole;

/// <summary>
/// Exception thrown when computing context operations fail.
/// Provides detailed diagnostic information for troubleshooting.
/// </summary>
public class ContextOperationException : Exception
{
    /// <summary>
    /// Type of operation that failed (Add/Remove/Replace/Commit).
    /// </summary>
    public string OperationType { get; }

    /// <summary>
    /// Name of the property involved in the failed operation.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The item that caused the failure (if applicable).
    /// </summary>
    public object? FailedItem { get; }

    /// <summary>
    /// Index of the operation in a batch (for batch failures).
    /// </summary>
    public int OperationIndex { get; }

    /// <summary>
    /// Creates a new context operation exception.
    /// </summary>
    /// <param name="operationType">Type of operation that failed</param>
    /// <param name="propertyName">Property involved in the operation</param>
    /// <param name="message">Error message</param>
    /// <param name="failedItem">Item that caused the failure</param>
    /// <param name="operationIndex">Index in batch operation</param>
    /// <param name="innerException">Underlying exception</param>
    public ContextOperationException(
        string operationType,
        string propertyName,
        string message,
        object? failedItem = null,
        int operationIndex = 0,
        Exception? innerException = null)
        : base(message, innerException)
    {
        OperationType = operationType;
        PropertyName = propertyName;
        FailedItem = failedItem;
        OperationIndex = operationIndex;
    }

    /// <summary>
    /// Creates an exception for duplicate item errors.
    /// </summary>
    public static ContextOperationException DuplicateItem<T>(string propertyName, T item)
    {
        return new ContextOperationException(
            "Add",
            propertyName,
            $"Item of type {typeof(T).Name} already exists in property {propertyName}",
            item);
    }

    /// <summary>
    /// Creates an exception for batch operation failures.
    /// </summary>
    public static ContextOperationException BatchOperationFailed(
        string operationType,
        string propertyName,
        int failedIndex,
        object? failedItem,
        Exception innerException)
    {
        return new ContextOperationException(
            operationType,
            propertyName,
            $"Batch {operationType.ToLower()} operation failed at index {failedIndex}",
            failedItem,
            failedIndex,
            innerException);
    }
}
