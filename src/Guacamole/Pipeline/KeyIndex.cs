using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Implemented by dictionary nodes that know which keys changed between their previous result and
/// their latest one, so that a <see cref="KeyIndex{TKey, TValue}"/> does not have to compare its keys
/// one by one (<c>CollectToDictionary</c> knows them from the item edits it applied).
/// </summary>
internal interface IDictionaryChanges<TKey, TValue>
    where TKey : notnull
{
    /// <summary>
    /// The equality comparer of the node's dictionaries (and of the keys
    /// <see cref="TryGetChangedKeys"/> returns).
    /// </summary>
    IEqualityComparer<TKey> KeyComparer { get; }

    /// <summary>
    /// Returns the keys whose entries differ between <paramref name="previous"/> and
    /// <paramref name="current"/> (added, removed, or with a different value), if the node knows
    /// them; <see langword="false"/> otherwise.
    /// </summary>
    bool TryGetChangedKeys(ImmutableDictionary<TKey, TValue> previous, ImmutableDictionary<TKey, TValue> current, out IReadOnlyCollection<TKey> keys);
}

/// <summary>
/// The per-key readers of one dictionary node (issue #95): a key handle for each key that has been
/// read with <see cref="GraphEvaluator.ReadKey"/>. Kept by the dictionary node's handle
/// (<c>NodeHandle&lt;TResult&gt;.KeyIndex</c>), which calls <see cref="OnCommit"/> whenever it commits a
/// result.
/// </summary>
internal abstract class KeyIndex
{
    // Versions that match no recorded version (see KeyHandle.VersionAt): -2, -3, ...
    private static long s_unmatchedVersions = -1;

    /// <summary>
    /// Returns a version that is different from every version recorded so far and from every
    /// version returned later: a dependency that records it is never found up to date.
    /// </summary>
    internal static long UnmatchedVersion() => Interlocked.Decrement(ref s_unmatchedVersions);

    /// <summary>
    /// Called by the dictionary node's commit with <see cref="SharedGraphState.CommitLock"/> held,
    /// after the commit has decided the node's output version and before it stores (or removes) the
    /// cached value.
    /// </summary>
    /// <param name="state">The graph state</param>
    /// <param name="kind">What the committed result is</param>
    /// <param name="stored">The stored value (the dictionary) when <paramref name="kind"/> is a value</param>
    /// <param name="cacheable">Whether the result is cached (see <see cref="GraphEvaluator.FailedReads"/>)</param>
    /// <param name="version">The node's output version for this result</param>
    /// <param name="changed">Whether the version was incremented (the result differs from the previous one)</param>
    /// <returns>
    /// The revision that the key change advanced <see cref="SharedGraphState.Revision"/> to, if the
    /// commit changed tracked keys (see <see cref="SharedGraphState.MarkKeysChanged"/>); 0 otherwise.
    /// The committing pass accounts for its own key change with it (see
    /// <see cref="GraphEvaluator.AccountForOwnKeyChange"/>).
    /// </returns>
    public abstract long OnCommit(SharedGraphState state, CachedResultKind kind, object? stored, bool cacheable, long version, bool changed);
}

/// <summary>
/// The per-key readers of a dictionary node producing <see cref="ImmutableDictionary{TKey, TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Versions.</b> Each key handle records the dictionary output version at which its key's entry
/// last changed, and the index the version at which the dictionary's status last changed (from a
/// dictionary to an error, no value, or a result that is not cached, or back). A key's version is
/// the later of the two. A per-key reader records the key's version as of the dictionary result it
/// read (see <see cref="KeyHandle{TKey, TValue}.VersionAt"/>), so it is invalidated exactly when its
/// key's entry or the dictionary's status changed since.
/// </para>
/// <para>
/// <b>Commits.</b> On each commit that changes the dictionary, <see cref="OnCommit"/> compares the
/// new dictionary with the previously committed one, for the tracked keys only: through the node's
/// own list of changed keys when it has one (<see cref="IDictionaryChanges{TKey, TValue}"/>, O(changed
/// keys)), otherwise key by key (O(tracked keys)). It records the new version on the changed keys and
/// then marks the nodes downstream of their key states dirty, as a change of their own
/// (<see cref="SharedGraphState.MarkKeysChanged"/>), before the commit stores the dictionary. A status
/// change marks the readers of every tracked key. The previously committed dictionary is kept here,
/// not taken from the value cache, so an evicted value does not cost a status change.
/// </para>
/// <para>
/// The index holds its key handles strongly for as long as the dictionary node lives (the per-key
/// readers' cached values reference them weakly, like any dependency), so a key that was looked up
/// once is tracked until the dictionary node is collected.
/// </para>
/// </remarks>
internal sealed class KeyIndex<TKey, TValue> : KeyIndex
    where TKey : notnull
{
    private static readonly IEqualityComparer<TValue> s_valueComparer = DefaultEqualityComparer<TValue>.Instance;

    private readonly ValueNodeHandle<ImmutableDictionary<TKey, TValue>> _dictionary;
    private readonly IDictionaryChanges<TKey, TValue>? _changes;

    // Added to under SharedGraphState.CommitLock only; read without it
    private readonly ConcurrentDictionary<TKey, KeyHandle<TKey, TValue>> _keys;

    // The latest committed dictionary, or null after a result that is not a cached dictionary;
    // accessed under SharedGraphState.CommitLock
    private ImmutableDictionary<TKey, TValue>? _committed;
    private long _statusChangedAt;
    private int _nextKeyId;

    private KeyIndex(ValueNodeHandle<ImmutableDictionary<TKey, TValue>> dictionary, ComputationGraph graph)
    {
        _dictionary = dictionary;
        _changes = dictionary.Node as IDictionaryChanges<TKey, TValue>;
        _keys = new ConcurrentDictionary<TKey, KeyHandle<TKey, TValue>>(_changes?.KeyComparer ?? DefaultEqualityComparer<TKey>.Instance);

        // The dictionary committed last, if it is still cached (otherwise the next commit is a status change)
        if (graph.ValueCache.TryGet(dictionary.Id, out var cached)
            && cached is CachedNodeValue { Kind: CachedResultKind.Value, Value: ImmutableDictionary<TKey, TValue> committed } entry
            && entry.OutputVersion == dictionary.State.Version)
        {
            _committed = committed;
        }
    }

    /// <summary>
    /// The dictionary node's handle (the gate of every per-key reader, see <see cref="NodeHandle.Gates"/>).
    /// </summary>
    public ValueNodeHandle<ImmutableDictionary<TKey, TValue>> Dictionary => _dictionary;

    /// <summary>
    /// The dictionary output version at which the dictionary's status last changed (see the remarks).
    /// </summary>
    public long StatusChangedAt => Volatile.Read(ref _statusChangedAt);

    /// <summary>
    /// The number of tracked keys (for tests).
    /// </summary>
    internal int KeyCount => _keys.Count;

    /// <summary>
    /// Returns the key handle of <paramref name="key"/> in the dictionary node of
    /// <paramref name="dictionary"/>, creating the index and the handle on first use.
    /// </summary>
    public static KeyHandle<TKey, TValue> GetKeyHandle(ValueNodeHandle<ImmutableDictionary<TKey, TValue>> dictionary, TKey key, ComputationGraph graph)
    {
        if (dictionary.KeyIndex is KeyIndex<TKey, TValue> index && index._keys.TryGetValue(key, out var handle))
            return handle;

        // Under the commit lock, so that no commit runs between reading the dictionary's version
        // and tracking the key (see Add)
        lock (graph.SharedState.CommitLock)
        {
            if (dictionary.KeyIndex is not KeyIndex<TKey, TValue> existing)
            {
                existing = new KeyIndex<TKey, TValue>(dictionary, graph);
                dictionary.KeyIndex = existing;
            }

            return existing._keys.TryGetValue(key, out handle) ? handle : existing.Add(key);
        }
    }

    // Commit lock held
    private KeyHandle<TKey, TValue> Add(TKey key)
    {
        // Untracked until now, the key may have changed at any commit so far: its version is the
        // dictionary's current one (that of the latest commit)
        var state = new NodeState($"{_dictionary.Id}[key {_nextKeyId++}]");
        var handle = new KeyHandle<TKey, TValue>(this, state, _dictionary.State.Version);
        _keys[key] = handle;
        return handle;
    }

    /// <inheritdoc />
    public override long OnCommit(SharedGraphState state, CachedResultKind kind, object? stored, bool cacheable, long version, bool changed)
    {
        var current = cacheable && kind == CachedResultKind.Value ? stored as ImmutableDictionary<TKey, TValue> : null;
        if (!changed)
        {
            // Equal to the committed result (by the node's comparer): nothing changed for any key
            _committed ??= current;
            return 0;
        }

        var previous = _committed;
        _committed = current;

        List<NodeState>? changedKeys = null;
        if (previous is null || current is null)
        {
            // A status change: every tracked key may read differently
            Volatile.Write(ref _statusChangedAt, version);
            foreach (var (_, handle) in _keys)
                (changedKeys ??= []).Add(handle.State);
        }
        else if (!ReferenceEquals(previous, current))
        {
            if (_changes != null && _changes.TryGetChangedKeys(previous, current, out var keys))
            {
                foreach (var key in keys)
                {
                    if (_keys.TryGetValue(key, out var handle))
                        Changed(handle);
                }
            }
            else
            {
                foreach (var (key, handle) in _keys)
                {
                    if (EntryChanged(previous, current, key))
                        Changed(handle);
                }
            }
        }

        return changedKeys is null ? 0 : state.MarkKeysChanged(changedKeys);

        void Changed(KeyHandle<TKey, TValue> handle)
        {
            // The version first: a pass that sees a reader marked also sees the key's new version
            handle.SetChangedAt(version);
            (changedKeys ??= []).Add(handle.State);
        }
    }

    private static bool EntryChanged(ImmutableDictionary<TKey, TValue> previous, ImmutableDictionary<TKey, TValue> current, TKey key)
    {
        var had = previous.TryGetValue(key, out var before);
        var has = current.TryGetValue(key, out var after);
        return had != has || (had && !s_valueComparer.Equals(before, after));
    }
}

/// <summary>
/// One key of a dictionary node, as a dependency: its <see cref="NodeHandle.State"/> holds the
/// per-key readers as dependents (so the dictionary node can mark them when the key's entry
/// changes), and refreshing it brings the dictionary node up to date and returns the key's version.
/// </summary>
internal sealed class KeyHandle<TKey, TValue> : NodeHandle
    where TKey : notnull
{
    private readonly KeyIndex<TKey, TValue> _index;
    private long _changedAt;

    public KeyHandle(KeyIndex<TKey, TValue> index, NodeState state, long changedAt)
        : base(state)
    {
        _index = index;
        _changedAt = changedAt;
        InitializeGates([index.Dictionary]);
    }

    /// <summary>
    /// The dictionary node (a key has no node of its own).
    /// </summary>
    public override IComputationNode Node => _index.Dictionary.Node;

    /// <summary>
    /// Records that the key's entry changed in the dictionary result of output version
    /// <paramref name="version"/>. Commit lock held.
    /// </summary>
    public void SetChangedAt(long version) => Volatile.Write(ref _changedAt, version);

    /// <summary>
    /// The key's version as of the dictionary result of output version <paramref name="dictionaryVersion"/>:
    /// the version at which the key's entry or the dictionary's status last changed, if that is not
    /// later than <paramref name="dictionaryVersion"/> (the key then reads the same in that result as
    /// in every result since); otherwise, when a later commit has already changed the key, a version
    /// that matches nothing, so that the reader is computed again.
    /// </summary>
    public long VersionAt(long dictionaryVersion)
    {
        var changedAt = Math.Max(Volatile.Read(ref _changedAt), _index.StatusChangedAt);
        return changedAt <= dictionaryVersion ? changedAt : KeyIndex.UnmatchedVersion();
    }

    /// <summary>
    /// Brings the dictionary node up to date and returns the key's version.
    /// </summary>
    public override long Refresh(GraphEvaluator evaluator) => VersionAt(_index.Dictionary.Refresh(evaluator));
}
