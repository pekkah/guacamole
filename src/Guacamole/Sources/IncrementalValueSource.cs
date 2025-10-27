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
/// Automatically invalidates the computation graph cache when the value changes.
/// </summary>
/// <typeparam name="T">Type of value in the source</typeparam>
public sealed class IncrementalValueSource<T> : IIncrementalValueSource<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private T? _value;
    private bool _hasValue;
    private ComputationGraph _graph;
    private readonly IComputationNode<T> _node;

    /// <summary>
    /// Unique identifier for this source.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Event raised when the source changes.
    /// </summary>
    public event EventHandler<SourceChangedEventArgs>? Changed;

    /// <summary>
    /// Creates a new incremental value source.
    /// </summary>
    /// <param name="sourceId">Unique identifier for this source</param>
    /// <param name="graph">The computation graph to integrate with</param>
    public IncrementalValueSource(string sourceId, ComputationGraph graph)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));

        // Create the computation node for this source
        _node = new SourceValueNode(sourceId, this);

        // Register node in the graph
        _graph = _graph.AddNode(_node);
    }

    /// <summary>
    /// Returns a provider that exposes this source's value to the computation pipeline.
    /// </summary>
    public IncrementalValueProvider<T> AsProvider()
    {
        return new IncrementalValueProvider<T>(
            _node,
            _graph,
            new ProviderMetadata($"Source: {SourceId}", "Push-based single value source"));
    }

    /// <summary>
    /// Sets the value of the source. Triggers automatic invalidation and recomputation.
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
            InvalidateCacheAndNotify();
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
    /// Invalidates the computation cache and notifies observers.
    /// This causes dependent computations to be recomputed automatically.
    /// </summary>
    private void InvalidateCacheAndNotify()
    {
        // Remove this node from cache
        _graph.ValueCache.Remove(SourceId);

        // Find and invalidate all dependent nodes transitively
        var dependents = FindAllDependents();
        foreach (var dependent in dependents)
        {
            _graph.ValueCache.Remove(dependent);
        }

        // Notify observers (context will trigger output handlers)
        Changed?.Invoke(this, new SourceChangedEventArgs(SourceId, DateTime.UtcNow));
    }

    /// <summary>
    /// Finds all nodes that depend on this source (transitively).
    /// </summary>
    private HashSet<string> FindAllDependents()
    {
        var dependents = new HashSet<string>();
        var toCheck = new Queue<string>();
        toCheck.Enqueue(SourceId);

        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();

            // Find all nodes that directly depend on current
            foreach (var kvp in _graph.Dependencies)
            {
                if (kvp.Value.Contains(current) && !dependents.Contains(kvp.Key))
                {
                    dependents.Add(kvp.Key);
                    toCheck.Enqueue(kvp.Key);
                }
            }
        }

        return dependents;
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
