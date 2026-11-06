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
    /// Creates a ComputationError from an exception.
    /// </summary>
    public static ComputationError FromException(Exception exception, string? diagnosticId = null)
    {
        return new ComputationError(
            Message: exception.Message,
            InnerError: exception.InnerException != null
                ? FromException(exception.InnerException)
                : null,
            DiagnosticId: diagnosticId);
    }
}
