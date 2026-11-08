using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Null items are never used as dictionary keys here, so TSource is a valid key type
#pragma warning disable CS8714

namespace Guacamole.Pipeline;

/// <summary>
/// Per-item memo of a collection operator: remembers, for each input item of the previous
/// computation, the value its lambda produced, so that recomputing the collection after a change
/// only runs the lambda for new or changed items.
/// </summary>
/// <remarks>
/// <para>
/// Items are matched with <see cref="EqualityComparer{T}.Default"/> (not a custom result comparer,
/// which may be looser than the item's own equality). A hit returns the identical value instance,
/// so downstream stages see an unchanged item even when the value only has reference equality.
/// </para>
/// <para>
/// The memo keeps the previous computation's items and values (both arrays are immutable once
/// published) and a map from each distinct item of it to its value and number of occurrences.
/// A computation walks the new items in line with the previous ones, which costs a reference
/// comparison per unchanged item. An item that does not line up is looked up in the map: a new item
/// is computed (an insertion, or a replacement), and a previous item found further ahead in the
/// previous items realigns the walk there (the items in between were removed, replaced or moved).
/// The map is updated in place: occurrences of items that did not line up are added, those of the
/// previous items that were skipped are removed, and items left with none are forgotten. So after a
/// few changes in a large collection the work is one pass plus a few map operations, and the only
/// allocation is the result array (none at all if every value is unchanged: the previous array is
/// returned). The forward searches are bounded by a budget proportional to the input, so heavily
/// reordered input still costs O(n).
/// </para>
/// <para>
/// The map holds exactly the distinct items of the latest published computation. It is updated
/// only by the computation that holds it (<see cref="_mapBusy"/>), which then publishes its items
/// and values; a computation running concurrently matches positions only and computes the rest,
/// without updating the memo. If the lambda throws, the map changes are undone and the previous
/// computation stays published. <c>null</c> items are not memoized: the lambda runs for them every
/// time.
/// </para>
/// </remarks>
/// <typeparam name="TSource">The input item type</typeparam>
/// <typeparam name="TValue">The value computed per item</typeparam>
internal sealed class ItemMemo<TSource, TValue>
{
    private sealed class Generation(TSource[] items, TValue[] values)
    {
        public TSource[] Items { get; } = items;

        public TValue[] Values { get; } = values;
    }

    private struct Entry
    {
        public TValue Value;
        public int Count;
    }

    // The latest published computation; the map describes exactly its items
    private Generation? _latest;

    // Null items are never used as keys (see Apply), so TSource is a valid dictionary key (CS8714
    // is disabled for this file).
    private Dictionary<TSource, Entry>? _map;

    // 1 while a computation holds the map
    private int _mapBusy;

    /// <summary>
    /// The number of distinct items remembered (for tests). Not thread-safe.
    /// </summary>
    internal int Count => _map?.Count ?? 0;

    /// <summary>
    /// Computes the value for each item, in order, reusing the value of the previous computation for
    /// items equal to one seen then. Exceptions from <paramref name="compute"/> propagate and leave
    /// the memo unchanged. The returned array must not be modified.
    /// </summary>
    public TValue[] Apply(IEnumerable<TSource> items, Func<TSource, TValue> compute)
    {
        // Arrays from upstream stages and sources are immutable snapshots, so they can be kept
        var current = items as TSource[] ?? items.ToArray();

        if (Interlocked.CompareExchange(ref _mapBusy, 1, 0) != 0)
            return ApplyWithoutMap(current, compute);

        try
        {
            return ApplyWithMap(current, compute);
        }
        finally
        {
            Volatile.Write(ref _mapBusy, 0);
        }
    }

    private TValue[] ApplyWithMap(TSource[] current, Func<TSource, TValue> compute)
    {
        var previous = _latest;
        var previousItems = previous?.Items ?? [];
        var previousValues = previous?.Values ?? [];
        var map = _map ??= new Dictionary<TSource, Entry>(current.Length, EqualityComparer<TSource>.Default);

        var values = new TValue[current.Length];
        List<(int Start, int End)>? skipped = null; // ranges of previous items not matched by position
        List<TSource>? added = null; // items whose occurrences were added to the map, to undo on failure
        var j = 0;

        // Bounds the forward scans below, so that heavily reordered input costs O(n) comparisons
        // (plus a map lookup per item) instead of O(n^2)
        var scanBudget = 2L * (current.Length + previousItems.Length) + 64;

        try
        {
            for (var i = 0; i < current.Length; i++)
            {
                var item = current[i];
                if (item is null)
                {
                    values[i] = compute(item);
                    if (j < previousItems.Length && previousItems[j] is null)
                        j++;
                    continue;
                }

                if (j < previousItems.Length && Same(item, previousItems[j]))
                {
                    values[i] = previousValues[j++];
                    continue;
                }

                if (map.TryGetValue(item, out var entry))
                {
                    // A previous item: if it is further ahead, the previous items before it were
                    // removed (or replaced, or moved): skip them and continue in line from there
                    var found = -1;
                    for (var p = j + 1; p < previousItems.Length && scanBudget > 0; p++, scanBudget--)
                    {
                        if (Same(item, previousItems[p]))
                        {
                            found = p;
                            break;
                        }
                    }

                    if (found >= 0)
                    {
                        (skipped ??= new List<(int, int)>()).Add((j, found));
                        values[i] = previousValues[found];
                        j = found + 1;
                        continue;
                    }

                    // Moved from further back (or too far to look for): reuse its value
                    values[i] = entry.Value;
                }
                else
                {
                    // A new item (inserted, or replacing the previous item at j, which the next
                    // item in line then skips)
                    var value = compute(item);
                    map.Add(item, new Entry { Value = value });
                    values[i] = value;
                }

                CollectionsMarshal.GetValueRefOrNullRef(map, item).Count++;
                (added ??= new List<TSource>()).Add(item);
            }
        }
        catch
        {
            if (added != null)
            {
                foreach (var item in added)
                    RemoveOccurrence(map, item);
            }

            throw;
        }

        if (j < previousItems.Length)
            (skipped ??= new List<(int, int)>()).Add((j, previousItems.Length));

        // Previous items not matched by position lose an occurrence (after the additions above, so
        // an item that moved keeps its value)
        if (skipped != null)
        {
            foreach (var (first, end) in skipped)
            {
                for (var position = first; position < end; position++)
                {
                    if (previousItems[position] is { } item)
                        RemoveOccurrence(map, item);
                }
            }
        }
        else if (added is null && current.Length == previousValues.Length && !HasNull(current))
        {
            // Every item matched its previous position: the values are the previous ones
            values = previousValues;
        }

        Volatile.Write(ref _latest, new Generation(current, values));
        return values;
    }

    /// <summary>
    /// Used while another computation holds the map: reuses values by position only, and computes
    /// each other distinct item once. Does not update the memo.
    /// </summary>
    private TValue[] ApplyWithoutMap(TSource[] current, Func<TSource, TValue> compute)
    {
        var previous = Volatile.Read(ref _latest);
        var previousItems = previous?.Items ?? [];
        var previousValues = previous?.Values ?? [];
        Dictionary<TSource, TValue>? computed = null;

        var values = new TValue[current.Length];
        for (var i = 0; i < current.Length; i++)
        {
            var item = current[i];
            if (item is null)
            {
                values[i] = compute(item);
            }
            else if (i < previousItems.Length && Same(item, previousItems[i]))
            {
                values[i] = previousValues[i];
            }
            else
            {
                computed ??= new Dictionary<TSource, TValue>(EqualityComparer<TSource>.Default);
                if (!computed.TryGetValue(item, out var value))
                {
                    value = compute(item);
                    computed.Add(item, value);
                }

                values[i] = value;
            }
        }

        return values;
    }

    private static void RemoveOccurrence(Dictionary<TSource, Entry> map, TSource item)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(map, item);
        if (Unsafe.IsNullRef(ref entry))
            return;

        if (--entry.Count <= 0)
            map.Remove(item);
    }

    private static bool HasNull(TSource[] items)
    {
        if (default(TSource) is not null)
            return false;

        foreach (var item in items)
        {
            if (item is null)
                return true;
        }

        return false;
    }

    private static bool Same(TSource item, TSource? other) =>
        (!typeof(TSource).IsValueType && ReferenceEquals(item, other))
        || EqualityComparer<TSource>.Default.Equals(item, other);
}
