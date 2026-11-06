using System.Runtime.CompilerServices;

namespace Guacamole;

/// <summary>
/// Discriminated union for the outcome of a pipeline computation, without exceptions.
/// A result is in exactly one of three states:
/// <list type="bullet">
/// <item><description><b>Success</b> (<see cref="IsSuccess"/>): a value was computed.</description></item>
/// <item><description><b>None</b> (<see cref="IsNone"/>): no value, and no error. Produced when a
/// single-value <c>Where</c> rejects its input; downstream operators propagate it without running
/// their lambdas. <c>default(Result&lt;TValue, TError&gt;)</c> is also None.</description></item>
/// <item><description><b>Error</b> (<see cref="IsError"/>): the computation failed.</description></item>
/// </list>
/// Immutable structure with pattern matching support for railway-oriented programming.
/// </summary>
/// <typeparam name="TValue">Type of the success value</typeparam>
/// <typeparam name="TError">Type of the error information</typeparam>
public readonly struct Result<TValue, TError> : IEquatable<Result<TValue, TError>>
{
    private enum State : byte
    {
        None = 0,
        Success = 1,
        Error = 2,
    }

    private readonly TValue? _value;
    private readonly TError? _error;
    private readonly State _state;

    /// <summary>
    /// Indicates whether this result holds a value.
    /// </summary>
    public bool IsSuccess => _state == State.Success;

    /// <summary>
    /// Indicates whether this result holds neither a value nor an error
    /// (for example, the input was filtered out by <c>Where</c>).
    /// </summary>
    public bool IsNone => _state == State.None;

    /// <summary>
    /// Indicates whether this result represents a failed computation.
    /// </summary>
    public bool IsError => _state == State.Error;

    /// <summary>
    /// The computed value when <see cref="IsSuccess"/> is true, otherwise default(TValue).
    /// </summary>
    public TValue? Value => IsSuccess ? _value : default;

    /// <summary>
    /// The error information when <see cref="IsError"/> is true, otherwise default(TError).
    /// </summary>
    public TError? ErrorValue => IsError ? _error : default;

    /// <summary>
    /// A result with no value and no error.
    /// </summary>
    public static Result<TValue, TError> None => default;

    /// <summary>
    /// Creates a Result in success state with the specified value.
    /// </summary>
    /// <param name="value">The success value</param>
    private Result(TValue value)
    {
        _state = State.Success;
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

        _state = State.Error;
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
        if (_state != other._state)
            return false;

        return _state switch
        {
            State.Success => EqualityComparer<TValue>.Default.Equals(_value, other._value),
            State.Error => EqualityComparer<TError>.Default.Equals(_error, other._error),
            _ => true,
        };
    }

    /// <summary>
    /// Determines whether this Result equals the specified object.
    /// </summary>
    public override bool Equals(object? obj) =>
        obj is Result<TValue, TError> other && Equals(other);

    /// <summary>
    /// Returns the hash code for this Result.
    /// </summary>
    public override int GetHashCode() => _state switch
    {
        State.Success => HashCode.Combine(_state, _value),
        State.Error => HashCode.Combine(_state, _error),
        _ => HashCode.Combine(_state),
    };

    /// <summary>
    /// Returns a string representation of this Result.
    /// </summary>
    public override string ToString() => _state switch
    {
        State.Success => $"Success({_value})",
        State.Error => $"Error({_error})",
        _ => "None",
    };

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
    /// None and error states are propagated unchanged without invoking the mapper.
    /// </summary>
    /// <typeparam name="TResult">Type of the transformed value</typeparam>
    /// <param name="mapper">Function to transform the success value</param>
    /// <returns>A new Result with the transformed value or the same error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TResult, TError> Map<TResult>(Func<TValue, TResult> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return _state switch
        {
            State.Success => Result<TResult, TError>.Success(mapper(_value!)),
            State.Error => Result<TResult, TError>.Error(_error!),
            _ => Result<TResult, TError>.None,
        };
    }

    /// <summary>
    /// Applies a function that returns a Result, allowing for chaining of Result-returning operations.
    /// Also known as FlatMap or chain. None and error states are propagated unchanged.
    /// </summary>
    /// <typeparam name="TResult">Type of the new success value</typeparam>
    /// <param name="binder">Function that returns a new Result</param>
    /// <returns>The Result from applying the binder, or the original error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TResult, TError> Bind<TResult>(Func<TValue, Result<TResult, TError>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return _state switch
        {
            State.Success => binder(_value!),
            State.Error => Result<TResult, TError>.Error(_error!),
            _ => Result<TResult, TError>.None,
        };
    }

    /// <summary>
    /// Pattern matching for Result. Executes one of two functions based on success/error state.
    /// Use the overload with an <c>onNone</c> handler for results that can be None.
    /// </summary>
    /// <typeparam name="TResult">Type of the result from both branches</typeparam>
    /// <param name="onSuccess">Function to execute if result is success</param>
    /// <param name="onError">Function to execute if result is error</param>
    /// <returns>The result of the executed function</returns>
    /// <exception cref="InvalidOperationException">The result is None.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        return _state switch
        {
            State.Success => onSuccess(_value!),
            State.Error => onError(_error!),
            _ => throw NoneNotHandled(),
        };
    }

    /// <summary>
    /// Pattern matching for Result. Executes one of three functions based on the state.
    /// </summary>
    /// <typeparam name="TResult">Type of the result from all branches</typeparam>
    /// <param name="onSuccess">Function to execute if result is success</param>
    /// <param name="onNone">Function to execute if result is None</param>
    /// <param name="onError">Function to execute if result is error</param>
    /// <returns>The result of the executed function</returns>
    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TResult> onNone, Func<TError, TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onNone);
        ArgumentNullException.ThrowIfNull(onError);

        return _state switch
        {
            State.Success => onSuccess(_value!),
            State.Error => onError(_error!),
            _ => onNone(),
        };
    }

    /// <summary>
    /// Pattern matching for Result with side effects. Executes one of two actions based on success/error state.
    /// Use the overload with an <c>onNone</c> handler for results that can be None.
    /// </summary>
    /// <param name="onSuccess">Action to execute if result is success</param>
    /// <param name="onError">Action to execute if result is error</param>
    /// <exception cref="InvalidOperationException">The result is None.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Match(Action<TValue> onSuccess, Action<TError> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        switch (_state)
        {
            case State.Success:
                onSuccess(_value!);
                break;
            case State.Error:
                onError(_error!);
                break;
            default:
                throw NoneNotHandled();
        }
    }

    /// <summary>
    /// Pattern matching for Result with side effects. Executes one of three actions based on the state.
    /// </summary>
    /// <param name="onSuccess">Action to execute if result is success</param>
    /// <param name="onNone">Action to execute if result is None</param>
    /// <param name="onError">Action to execute if result is error</param>
    public void Match(Action<TValue> onSuccess, Action onNone, Action<TError> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onNone);
        ArgumentNullException.ThrowIfNull(onError);

        switch (_state)
        {
            case State.Success:
                onSuccess(_value!);
                break;
            case State.Error:
                onError(_error!);
                break;
            default:
                onNone();
                break;
        }
    }

    /// <summary>
    /// Transforms the error value using the specified function.
    /// Success and None states are propagated unchanged.
    /// </summary>
    /// <typeparam name="TNewError">Type of the new error</typeparam>
    /// <param name="mapper">Function to transform the error value</param>
    /// <returns>A new Result with the same success value or transformed error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TValue, TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return _state switch
        {
            State.Success => Result<TValue, TNewError>.Success(_value!),
            State.Error => Result<TValue, TNewError>.Error(mapper(_error!)),
            _ => Result<TValue, TNewError>.None,
        };
    }

    /// <summary>
    /// Returns the success value, or a default value if the result is None or an error.
    /// </summary>
    /// <param name="defaultValue">Value to return if the result has no value</param>
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
    /// <exception cref="InvalidOperationException">The result is None (there is no error to compute a default from).</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TValue GetValueOrDefault(Func<TError, TValue> getDefault)
    {
        ArgumentNullException.ThrowIfNull(getDefault);

        return _state switch
        {
            State.Success => _value!,
            State.Error => getDefault(_error!),
            _ => throw NoneNotHandled(),
        };
    }

    private static InvalidOperationException NoneNotHandled() =>
        new("The result is None (it has neither a value nor an error). Check IsNone first, or use an overload that handles None.");
}
