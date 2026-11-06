using System;

namespace Guacamole;

/// <summary>
/// Event args for source change notifications.
/// </summary>
public sealed class SourceChangedEventArgs : EventArgs
{
    /// <summary>
    /// The identifier of the source that changed.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// The timestamp when the change occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Creates a new instance of SourceChangedEventArgs.
    /// </summary>
    /// <param name="sourceId">The identifier of the source that changed</param>
    /// <param name="timestamp">The timestamp when the change occurred</param>
    public SourceChangedEventArgs(string sourceId, DateTime timestamp)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        Timestamp = timestamp;
    }
}
