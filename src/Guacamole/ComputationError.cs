namespace Guacamole;

/// <summary>
/// Error information for failed computations.
/// </summary>
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
