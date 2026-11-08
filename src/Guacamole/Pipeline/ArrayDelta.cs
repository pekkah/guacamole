namespace Guacamole.Pipeline;

// Item-level delta propagation for collection pipelines (#61)
// ============================================================
//
// Every collection stage produces an immutable T[] per computation. After K item changes in an
// N-item source, the stages used to rebuild their output by walking all N input items (comparing
// each with the previous computation's item), and the change checks after them compared all N
// results. With deltas, each stage instead derives its new output from its previous output and
// the K edits of its input:
//
// - A delta (ArrayDelta<T>) says how one array (From) became another (To): a list of edits, each
//   an insertion, a removal or a replacement, sorted by position in both arrays. Items outside the
//   edits are the identical items (same reference, or same value for value types), in the same
//   order. A delta is a fact about two immutable arrays, not about versions, so it can be used by
//   anyone who holds both arrays, at any time, from any thread: a stage asks its input for the
//   delta from the exact input array its previous output was computed from to the array it reads
//   now. If there is none (first computation, the input moved on more often than its delta
//   history holds, an error, a node type that records no deltas, the source's change log was
//   dropped), the stage recomputes in full, as before.
// - Sources log the slots changed since their last snapshot and turn them into a delta when the
//   next snapshot is built (IncrementalSource<T>.BuildSnapshot). The log is dropped (so the next
//   snapshot has no delta) when it grows past about half the items, on Clear, and when slots are
//   compacted.
// - Select, Where, SelectMany and values.Combine(value) apply their input's delta to their previous
//   output: they copy the unchanged runs in bulk (Array.Copy), run their lambda for the edited
//   items only (through the per-item memo, so its semantics are unchanged), and record the delta
//   of their output for the stages after them.
// - CollectToDictionary applies its input's delta to the dictionary it built last (removing the
//   pairs of removed items, adding those of inserted ones), so a lookup table over a collection is
//   updated in O(K) too (see CollectToDictionaryNode).
// - The change check of a stage (is the new output equal to the cached one?) uses the delta when
//   it only replaces items: only the K replaced items are compared. Collect() wraps its input
//   array, so its check uses its input's delta the same way.
// - Each node keeps a short history of its latest deltas (DeltaHistory<T>, up to
//   DeltaHistory<T>.Capacity, each going from the To of the one before), holding both arrays of
//   each delta weakly, so deltas add no retained arrays. A stage whose input moved on several times
//   since it last computed (a provider read less often than its source changes, or another pipeline
//   reading the source in between) gets the composition of the deltas in between
//   (ArrayDelta.Compose); only one whose input moved on more often than that, or whose last input
//   was collected, falls back to a full recompute.
// - Merge concatenates the deltas of its two inputs. A collection node with no structural knowledge
//   of its change (single-value SelectMany, which reruns its lambda on the whole value) opts in by
//   diffing its previous and new output (ArrayDelta.Diff: common prefix and suffix of identical
//   items), recording a delta when the changed middle is small. Any other collection node can opt in
//   the same way: implement IDeltaSource<T> with a DeltaHistory<T>, and record a delta for each new
//   output it derives from the previous one (Clear the history when it computes from scratch).
//
// What remains O(N) per read: every stage still allocates its output array and copies the
// unchanged items into it (a memory copy, no per-item work), the source copies its snapshot the
// same way, Where counts kept flags over unchanged runs (vectorized), SelectMany shifts its offset
// table when the number of results changes, and user code that consumes a Collect()ed array (a
// sum, a dictionary build) is O(N) by nature. Avoiding the copies would need a persistent (chunked
// or tree) representation instead of arrays for stage outputs, Collect() results and the
// read-only values view.

/// <summary>
/// One edit of an <see cref="ArrayDelta{T}"/>: <paramref name="From"/> is the item's index in the
/// old array (-1 for an inserted item) and <paramref name="To"/> its index in the new array (-1 for
/// a removed item). With both set, the item at <paramref name="From"/> was replaced by the item at
/// <paramref name="To"/> (which may be equal to it).
/// </summary>
internal readonly record struct ItemEdit(int From, int To)
{
    /// <summary>
    /// The number of unchanged items between the previous edit and this one, given the positions
    /// (<paramref name="from"/>, <paramref name="to"/>) just after the previous edit.
    /// </summary>
    public int RunBefore(int from, int to) => From >= 0 ? From - from : To - to;
}

/// <summary>
/// How an immutable array (From) became another (To): the edits, sorted by position in both
/// arrays. Between two edits, the items of both arrays are the identical items in the same order.
/// </summary>
/// <remarks>
/// Both arrays are held weakly: whoever wants to use the delta holds them already (as its previous
/// input or cached value, and the input it just read), and the delta, kept in a node's
/// <see cref="DeltaHistory{T}"/>, does not keep them alive otherwise.
/// </remarks>
internal sealed class ArrayDelta<T>
{
    private readonly WeakReference<T[]> _from;
    private readonly WeakReference<T[]> _to;

    public ArrayDelta(T[] from, T[] to, ItemEdit[] edits)
    {
        _from = new WeakReference<T[]>(from);
        _to = new WeakReference<T[]>(to);
        FromLength = from.Length;
        ToLength = to.Length;
        Edits = edits;
    }

    /// <summary>
    /// The length of the old array.
    /// </summary>
    public int FromLength { get; }

    /// <summary>
    /// The length of the new array.
    /// </summary>
    public int ToLength { get; }

    /// <summary>
    /// The edits, sorted by position in both arrays.
    /// </summary>
    public ItemEdit[] Edits { get; }

    /// <summary>
    /// Whether this delta goes from exactly <paramref name="from"/> to exactly <paramref name="to"/>.
    /// </summary>
    public bool Describes(T[] from, T[] to) => StartsAt(from) && EndsAt(to);

    /// <summary>
    /// Whether the old array is exactly <paramref name="array"/>.
    /// </summary>
    public bool StartsAt(T[] array) => _from.TryGetTarget(out var target) && ReferenceEquals(target, array);

    /// <summary>
    /// Whether the new array is exactly <paramref name="array"/>.
    /// </summary>
    public bool EndsAt(T[] array) => _to.TryGetTarget(out var target) && ReferenceEquals(target, array);

    /// <summary>
    /// The new array, if it is still alive.
    /// </summary>
    public T[]? TryGetTo() => _to.TryGetTarget(out var target) ? target : null;
}

/// <summary>
/// The latest deltas of a collection node's outputs, oldest first, each going from the new array of
/// the one before (see the design note in ArrayDelta.cs). Thread-safe: recording takes a lock,
/// lookups read an immutable array.
/// </summary>
internal sealed class DeltaHistory<T>
{
    /// <summary>
    /// The number of deltas kept: a reader can be up to this many outputs behind and still get a
    /// delta.
    /// </summary>
    public const int Capacity = 8;

    private readonly object _lock = new();

    // Oldest first; each delta starts at the array the one before ends at. Replaced, never modified.
    private ArrayDelta<T>[] _entries = [];

    // The latest composition handed out, for the next reader asking for the same one (a stage and
    // the change check of its node, or a stage and Collect)
    private ArrayDelta<T>? _composed;

    /// <summary>
    /// Records the delta of a new output. It extends the history when it starts at the output the
    /// latest delta ends at; otherwise it starts a new history.
    /// </summary>
    public void Record(ArrayDelta<T> delta)
    {
        lock (_lock)
        {
            var entries = _entries;
            ArrayDelta<T>[] next;
            if (entries.Length > 0 && entries[^1].TryGetTo() is { } latest && delta.StartsAt(latest))
            {
                var keep = Math.Min(entries.Length, Capacity - 1);
                next = new ArrayDelta<T>[keep + 1];
                Array.Copy(entries, entries.Length - keep, next, 0, keep);
                next[keep] = delta;
            }
            else
            {
                next = [delta];
            }

            Volatile.Write(ref _entries, next);
        }
    }

    /// <summary>
    /// Forgets every delta (the node computed an output from scratch, or its items were reset).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            Volatile.Write(ref _entries, []);
            Volatile.Write(ref _composed, null);
        }
    }

    /// <summary>
    /// The current deltas, to restore them with <see cref="Restore"/>.
    /// </summary>
    public ArrayDelta<T>[] Save() => Volatile.Read(ref _entries);

    /// <summary>
    /// Restores deltas saved with <see cref="Save"/>.
    /// </summary>
    public void Restore(ArrayDelta<T>[] entries)
    {
        lock (_lock)
        {
            Volatile.Write(ref _entries, entries);
        }
    }

    /// <summary>
    /// The delta from <paramref name="from"/> to <paramref name="to"/>, both outputs of the node:
    /// a recorded delta, or the composition of the consecutive recorded deltas between them;
    /// <c>null</c> if they are not both in the history.
    /// </summary>
    public ArrayDelta<T>? Get(T[] from, T[] to)
    {
        var entries = Volatile.Read(ref _entries);
        var last = entries.Length - 1;
        while (last >= 0 && !entries[last].EndsAt(to))
            last--;

        for (var first = last; first >= 0; first--)
        {
            if (!entries[first].StartsAt(from))
                continue;

            if (first == last)
                return entries[last];

            if (Volatile.Read(ref _composed) is { } composed && composed.Describes(from, to))
                return composed;

            composed = ArrayDelta.Compose(entries.AsSpan(first, last - first + 1), from, to);
            if (composed != null)
                Volatile.Write(ref _composed, composed);
            return composed;
        }

        return null;
    }
}

/// <summary>
/// A collection node that records the delta of its latest output (see the design note in
/// ArrayDelta.cs).
/// </summary>
internal interface IDeltaSource<T>
{
    /// <summary>
    /// The delta from <paramref name="from"/> to <paramref name="to"/>, both outputs of this node,
    /// if this node recorded it; otherwise <c>null</c>.
    /// </summary>
    ArrayDelta<T>? GetDelta(T[] from, T[] to);
}

/// <summary>
/// A single-value node that can compare two of its values faster than its comparer in some cases
/// (<c>Collect()</c>, through its input's delta).
/// </summary>
internal interface IDeltaComparable<T>
{
    /// <summary>
    /// Compares <paramref name="previous"/> and <paramref name="current"/> exactly as the node's
    /// comparer would, if that can be decided cheaply; returns <c>false</c> otherwise.
    /// </summary>
    bool TryEquals(T previous, T current, out bool equal);
}

/// <summary>
/// Helpers for <see cref="ArrayDelta{T}"/>.
/// </summary>
internal static class ArrayDelta
{
    /// <summary>
    /// The delta from <paramref name="from"/> to <paramref name="to"/> recorded by
    /// <paramref name="node"/>, if it records deltas and has this one.
    /// </summary>
    public static ArrayDelta<T>? Get<T>(object node, T[] from, T[] to) =>
        node is IDeltaSource<T> source ? source.GetDelta(from, to) : null;

    /// <summary>
    /// Composes consecutive deltas (each starting at the array the one before ends at) into the
    /// delta from <paramref name="from"/>, where the first one starts, to <paramref name="to"/>,
    /// where the last one ends. Replacements of an item by the identical item (replaced and then
    /// replaced back) are dropped. Returns <c>null</c> if the deltas do not line up (a bug).
    /// </summary>
    /// <remarks>
    /// Works on the edits only (the arrays in between are not needed): each delta is read as a
    /// sequence of operations (copy n items, remove, insert, replace), and two sequences compose like
    /// edit scripts, the second consuming the items the first produces. The cost is proportional to
    /// the number of edits.
    /// </remarks>
    public static ArrayDelta<T>? Compose<T>(ReadOnlySpan<ArrayDelta<T>> deltas, T[] from, T[] to)
    {
        var ops = ToOps(deltas[0].Edits, deltas[0].FromLength);
        for (var i = 1; i < deltas.Length; i++)
        {
            if (deltas[i].FromLength != deltas[i - 1].ToLength)
                return null;

            ops = Compose(ops, ToOps(deltas[i].Edits, deltas[i].FromLength));
            if (ops is null)
                return null;
        }

        var edits = new List<ItemEdit>();
        int f = 0, t = 0;
        foreach (var (kind, count) in ops)
        {
            switch (kind)
            {
                case EditOp.Copy:
                    f += count;
                    t += count;
                    break;
                case EditOp.Remove:
                    edits.Add(new ItemEdit(f++, -1));
                    break;
                case EditOp.Insert:
                    edits.Add(new ItemEdit(-1, t++));
                    break;
                default:
                    if (!Identity<T>.Same(from[f], to[t]))
                        edits.Add(new ItemEdit(f, t));
                    f++;
                    t++;
                    break;
            }
        }

        if (f != from.Length || t != to.Length)
            return null;

        return new ArrayDelta<T>(from, to, [.. edits]);
    }

    private enum EditOp : byte
    {
        Copy,
        Remove,
        Insert,
        Replace,
    }

    /// <summary>
    /// The operations of an edit list over an array of <paramref name="fromLength"/> items.
    /// </summary>
    private static List<(EditOp Kind, int Count)> ToOps(ItemEdit[] edits, int fromLength)
    {
        var ops = new List<(EditOp, int)>(2 * edits.Length + 1);
        int from = 0, to = 0;
        foreach (var edit in edits)
        {
            var run = edit.RunBefore(from, to);
            if (run > 0)
                ops.Add((EditOp.Copy, run));
            from += run;
            to += run;

            if (edit.From >= 0 && edit.To >= 0)
            {
                ops.Add((EditOp.Replace, 1));
                from++;
                to++;
            }
            else if (edit.From >= 0)
            {
                ops.Add((EditOp.Remove, 1));
                from++;
            }
            else
            {
                ops.Add((EditOp.Insert, 1));
                to++;
            }
        }

        if (fromLength > from)
            ops.Add((EditOp.Copy, fromLength - from));
        return ops;
    }

    /// <summary>
    /// Composes the operations taking A to B (<paramref name="first"/>) with those taking B to C
    /// (<paramref name="second"/>) into operations taking A to C; <c>null</c> if they do not line up.
    /// </summary>
    private static List<(EditOp Kind, int Count)>? Compose(List<(EditOp Kind, int Count)> first, List<(EditOp Kind, int Count)> second)
    {
        var result = new List<(EditOp Kind, int Count)>(first.Count + second.Count);
        int i = 0, j = 0;

        // Items of the current copy operations not consumed yet
        var left1 = first.Count > 0 ? first[0].Count : 0;
        var left2 = second.Count > 0 ? second[0].Count : 0;

        while (true)
        {
            // An item of A removed by the first delta, or an item of C inserted by the second: no B item
            if (i < first.Count && first[i].Kind == EditOp.Remove)
            {
                Emit(EditOp.Remove, 1);
                Next1();
                continue;
            }

            if (j < second.Count && second[j].Kind == EditOp.Insert)
            {
                Emit(EditOp.Insert, 1);
                Next2();
                continue;
            }

            if (i >= first.Count || j >= second.Count)
                break;

            // Both deal with the same item of B: the first produced it (copied from A, inserted, or
            // replacing an item of A), the second consumes it (copies it to C, removes or replaces it)
            var kind1 = first[i].Kind;
            var kind2 = second[j].Kind;
            if (kind1 == EditOp.Copy && kind2 == EditOp.Copy)
            {
                var n = Math.Min(left1, left2);
                Emit(EditOp.Copy, n);
                left1 -= n;
                left2 -= n;
                if (left1 == 0)
                    Next1();
                if (left2 == 0)
                    Next2();
                continue;
            }

            var composed = (kind1, kind2) switch
            {
                (EditOp.Insert, EditOp.Remove) => (EditOp?)null, // inserted, then removed: nothing
                (EditOp.Insert, _) => EditOp.Insert,
                (_, EditOp.Remove) => EditOp.Remove,
                _ => EditOp.Replace, // a replaced or copied item, replaced or copied (not both copied)
            };
            if (composed is { } op)
                Emit(op, 1);

            if (kind1 != EditOp.Copy || --left1 == 0)
                Next1();
            if (kind2 != EditOp.Copy || --left2 == 0)
                Next2();
        }

        // Both must have consumed all of B
        return i == first.Count && j == second.Count ? result : null;

        void Next1()
        {
            i++;
            left1 = i < first.Count ? first[i].Count : 0;
        }

        void Next2()
        {
            j++;
            left2 = j < second.Count ? second[j].Count : 0;
        }

        void Emit(EditOp kind, int count)
        {
            if (kind == EditOp.Copy && result.Count > 0 && result[^1].Kind == EditOp.Copy)
                result[^1] = (EditOp.Copy, result[^1].Count + count);
            else
                result.Add((kind, count));
        }
    }

    /// <summary>
    /// The delta from <paramref name="from"/> to <paramref name="to"/> found by trimming their
    /// common prefix and suffix of identical items (see <see cref="Identity{T}"/>): the items in the
    /// middle are replaced pairwise, and the extra ones removed or inserted. Returns <c>null</c> when
    /// the arrays are identical or the middle has more than <paramref name="maxEdits"/> edits (a
    /// delta would not pay off). Costs one identity comparison per item of the prefix and suffix.
    /// </summary>
    public static ArrayDelta<T>? Diff<T>(T[] from, T[] to, int maxEdits)
    {
        if (ReferenceEquals(from, to))
            return null;

        var common = Math.Min(from.Length, to.Length);
        var prefix = 0;
        while (prefix < common && Identity<T>.Same(from[prefix], to[prefix]))
            prefix++;

        var suffix = 0;
        while (suffix < common - prefix && Identity<T>.Same(from[from.Length - 1 - suffix], to[to.Length - 1 - suffix]))
            suffix++;

        var removed = from.Length - prefix - suffix;
        var inserted = to.Length - prefix - suffix;
        var count = Math.Max(removed, inserted);
        if (count > maxEdits)
            return null;

        var edits = new ItemEdit[count];
        var replaced = Math.Min(removed, inserted);
        for (var k = 0; k < replaced; k++)
            edits[k] = new ItemEdit(prefix + k, prefix + k);
        for (var k = replaced; k < removed; k++)
            edits[k] = new ItemEdit(prefix + k, -1);
        for (var k = replaced; k < inserted; k++)
            edits[k] = new ItemEdit(-1, prefix + k);

        return new ArrayDelta<T>(from, to, edits);
    }

    /// <summary>
    /// Decides whether <paramref name="from"/> and <paramref name="to"/> are element-wise equal by
    /// <paramref name="comparer"/>, using <paramref name="delta"/> (which describes them): arrays of
    /// different lengths are not, and when the delta only replaces items, only the replaced items
    /// are compared (the others are identical). Returns <c>false</c> when the delta inserts and
    /// removes items, so that positions shift and only a full comparison can tell.
    /// </summary>
    public static bool TryEquals<T>(ArrayDelta<T> delta, T[] from, T[] to, IEqualityComparer<T> comparer, out bool equal)
    {
        if (from.Length != to.Length)
        {
            equal = false;
            return true;
        }

        var edits = delta.Edits;
        foreach (var edit in edits)
        {
            if (edit.From != edit.To)
            {
                equal = false;
                return false;
            }
        }

        foreach (var edit in edits)
        {
            if (!comparer.Equals(from[edit.From], to[edit.To]))
            {
                equal = false;
                return true;
            }
        }

        equal = true;
        return true;
    }
}

/// <summary>
/// Whether two values are interchangeable (indistinguishable to user code):
/// <list type="bullet">
/// <item><description>reference types: the same reference;</description></item>
/// <item><description><see cref="float"/> and <see cref="double"/>: the same bit pattern
/// (<c>0.0</c> and <c>-0.0</c>, or NaNs with different payloads, are equal but distinguishable);</description></item>
/// <item><description>other primitive and enum types: equal by <see cref="EqualityComparer{T}.Default"/>;</description></item>
/// <item><description><see cref="System.Collections.Immutable.ImmutableArray{T}"/>: the same underlying
/// array (its default equality is exactly that);</description></item>
/// <item><description>other value types: never, since their equality may be looser than their contents.</description></item>
/// </list>
/// </summary>
internal static class Identity<T>
{
    private static readonly bool s_byValue =
        (typeof(T).IsPrimitive && typeof(T) != typeof(float) && typeof(T) != typeof(double))
        || typeof(T).IsEnum
        || (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(System.Collections.Immutable.ImmutableArray<>));

    public static bool Same(T x, T y)
    {
        if (!typeof(T).IsValueType)
            return ReferenceEquals(x, y);

        if (typeof(T) == typeof(double))
            return BitConverter.DoubleToInt64Bits(System.Runtime.CompilerServices.Unsafe.As<T, double>(ref x)) == BitConverter.DoubleToInt64Bits(System.Runtime.CompilerServices.Unsafe.As<T, double>(ref y));

        if (typeof(T) == typeof(float))
            return BitConverter.SingleToInt32Bits(System.Runtime.CompilerServices.Unsafe.As<T, float>(ref x)) == BitConverter.SingleToInt32Bits(System.Runtime.CompilerServices.Unsafe.As<T, float>(ref y));

        return s_byValue && EqualityComparer<T>.Default.Equals(x, y);
    }
}
