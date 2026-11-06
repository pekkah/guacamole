using Xunit;

namespace Guacamole.Tests.Common;

/// <summary>
/// Tests for Result discriminated union following TDD progression:
/// Basic → Edge → Incremental → Performance
/// </summary>
public class ResultTests
{
    /// <summary>
    /// BASIC: Test that Result can represent success state
    /// </summary>
    [Fact]
    public void Result_ShouldCreate_SuccessState()
    {
        // Act - This will fail until Result<TValue, TError> is implemented
        var result = Result<string, Exception>.Success("test-value");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("test-value", result.Value);
        Assert.Null(result.ErrorValue);
    }

    /// <summary>
    /// BASIC: Test that Result can represent error state
    /// </summary>
    [Fact]
    public void Result_ShouldCreate_ErrorState()
    {
        // Arrange
        var error = new InvalidOperationException("test error");

        // Act
        var result = Result<string, Exception>.Error(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Same(error, result.ErrorValue);
    }

    /// <summary>
    /// BASIC: Test that Result enforces exactly one of Value or Error is populated
    /// </summary>
    [Fact]
    public void Result_ShouldEnforce_MutualExclusivity()
    {
        // Arrange & Act
        var successResult = Result<int, string>.Success(42);
        var errorResult = Result<int, string>.Error("failure");

        // Assert - Success has value but not error
        Assert.True(successResult.IsSuccess);
        Assert.Equal(42, successResult.Value);
        Assert.Null(successResult.ErrorValue);

        // Error has error but not value
        Assert.False(errorResult.IsSuccess);
        Assert.Equal(default(int), errorResult.Value);
        Assert.Equal("failure", errorResult.ErrorValue);
    }

    /// <summary>
    /// BASIC: Test Result with different value and error types
    /// </summary>
    [Theory]
    [InlineData(123)]
    [InlineData("test")]
    [InlineData(true)]
    public void Result_ShouldSupport_DifferentTypes<T>(T value)
    {
        // Act
        var successResult = Result<T, string>.Success(value);
        var errorResult = Result<T, string>.Error("error");

        // Assert
        Assert.True(successResult.IsSuccess);
        Assert.Equal(value, successResult.Value);

        Assert.False(errorResult.IsSuccess);
        Assert.Equal("error", errorResult.ErrorValue);
    }

    /// <summary>
    /// EDGE: Test Result validation for constructor arguments
    /// </summary>
    [Fact]
    public void Result_Success_ShouldValidate_Arguments()
    {
        // Act & Assert - Null success value should be allowed for reference types
        var nullResult = Result<string?, Exception>.Success(null);
        Assert.True(nullResult.IsSuccess);
        Assert.Null(nullResult.Value);

        // But error should not be null
        Assert.Throws<ArgumentNullException>(() =>
            Result<string, Exception>.Error(null!));
    }

    /// <summary>
    /// EDGE: Test Result with nullable reference types
    /// </summary>
    [Fact]
    public void Result_ShouldHandle_NullableReferenceTypes()
    {
        // Act
        var nullSuccess = Result<string?, ArgumentException>.Success(null);
        var valueSuccess = Result<string?, ArgumentException>.Success("value");
        var error = Result<string?, ArgumentException>.Error(new ArgumentException("error"));

        // Assert
        Assert.True(nullSuccess.IsSuccess);
        Assert.Null(nullSuccess.Value);

        Assert.True(valueSuccess.IsSuccess);
        Assert.Equal("value", valueSuccess.Value);

        Assert.False(error.IsSuccess);
        Assert.NotNull(error.ErrorValue);
    }

    /// <summary>
    /// EDGE: Test Result equality and hash code behavior
    /// </summary>
    [Fact]
    public void Result_ShouldImplement_EqualityCorrectly()
    {
        // Arrange
        var success1 = Result<int, string>.Success(42);
        var success2 = Result<int, string>.Success(42);
        var success3 = Result<int, string>.Success(99);
        var error1 = Result<int, string>.Error("fail");
        var error2 = Result<int, string>.Error("fail");

        // Assert - Same success values are equal
        Assert.Equal(success1, success2);
        Assert.Equal(success1.GetHashCode(), success2.GetHashCode());

        // Different success values are not equal
        Assert.NotEqual(success1, success3);

        // Same error values are equal
        Assert.Equal(error1, error2);
        Assert.Equal(error1.GetHashCode(), error2.GetHashCode());

        // Success and error are never equal
        Assert.NotEqual(success1, error1);
    }

    /// <summary>
    /// INCREMENTAL: Test Result supports pattern matching for railway-oriented programming
    /// </summary>
    [Fact]
    public void Result_ShouldSupport_PatternMatching()
    {
        // Arrange
        var success = Result<int, string>.Success(42);
        var error = Result<int, string>.Error("failed");

        // Act & Assert - Should support switch expressions
        var successMessage = success switch
        {
            { IsSuccess: true, Value: var value } => $"Success: {value}",
            { IsSuccess: false, ErrorValue: var err } => $"Error: {err}"
        };

        var errorMessage = error switch
        {
            { IsSuccess: true, Value: var value } => $"Success: {value}",
            { IsSuccess: false, ErrorValue: var err } => $"Error: {err}"
        };

        Assert.Equal("Success: 42", successMessage);
        Assert.Equal("Error: failed", errorMessage);
    }

    /// <summary>
    /// INCREMENTAL: Test Result can be used in pipeline transformations
    /// </summary>
    [Fact]
    public void Result_ShouldSupport_PipelineTransformations()
    {
        // Arrange
        var success = Result<int, string>.Success(10);
        var error = Result<int, string>.Error("calculation failed");

        // Act - Transform success result
        var transformedSuccess = success.IsSuccess
            ? Result<string, string>.Success($"Value: {success.Value}")
            : Result<string, string>.Error(success.ErrorValue!);

        // Transform error result
        var transformedError = error.IsSuccess
            ? Result<string, string>.Success($"Value: {error.Value}")
            : Result<string, string>.Error(error.ErrorValue!);

        // Assert
        Assert.True(transformedSuccess.IsSuccess);
        Assert.Equal("Value: 10", transformedSuccess.Value);

        Assert.False(transformedError.IsSuccess);
        Assert.Equal("calculation failed", transformedError.ErrorValue);
    }

    /// <summary>
    /// INCREMENTAL: Test Result prevents exception propagation in transformation chains
    /// </summary>
    [Fact]
    public void Result_ShouldPrevent_ExceptionPropagation()
    {
        // Arrange - Simulate a computation that could throw
        static Result<int, Exception> DivideByZero(int value)
        {
            try
            {
                return Result<int, Exception>.Success(10 / value);
            }
            catch (Exception ex)
            {
                return Result<int, Exception>.Error(ex);
            }
        }

        // Act
        var validResult = DivideByZero(2);
        var errorResult = DivideByZero(0);

        // Assert - No exceptions thrown, errors captured in Result
        Assert.True(validResult.IsSuccess);
        Assert.Equal(5, validResult.Value);

        Assert.False(errorResult.IsSuccess);
        Assert.IsType<DivideByZeroException>(errorResult.ErrorValue);
    }

    /// <summary>
    /// PERFORMANCE: Test that Result creation and access is fast
    /// </summary>
    [Fact]
    public void Result_Operations_ShouldBe_Fast()
    {
        // Act - Measure Result creation and access
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < 1000; i++)
        {
            var result = Result<int, string>.Success(i);
            var value = result.Value;
            var isSuccess = result.IsSuccess;
        }

        stopwatch.Stop();

        // Assert - Should be very fast for struct operations
        Assert.True(stopwatch.ElapsedMilliseconds < 5,
            $"1000 Result operations took {stopwatch.ElapsedMilliseconds}ms, expected <5ms");
    }

    /// <summary>
    /// PERFORMANCE: Test Result memory usage is minimal
    /// </summary>
    [Fact]
    public void Result_ShouldHave_MinimalMemoryOverhead()
    {
        // Arrange - Create results without allocating additional memory
        var success = Result<long, string>.Success(123L);
        var error = Result<long, string>.Error("error");

        // Assert - Results should be value types (structs)
        Assert.True(success.GetType().IsValueType, "Result should be a value type");
        Assert.True(error.GetType().IsValueType, "Result should be a value type");

        // Size should be reasonable for the contained data
        var successSize = System.Runtime.InteropServices.Marshal.SizeOf(success);
        Assert.True(successSize > 0, "Result should have measurable size");
    }
    /// <summary>
    /// BASIC: None is a third state, distinct from success and error
    /// </summary>
    [Fact]
    public void Result_ShouldCreate_NoneState()
    {
        var none = Result<int, string>.None;

        Assert.True(none.IsNone);
        Assert.False(none.IsSuccess);
        Assert.False(none.IsError);
        Assert.Equal(default, none.Value);
        Assert.Null(none.ErrorValue);
        Assert.Equal("None", none.ToString());
    }

    [Fact]
    public void Result_SuccessAndError_AreNotNone()
    {
        var success = Result<int, string>.Success(1);
        var error = Result<int, string>.Error("e");

        Assert.False(success.IsNone);
        Assert.False(success.IsError);
        Assert.False(error.IsNone);
        Assert.True(error.IsError);
    }

    [Fact]
    public void Result_Default_IsNone()
    {
        Assert.True(default(Result<int, string>).IsNone);
    }

    [Fact]
    public void Result_None_ShouldImplement_EqualityCorrectly()
    {
        Assert.Equal(Result<int, string>.None, Result<int, string>.None);
        Assert.Equal(Result<int, string>.None.GetHashCode(), Result<int, string>.None.GetHashCode());
        Assert.NotEqual(Result<int, string>.None, Result<int, string>.Success(0));
        Assert.NotEqual(Result<int, string>.None, Result<int, string>.Error("e"));
    }

    [Fact]
    public void Result_None_PropagatesThroughTransformationsWithoutInvokingThem()
    {
        var none = Result<int, string>.None;
        var calls = 0;

        var mapped = none.Map(x => { calls++; return x + 1; });
        var bound = none.Bind(x => { calls++; return Result<int, string>.Success(x); });
        var mappedError = none.MapError(e => { calls++; return e.Length; });

        Assert.True(mapped.IsNone);
        Assert.True(bound.IsNone);
        Assert.True(mappedError.IsNone);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Result_Match_WithNoneHandler_DispatchesAllThreeStates()
    {
        string Describe(Result<int, string> r) => r.Match(v => $"value {v}", () => "none", e => $"error {e}");

        Assert.Equal("value 1", Describe(Result<int, string>.Success(1)));
        Assert.Equal("none", Describe(Result<int, string>.None));
        Assert.Equal("error x", Describe(Result<int, string>.Error("x")));

        var seen = new List<string>();
        Result<int, string>.None.Match(_ => seen.Add("value"), () => seen.Add("none"), _ => seen.Add("error"));
        Assert.Equal(new[] { "none" }, seen);
    }

    [Fact]
    public void Result_Match_WithoutNoneHandler_ThrowsForNone()
    {
        var none = Result<int, string>.None;

        Assert.Throws<InvalidOperationException>(() => none.Match(v => v, e => -1));
        Assert.Throws<InvalidOperationException>(() => none.Match(_ => { }, _ => { }));
        Assert.Throws<InvalidOperationException>(() => none.GetValueOrDefault(e => -1));
    }

    [Fact]
    public void Result_GetValueOrDefault_ReturnsDefaultForNone()
    {
        Assert.Equal(7, Result<int, string>.None.GetValueOrDefault(7));
    }
}
