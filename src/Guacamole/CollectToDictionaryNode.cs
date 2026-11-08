using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Guacamole.Pipeline;

// Null items are never used as dictionary keys here (they are counted separately), so T is a
// valid key type
#pragma warning disable CS8714

namespace Guacamole;

/// <summary>
/// <c>CollectToDictionary</c>: maps each item of a collection to a key and a value and keeps the
/// dictionary of them up to date in O(K) per K item edits, from its input's item-level delta (see
/// the design note in Pipeline/ArrayDelta.cs), with a full fold when no delta is available.
/// </summary>
/// <remarks>
/// <para>
/// The node keeps, for each key, its candidates: the distinct values (by
/// <see cref="DefaultEqualityComparer{T}.Instance"/>) that items map it to, each with its number of
/// items. A key with one candidate maps to it; a key with several maps to the smallest by the
/// conflict comparer, or is a conflict (the result is an error) when there is none. So the result
/// depends only on the multiset of (key, value) pairs of the items, not on their order, on the order
/// of the edits, or on whether it was folded in full or updated from deltas.
/// </para>
/// <para>
/// The (key, value) pair of each distinct item is memoized with its number of occurrences (items are
/// matched with <see cref="DefaultEqualityComparer{T}.Instance"/>, like the per-item memo of the
/// other collection operators), so the selectors run once per new item, and a removed item's pair
/// is known without running them again. Applying K edits removes the pairs of the removed items,
/// adds those of the inserted ones, and settles each key they touched in a builder of the previous
/// dictionary: O(K) map and dictionary operations, with no per-item array. When no key's value
/// changed, the previous dictionary instance is the result. The node remembers the transition from
/// its previous result to its new one, so the change check (<see cref="TryEquals"/>) knows a new
/// instance differs without comparing it entry by entry.
/// </para>
/// <para>
/// Thread safety follows the per-item memo: the computation that holds the node (<see cref="_busy"/>)
/// updates the memo and candidates in place and publishes its state (immutable); a computation
/// running concurrently folds the input in full without touching them or publishing. The selectors
/// run before anything is updated, so a selector exception leaves the node as it was; if updating
/// fails anyway (a comparer throws), the node forgets everything and folds in full next time.
/// </para>
/// </remarks>
internal sealed class CollectToDictionaryNode<T, TKey, TValue>
    : IComputationNode<ImmutableDictionary<TKey, TValue>>,
      IHasComparer<ImmutableDictionary<TKey, TValue>>,
      IDeltaComparable<ImmutableDictionary<TKey, TValue>>,
      IDictionaryChanges<TKey, TValue>
    where TKey : notnull
{
    private static readonly IEqualityComparer<T> s_itemComparer = DefaultEqualityComparer<T>.Instance;
    private static readonly IEqualityComparer<TValue> s_valueComparer = DefaultEqualityComparer<TValue>.Instance;

    private readonly ICollectionComputationNode<T> _source;
    private readonly Func<T, TKey> _keySelector;
    private readonly Func<T, TValue> _valueSelector;
    private readonly IComparer<TValue>? _conflictComparer;
    private readonly IEqualityComparer<TKey> _keyComparer;
    private readonly ImmutableDictionary<TKey, TValue> _empty;

    // The published state of the latest computation
    private State? _state;

    // Updated in place by the computation holding the node; they describe _state.Input
    private Memo? _memo;

    // 1 while a computation holds the node
    private int _busy;
    private int _deltaComputations;

    /// <summary>
    /// The pair of an item, and its number of occurrences in the input.
    /// </summary>
    private struct ItemEntry
    {
        public KeyValuePair<TKey, TValue> Pair;
        public int Count;
    }

    /// <summary>
    /// The distinct values items map a key to, with the number of items for each.
    /// </summary>
    private struct Candidates
    {
        public TValue First;
        public int FirstCount;
        public List<(TValue Value, int Count)>? Others;
    }

    /// <summary>
    /// The pairs of the distinct items (and of the null items), the candidates of each key, and the
    /// keys in conflict.
    /// </summary>
    private sealed class Memo(IEqualityComparer<TKey> keyComparer, int capacity)
    {
        public Dictionary<T, ItemEntry> Items { get; } = new(capacity, s_itemComparer);

        public ItemEntry NullItems;

        public Dictionary<TKey, Candidates> Keys { get; } = new(capacity, keyComparer);

        public HashSet<TKey> Conflicts { get; } = new(keyComparer);
    }

    /// <summary>
    /// The latest computation: its input, the result, the result before it when the two differ
    /// (with the keys whose entries differ between them, when known), and the number of keys in
    /// conflict (with one of them, for the error message).
    /// </summary>
    private sealed record State(
        T[] Input,
        ImmutableDictionary<TKey, TValue> Result,
        ImmutableDictionary<TKey, TValue>? Previous,
        TKey[]? ChangedKeys,
        int ConflictCount,
        TKey? ConflictExample);

    public CollectToDictionaryNode(
        ICollectionComputationNode<T> source,
        Func<T, TKey> keySelector,
        Func<T, TValue> valueSelector,
        IComparer<TValue>? conflictComparer,
        IEqualityComparer<TKey>? keyComparer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _valueSelector = valueSelector ?? throw new ArgumentNullException(nameof(valueSelector));
        _conflictComparer = conflictComparer;
        _keyComparer = keyComparer ?? DefaultEqualityComparer<TKey>.Instance;
        _empty = ImmutableDictionary.Create(_keyComparer, s_valueComparer);
        Comparer = new DictionaryComparer();
        Id = NodeIdGenerator.Generate("CollectToDictionary");
        Dependencies = ImmutableHashSet.Create(_source.Id);
    }

    public string Id { get; }
    public Type OutputType => typeof(ImmutableDictionary<TKey, TValue>);
    public ImmutableHashSet<string> Dependencies { get; }
    public IEqualityComparer<ImmutableDictionary<TKey, TValue>> Comparer { get; }

    /// <summary>
    /// The comparer of the keys (and of the resulting dictionaries).
    /// </summary>
    public IEqualityComparer<TKey> KeyComparer => _keyComparer;

    internal int DeltaComputations => Volatile.Read(ref _deltaComputations);

    public Result<ImmutableDictionary<TKey, TValue>, ComputationError> Compute(ComputationContext context)
    {
        // Check cache first
        if (context.CachedValues.TryGetValue(Id, out var cachedValue) && cachedValue is ImmutableDictionary<TKey, TValue> cached)
        {
            return Result<ImmutableDictionary<TKey, TValue>, ComputationError>.Success(cached);
        }

        try
        {
            var sourceResult = context.Read(_source);
            if (sourceResult.IsError)
                return Result<ImmutableDictionary<TKey, TValue>, ComputationError>.Error(sourceResult.ErrorValue!);

            var input = sourceResult.IsNone ? [] : sourceResult.ValueOrDefault as T[] ?? sourceResult.ValueOrDefault!.ToArray();

            // The same input as the latest computation (a conflict is an error, which is not cached,
            // so the node is computed again on every read until the input changes)
            var state = Volatile.Read(ref _state);
            if (state != null && ReferenceEquals(state.Input, input))
                return ToResult(state);

            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                // Another computation holds the node: fold without touching its memo or state
                return ToResult(Fold(input, previousMemo: null, state?.Result, out _));
            }

            try
            {
                state = Volatile.Read(ref _state);
                if (state != null && ReferenceEquals(state.Input, input))
                    return ToResult(state);

                State next;
                if (state != null && _memo is { } memo && ArrayDelta.Get(_source, state.Input, input) is { } delta)
                {
                    Interlocked.Increment(ref _deltaComputations);
                    next = ApplyDelta(state, memo, delta.Edits, input);
                }
                else
                {
                    next = Fold(input, _memo, state?.Result, out var newMemo);
                    _memo = newMemo;
                }

                Volatile.Write(ref _state, next);
                return ToResult(next);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }
        catch (Exception ex)
        {
            return Result<ImmutableDictionary<TKey, TValue>, ComputationError>.Error(ComputationError.FromException(ex));
        }
    }

    /// <summary>
    /// Decides the change check from the latest transition: the identical dictionary is equal, and
    /// the result of the latest computation differs from the result before it (a new instance is
    /// created only when a key was added or removed, or its value changed).
    /// </summary>
    public bool TryEquals(ImmutableDictionary<TKey, TValue> previous, ImmutableDictionary<TKey, TValue> current, out bool equal)
    {
        if (ReferenceEquals(previous, current))
        {
            equal = true;
            return true;
        }

        if (Volatile.Read(ref _state) is { } state && ReferenceEquals(state.Result, current) && ReferenceEquals(state.Previous, previous))
        {
            equal = false;
            return true;
        }

        equal = false;
        return false;
    }

    /// <summary>
    /// Returns the keys whose entries changed in the latest transition, when <paramref name="previous"/>
    /// and <paramref name="current"/> are its two results and it was applied from a delta (a full fold
    /// does not record them; the caller then compares the keys it needs).
    /// </summary>
    public bool TryGetChangedKeys(ImmutableDictionary<TKey, TValue> previous, ImmutableDictionary<TKey, TValue> current, out IReadOnlyCollection<TKey> keys)
    {
        if (Volatile.Read(ref _state) is { ChangedKeys: { } changed } state
            && ReferenceEquals(state.Result, current)
            && ReferenceEquals(state.Previous, previous))
        {
            keys = changed;
            return true;
        }

        keys = [];
        return false;
    }

    private KeyValuePair<TKey, TValue> Pair(T item)
    {
        var key = _keySelector(item);
        if (key is null)
            throw new InvalidOperationException("CollectToDictionary: the key selector returned null.");

        return new KeyValuePair<TKey, TValue>(key, _valueSelector(item));
    }

    private static Result<ImmutableDictionary<TKey, TValue>, ComputationError> ToResult(State state) =>
        state.ConflictCount == 0
            ? Result<ImmutableDictionary<TKey, TValue>, ComputationError>.Success(state.Result)
            : Result<ImmutableDictionary<TKey, TValue>, ComputationError>.Error(new ComputationError(
                $"CollectToDictionary: {state.ConflictCount} key(s) map to different values, for example '{state.ConflictExample}'. " +
                "Pass a conflictComparer to choose one value per key."));

    /// <summary>
    /// Applies the input's edits: the pairs of the inserted items are computed first (running the
    /// selectors for new items), then the pairs of removed items are removed, those of inserted
    /// items added, and each key they touched is settled. Node held.
    /// </summary>
    private State ApplyDelta(State state, Memo memo, ItemEdit[] edits, T[] input)
    {
        var previousInput = state.Input;

        // Selectors first, so that an exception leaves everything as it was
        Dictionary<T, KeyValuePair<TKey, TValue>>? computed = null;
        var nullPair = memo.NullItems.Count > 0 ? memo.NullItems.Pair : default;
        var hasNullPair = memo.NullItems.Count > 0;
        foreach (var edit in edits)
        {
            if (edit.To < 0)
                continue;

            var item = input[edit.To];
            if (item is null)
            {
                if (!hasNullPair)
                {
                    nullPair = Pair(item!);
                    hasNullPair = true;
                }
            }
            else if (!memo.Items.ContainsKey(item) && (computed is null || !computed.ContainsKey(item)))
            {
                (computed ??= new(s_itemComparer)).Add(item, Pair(item));
            }
        }

        try
        {
            var touched = new HashSet<TKey>(_keyComparer);
            if (computed != null)
            {
                foreach (var (item, pair) in computed)
                    memo.Items.Add(item, new ItemEntry { Pair = pair });
            }

            // Insertions before removals, so that a moved item keeps its memo entry
            foreach (var edit in edits)
            {
                if (edit.To < 0)
                    continue;

                var item = input[edit.To];
                KeyValuePair<TKey, TValue> pair;
                if (item is null)
                {
                    memo.NullItems.Pair = nullPair;
                    memo.NullItems.Count++;
                    pair = nullPair;
                }
                else
                {
                    ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(memo.Items, item);
                    entry.Count++;
                    pair = entry.Pair;
                }

                AddCandidate(memo.Keys, pair);
                touched.Add(pair.Key);
            }

            foreach (var edit in edits)
            {
                if (edit.From < 0)
                    continue;

                var item = previousInput[edit.From];
                KeyValuePair<TKey, TValue> pair;
                if (item is null)
                {
                    pair = memo.NullItems.Pair;
                    if (--memo.NullItems.Count == 0)
                        memo.NullItems = default;
                }
                else
                {
                    ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(memo.Items, item);
                    pair = entry.Pair;
                    if (--entry.Count == 0)
                        memo.Items.Remove(item);
                }

                RemoveCandidate(memo.Keys, pair);
                touched.Add(pair.Key);
            }

            var builder = state.Result.ToBuilder();
            List<TKey>? changedKeys = null;
            foreach (var key in touched)
            {
                if (Settle(builder, memo.Keys, memo.Conflicts, key))
                    (changedKeys ??= []).Add(key);
            }

            return changedKeys != null
                ? Publish(input, builder.ToImmutable(), state.Result, [.. changedKeys], memo.Conflicts)
                : Publish(input, state.Result, state.Previous, state.ChangedKeys, memo.Conflicts);
        }
        catch
        {
            // The memo may be half updated: forget it, the next computation folds in full
            _memo = null;
            Volatile.Write(ref _state, null);
            throw;
        }
    }

    /// <summary>
    /// Builds a memo of all items (reusing the pairs of <paramref name="previousMemo"/>, when set, for
    /// the items it knows), the candidates and the result, starting from <paramref name="previous"/>
    /// (when set) so that an unchanged result is the previous instance. Changes nothing.
    /// </summary>
    private State Fold(T[] input, Memo? previousMemo, ImmutableDictionary<TKey, TValue>? previous, out Memo memo)
    {
        memo = new Memo(_keyComparer, input.Length);
        foreach (var item in input)
        {
            KeyValuePair<TKey, TValue> pair;
            if (item is null)
            {
                if (memo.NullItems.Count == 0)
                    memo.NullItems.Pair = previousMemo is { NullItems.Count: > 0 } ? previousMemo.NullItems.Pair : Pair(item!);

                memo.NullItems.Count++;
                pair = memo.NullItems.Pair;
            }
            else
            {
                ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(memo.Items, item, out var exists);
                if (!exists)
                {
                    entry.Pair = previousMemo != null && previousMemo.Items.TryGetValue(item, out var known)
                        ? known.Pair
                        : Pair(item);
                }

                entry.Count++;
                pair = entry.Pair;
            }

            AddCandidate(memo.Keys, pair);
        }

        var builder = (previous ?? _empty).ToBuilder();
        var changed = false;
        if (previous != null)
        {
            foreach (var key in previous.Keys)
            {
                if (!memo.Keys.ContainsKey(key))
                    changed |= builder.Remove(key);
            }
        }

        foreach (var key in memo.Keys.Keys)
            changed |= Settle(builder, memo.Keys, memo.Conflicts, key);

        return previous != null && !changed
            ? Publish(input, previous, null, null, memo.Conflicts)
            : Publish(input, builder.ToImmutable(), previous, null, memo.Conflicts);
    }

    private static State Publish(T[] input, ImmutableDictionary<TKey, TValue> result, ImmutableDictionary<TKey, TValue>? previous, TKey[]? changedKeys, HashSet<TKey> conflicts) =>
        new(input, result, previous, changedKeys, conflicts.Count, conflicts.Count > 0 ? conflicts.First() : default);

    /// <summary>
    /// Brings the entry of <paramref name="key"/> in <paramref name="builder"/> in line with its
    /// candidates (absent, a conflict, or the winning value), and records whether it is a conflict.
    /// Returns whether the builder changed.
    /// </summary>
    private bool Settle(ImmutableDictionary<TKey, TValue>.Builder builder, Dictionary<TKey, Candidates> keys, HashSet<TKey> conflicts, TKey key)
    {
        ref var candidates = ref CollectionsMarshal.GetValueRefOrNullRef(keys, key);
        if (Unsafe.IsNullRef(ref candidates))
        {
            conflicts.Remove(key);
            return builder.Remove(key);
        }

        var winner = candidates.First;
        if (candidates.Others is { Count: > 0 } others)
        {
            if (_conflictComparer is null)
            {
                conflicts.Add(key);
                return builder.Remove(key);
            }

            foreach (var (value, _) in others)
            {
                if (_conflictComparer.Compare(value, winner) < 0)
                    winner = value;
            }
        }

        conflicts.Remove(key);
        if (builder.TryGetValue(key, out var current) && s_valueComparer.Equals(current, winner))
            return false;

        builder[key] = winner;
        return true;
    }

    private static void AddCandidate(Dictionary<TKey, Candidates> keys, KeyValuePair<TKey, TValue> pair)
    {
        ref var candidates = ref CollectionsMarshal.GetValueRefOrAddDefault(keys, pair.Key, out var exists);
        if (!exists)
        {
            candidates.First = pair.Value;
            candidates.FirstCount = 1;
            return;
        }

        if (s_valueComparer.Equals(candidates.First, pair.Value))
        {
            candidates.FirstCount++;
            return;
        }

        var others = candidates.Others ??= new List<(TValue, int)>(1);
        for (var i = 0; i < others.Count; i++)
        {
            if (s_valueComparer.Equals(others[i].Value, pair.Value))
            {
                others[i] = (others[i].Value, others[i].Count + 1);
                return;
            }
        }

        others.Add((pair.Value, 1));
    }

    private static void RemoveCandidate(Dictionary<TKey, Candidates> keys, KeyValuePair<TKey, TValue> pair)
    {
        ref var candidates = ref CollectionsMarshal.GetValueRefOrNullRef(keys, pair.Key);
        if (Unsafe.IsNullRef(ref candidates))
            return;

        var others = candidates.Others;
        if (s_valueComparer.Equals(candidates.First, pair.Value))
        {
            if (--candidates.FirstCount > 0)
                return;

            if (others is { Count: > 0 })
            {
                (candidates.First, candidates.FirstCount) = others[^1];
                others.RemoveAt(others.Count - 1);
            }
            else
            {
                keys.Remove(pair.Key);
            }

            return;
        }

        if (others is null)
            return;

        for (var i = 0; i < others.Count; i++)
        {
            if (s_valueComparer.Equals(others[i].Value, pair.Value))
            {
                if (others[i].Count > 1)
                    others[i] = (others[i].Value, others[i].Count - 1);
                else
                    others.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// Dictionaries are equal when they have the same keys, each with an equal value (by
    /// <see cref="DefaultEqualityComparer{T}.Instance"/>); their enumeration order does not matter.
    /// </summary>
    private sealed class DictionaryComparer : IEqualityComparer<ImmutableDictionary<TKey, TValue>>
    {
        public bool Equals(ImmutableDictionary<TKey, TValue>? x, ImmutableDictionary<TKey, TValue>? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Count != y.Count)
                return false;

            foreach (var (key, value) in x)
            {
                if (!y.TryGetValue(key, out var other) || !s_valueComparer.Equals(value, other))
                    return false;
            }

            return true;
        }

        public int GetHashCode(ImmutableDictionary<TKey, TValue> obj) => obj.Count;
    }
}
