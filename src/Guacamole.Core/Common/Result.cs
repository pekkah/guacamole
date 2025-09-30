using System.Runtime.CompilerServices;

namespace Guacamole.Core.Common;

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
}