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
/// The memo is rebuilt from the current items on every computation, so it holds at most one entry
/// per distinct item of the latest computation. It is replaced only when a computation completes;
/// if the lambda throws, the previous memo is kept. The dictionary is never mutated after it is
/// published (with <see cref="Volatile.Write{T}(ref T, T)"/>), so concurrent computations can read
/// it safely; when two computations race, the last one to finish wins. <c>null</c> items are not
/// memoized: the lambda runs for them every time.
/// </para>
/// </remarks>
/// <typeparam name="TSource">The input item type</typeparam>
/// <typeparam name="TValue">The value computed per item</typeparam>
internal sealed class ItemMemo<TSource, TValue>
{
    // Null items are never used as keys (see Apply), so TSource is a valid dictionary key.
#pragma warning disable CS8714
    private Dictionary<TSource, TValue>? _entries;
#pragma warning restore CS8714

    /// <summary>
    /// Computes the value for each item, in order, reusing the value of the previous computation for
    /// items equal to one seen then. Exceptions from <paramref name="compute"/> propagate and leave
    /// the memo unchanged.
    /// </summary>
    public TValue[] Apply(IEnumerable<TSource> items, Func<TSource, TValue> compute)
    {
        var previous = Volatile.Read(ref _entries);
        var count = items is IReadOnlyCollection<TSource> collection ? collection.Count : 0;
#pragma warning disable CS8714
        var current = new Dictionary<TSource, TValue>(count, EqualityComparer<TSource>.Default);
#pragma warning restore CS8714
        var results = new List<TValue>(count);

        foreach (var item in items)
        {
            TValue value;
            if (item is null)
            {
                value = compute(item);
            }
            else if (current.TryGetValue(item, out var seen))
            {
                value = seen;
            }
            else
            {
                if (previous is null || !previous.TryGetValue(item, out value!))
                {
                    value = compute(item);
                }

                current.Add(item, value);
            }

            results.Add(value);
        }

        Volatile.Write(ref _entries, current);
        return results.ToArray();
    }
}
