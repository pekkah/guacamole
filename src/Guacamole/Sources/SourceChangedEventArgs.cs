using System;
using System.Threading;
using Guacamole.Pipeline;

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
    /// When the change occurred (UTC, as recorded by the source).
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Creates a new instance of SourceChangedEventArgs.
    /// </summary>
    /// <param name="sourceId">The identifier of the source that changed</param>
    /// <param name="timestamp">The timestamp when the change occurred</param>
    public SourceChangedEventArgs(string sourceId, DateTimeOffset timestamp)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        Timestamp = timestamp;
    }

    /// <summary>
    /// Creates the event args of a change that a source is about to make, and records in
    /// <paramref name="state"/> that a source change has started (see
    /// <see cref="SharedGraphState.BeginSourceChange"/>). The change counts as in progress until
    /// <see cref="EndPendingChange"/>; the context's handler ends it once it has marked the source
    /// changed, and the source ends it in any case once the notification returns (or the mutation
    /// turned out to change nothing, or threw).
    /// </summary>
    /// <remarks>
    /// The change starts before the source publishes its new contents (issue #99): an evaluation pass
    /// that can read the new contents before the change has advanced the source's version and the
    /// graph revision then sees the change in progress (see <see cref="GraphEvaluator.OverlapsChange"/>).
    /// </remarks>
    internal SourceChangedEventArgs(string sourceId, DateTimeOffset timestamp, SharedGraphState state)
        : this(sourceId, timestamp)
    {
        state.BeginSourceChange();
        _pendingState = state;
    }

    // The graph state whose source change this notification still holds open; null once ended
    private SharedGraphState? _pendingState;

    /// <summary>
    /// Whether this notification still holds a source change open (see the internal constructor).
    /// </summary>
    internal bool HasPendingChange => Volatile.Read(ref _pendingState) is not null;

    /// <summary>
    /// Ends the source change this notification holds open, if it has not been ended yet. Idempotent
    /// and thread-safe.
    /// </summary>
    internal void EndPendingChange() => Interlocked.Exchange(ref _pendingState, null)?.EndSourceChange();
}
