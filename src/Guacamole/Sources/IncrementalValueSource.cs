using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Guacamole.Core.Common;
using Guacamole.Core.Diagnostics;
using Guacamole.Core.Pipeline;

namespace Guacamole.Sources;

/// <summary>
/// Thread-safe implementation of IIncrementalValueSource for single values.
/// Raises change events when the value changes; cache invalidation is handled by the context.
/// </summary>
/// <typeparam name="T">Type of value in the source</typeparam>
public sealed class IncrementalValueSource<T> : IIncrementalValueSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private T? _value;
    private bool _hasValue;
    private readonly Func<ComputationGraph> _graphProvider;
    private readonly IComputationNode<T> _node;

    /// <summary>
    /// Unique identifier for this source.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Event raised when the source changes.
    /// The context subscribes to this and handles cache invalidation.
    /// </summary>
    public event EventHandler<SourceChangedEventArgs>? Changed;

    /// <summary>
    /// Creates a new incremental value source.
    /// </summary>
    /// <param name="sourceId">Unique identifier for this source</param>
    /// <param name="graphProvider">Function that provides access to the current computation graph</param>
    public IncrementalValueSource(string sourceId, Func<ComputationGraph> graphProvider)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _graphProvider = graphProvider ?? throw new ArgumentNullException(nameof(graphProvider));

        // Create the computation node for this source
        _node = new SourceValueNode(sourceId, this);
    }

    /// <summary>
    /// Gets the computation node for graph registration.
    /// Called by the context during initialization.
    /// </summary>
    internal IComputationNode<T> GetNode() => _node;

    /// <summary>
    /// Returns a provider that exposes this source's value to the computation pipeline.
    /// </summary>
    public IncrementalValueProvider<T> AsProvider()
    {
        return new IncrementalValueProvider<T>(
            _node,
            _graphProvider(), // Get current graph from provider
            new ProviderMetadata($"Source: {SourceId}", "Push-based single value source"));
    }

    /// <summary>
    /// Sets the value of the source. Triggers change notification.
    /// </summary>
    public void Set(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        bool changed = false;

        _lock.EnterWriteLock();
        try
        {
            // Only update if value actually changed
            if (!_hasValue || !EqualityComparer<T>.Default.Equals(_value, value))
            {
                _value = value;
                _hasValue = true;
                changed = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// Gets the current value (thread-safe read).
    /// </summary>
    public T? GetValue()
    {
        _lock.EnterReadLock();
        try
        {
            return _value;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Notifies subscribers that the source has changed.
    /// Cache invalidation is handled by the context via the Changed event.
    /// </summary>
    private void NotifyChanged()
    {
        Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
    }

    /// <summary>
    /// Internal computation node that reads from the source.
    /// This node has no dependencies (sources are the root of the graph).
    /// </summary>
    private sealed class SourceValueNode : IComputationNode<T>
    {
        private readonly IncrementalValueSource<T> _source;

        public string Id { get; }
        public Type OutputType => typeof(T);
        public ImmutableHashSet<string> Dependencies => ImmutableHashSet<string>.Empty; // Sources have no dependencies!

        public SourceValueNode(string id, IncrementalValueSource<T> source)
        {
            Id = id;
            _source = source;
        }

        public Result<T, ComputationError> Compute(ComputationContext context)
        {
            // Check cache first
            if (context.CachedValues.TryGetValue(Id, out var cached) && cached is T cachedValue)
            {
                return Result<T, ComputationError>.Success(cachedValue);
            }

            // Read current value from source
            var value = _source.GetValue();
            if (value == null)
            {
                return Result<T, ComputationError>.Error(
                    new ComputationError($"Source '{Id}' has no value set"));
            }

            return Result<T, ComputationError>.Success(value);
        }
    }
}
