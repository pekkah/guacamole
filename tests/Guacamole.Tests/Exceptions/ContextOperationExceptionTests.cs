using System;
using Xunit;

namespace Guacamole.Tests.Exceptions;

/// <summary>
/// Tests for ContextOperationException exception types.
/// Validates error diagnostic information and exception handling.
/// </summary>
public class ContextOperationExceptionTests
{
    [Fact]
    public void DuplicateItem_CreatesExceptionWithCorrectDetails()
    {
        // Act
        var exception = ContextOperationException.DuplicateItem("TestProperty", "test-item");

        // Assert
        Assert.Equal("Add", exception.OperationType);
        Assert.Equal("TestProperty", exception.PropertyName);
        Assert.Equal("test-item", exception.FailedItem);
        Assert.Equal(0, exception.OperationIndex);
        Assert.Contains("TestProperty", exception.Message);
        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public void BatchOperationFailed_IncludesOperationIndex()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = ContextOperationException.BatchOperationFailed(
            "Add", "TestProperty", 5, "failed-item", innerException);

        // Assert
        Assert.Equal("Add", exception.OperationType);
        Assert.Equal("TestProperty", exception.PropertyName);
        Assert.Equal("failed-item", exception.FailedItem);
        Assert.Equal(5, exception.OperationIndex);
        Assert.Contains("index 5", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void Constructor_PreservesAllProperties()
    {
        // Arrange
        var innerException = new Exception("Inner");

        // Act
        var exception = new ContextOperationException(
            "Remove",
            "Items",
            "Test message",
            "failed-item",
            3,
            innerException);

        // Assert
        Assert.Equal("Remove", exception.OperationType);
        Assert.Equal("Items", exception.PropertyName);
        Assert.Equal("Test message", exception.Message);
        Assert.Equal("failed-item", exception.FailedItem);
        Assert.Equal(3, exception.OperationIndex);
        Assert.Same(innerException, exception.InnerException);
    }
}
