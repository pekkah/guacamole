namespace Guacamole;

/// <summary>
/// Error information for failed computations.
/// </summary>
/// <remarks>
/// A failed result is cached like a value, with the versions of the inputs it was computed from, so
/// a failing stage runs again only when one of its inputs changes. When it fails again, the new error
/// is compared with the cached one by this record's value equality (type, <see cref="Message"/>,
/// <see cref="InnerError"/> chain, <see cref="SourceLocation"/>, <see cref="DiagnosticId"/>): an
/// equal error keeps the stages downstream of it cached, like an equal value. Errors built with
/// <see cref="FromException"/> keep the exception messages, not the exceptions or their types.
/// </remarks>
/// <param name="Message">Error message</param>
/// <param name="InnerError">Nested error if any</param>
/// <param name="SourceLocation">Location where error occurred</param>
/// <param name="DiagnosticId">Unique diagnostic identifier</param>
public record class ComputationError(
    string Message,
    ComputationError? InnerError = null,
    SourceLocation? SourceLocation = null,
    string? DiagnosticId = null)
{
    /// <summary>
    /// Creates a ComputationError from an exception: its message, with the chain of inner exceptions
    /// as <see cref="InnerError"/>s.
    /// </summary>
    /// <param name="exception">The exception</param>
    /// <param name="diagnosticId">Optional diagnostic identifier of the top-level error</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <c>null</c>.</exception>
    public static ComputationError FromException(Exception exception, string? diagnosticId = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ComputationError(
            Message: exception.Message,
            InnerError: exception.InnerException != null
                ? FromException(exception.InnerException)
                : null,
            DiagnosticId: diagnosticId);
    }
}
