using System.Runtime.CompilerServices;

namespace Guacamole;

/// <summary>
/// Discriminated union for handling success/error states in pipeline transformations without exceptions.
/// Immutable structure with pattern matching support for railway-oriented programming.
/// </summary>
/// <typeparam name="TValue">Type of the success value</typeparam>
/// <typeparam name="TError">Type of the error information</typeparam>
public readonly struct Result<TValue, TError> : IEquatable<Result<TValue, TError>>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    /// <summary>
    /// Indicates whether this result represents a success state.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The computed value when IsSuccess = true, otherwise default(TValue).
    /// </summary>
    public TValue? Value => IsSuccess ? _value : default;

    /// <summary>
    /// The error information when IsSuccess = false, otherwise default(TError).
    /// </summary>
    public TError? ErrorValue => IsSuccess ? default : _error;

    /// <summary>
    /// Creates a Result in success state with the specified value.
    /// </summary>
    /// <param name="value">The success value</param>
    private Result(TValue value)
    {
        IsSuccess = true;
        _value = value;
        _error = default;
    }

    /// <summary>
    /// Creates a Result in error state with the specified error.
    /// </summary>
    /// <param name="error">The error information</param>
    /// <exception cref="ArgumentNullException">Thrown when error is null</exception>
    private Result(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IsSuccess = false;
        _value = default;
        _error = error;
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The success value</param>
    /// <returns>A Result in success state</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> Success(TValue value) => new(value);

    /// <summary>
    /// Creates an error result with the specified error.
    /// </summary>
    /// <param name="error">The error information</param>
    /// <returns>A Result in error state</returns>
    /// <exception cref="ArgumentNullException">Thrown when error is null</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> Error(TError error) => new(error);

    /// <summary>
    /// Determines whether two Result instances are equal.
    /// </summary>
    public bool Equals(Result<TValue, TError> other)
    {
        if (IsSuccess != other.IsSuccess)
            return false;

        return IsSuccess
            ? EqualityComparer<TValue>.Default.Equals(_value, other._value)
            : EqualityComparer<TError>.Default.Equals(_error, other._error);
    }

    /// <summary>
    /// Determines whether this Result equals the specified object.
    /// </summary>
    public override bool Equals(object? obj) =>
        obj is Result<TValue, TError> other && Equals(other);

    /// <summary>
    /// Returns the hash code for this Result.
    /// </summary>
    public override int GetHashCode()
    {
        if (IsSuccess)
        {
            return HashCode.Combine(IsSuccess, _value);
        }
        else
        {
            return HashCode.Combine(IsSuccess, _error);
        }
    }

    /// <summary>
    /// Returns a string representation of this Result.
    /// </summary>
    public override string ToString() =>
        IsSuccess ? $"Success({_value})" : $"Error({_error})";

    /// <summary>
    /// Equality operator for Result instances.
    /// </summary>
    public static bool operator ==(Result<TValue, TError> left, Result<TValue, TError> right) =>
        left.Equals(right);

    /// <summary>
    /// Inequality operator for Result instances.
    /// </summary>
    public static bool operator !=(Result<TValue, TError> left, Result<TValue, TError> right) =>
        !left.Equals(right);

    /// <summary>
    /// Transforms the success value using the specified function.
    /// Error state is propagated unchanged.
    /// </summary>
    /// <typeparam name="TResult">Type of the transformed value</typeparam>
    /// <param name="mapper">Function to transform the success value</param>
    /// <returns>A new Result with the transformed value or the same error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TResult, TError> Map<TResult>(Func<TValue, TResult> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TResult, TError>.Success(mapper(_value!))
            : Result<TResult, TError>.Error(_error!);
    }

    /// <summary>
    /// Applies a function that returns a Result, allowing for chaining of Result-returning operations.
    /// Also known as FlatMap or chain. Error state is propagated unchanged.
    /// </summary>
    /// <typeparam name="TResult">Type of the new success value</typeparam>
    /// <param name="binder">Function that returns a new Result</param>
    /// <returns>The Result from applying the binder, or the original error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TResult, TError> Bind<TResult>(Func<TValue, Result<TResult, TError>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return IsSuccess
            ? binder(_value!)
            : Result<TResult, TError>.Error(_error!);
    }

    /// <summary>
    /// Pattern matching for Result. Executes one of two functions based on success/error state.
    /// </summary>
    /// <typeparam name="TResult">Type of the result from both branches</typeparam>
    /// <param name="onSuccess">Function to execute if result is success</param>
    /// <param name="onError">Function to execute if result is error</param>
    /// <returns>The result of the executed function</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        return IsSuccess
            ? onSuccess(_value!)
            : onError(_error!);
    }

    /// <summary>
    /// Pattern matching for Result with side effects. Executes one of two actions based on success/error state.
    /// </summary>
    /// <param name="onSuccess">Action to execute if result is success</param>
    /// <param name="onError">Action to execute if result is error</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Match(Action<TValue> onSuccess, Action<TError> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        if (IsSuccess)
            onSuccess(_value!);
        else
            onError(_error!);
    }

    /// <summary>
    /// Transforms the error value using the specified function.
    /// Success state is propagated unchanged.
    /// </summary>
    /// <typeparam name="TNewError">Type of the new error</typeparam>
    /// <param name="mapper">Function to transform the error value</param>
    /// <returns>A new Result with the same success value or transformed error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TValue, TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TValue, TNewError>.Success(_value!)
            : Result<TValue, TNewError>.Error(mapper(_error!));
    }

    /// <summary>
    /// Returns the success value or a default value if in error state.
    /// </summary>
    /// <param name="defaultValue">Value to return if in error state</param>
    /// <returns>The success value or the default value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TValue GetValueOrDefault(TValue defaultValue)
    {
        return IsSuccess ? _value! : defaultValue;
    }

    /// <summary>
    /// Returns the success value or computes a default value using the error if in error state.
    /// </summary>
    /// <param name="getDefault">Function to compute default value from error</param>
    /// <returns>The success value or the computed default value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TValue GetValueOrDefault(Func<TError, TValue> getDefault)
    {
        ArgumentNullException.ThrowIfNull(getDefault);

        return IsSuccess ? _value! : getDefault(_error!);
    }
}
