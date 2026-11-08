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
//   now. If there is none (first computation, the input recomputed in between without anyone
//   reading it, an error, a node type that records no deltas, the source's change log was
//   dropped), the stage recomputes in full, as before.
// - Sources log the slots changed since their last snapshot and turn them into a delta when the
//   next snapshot is built (IncrementalSource<T>.BuildSnapshot). The log is dropped (so the next
//   snapshot has no delta) when it grows past about half the items, on Clear, and when slots are
//   compacted.
// - Select, Where, SelectMany and values.Combine(value) apply their input's delta to their previous
//   output: they copy the unchanged runs in bulk (Array.Copy), run their lambda for the edited
//   items only (through the per-item memo, so its semantics are unchanged), and record the delta
//   of their output for the stages after them.
// - The change check of a stage (is the new output equal to the cached one?) uses the delta when
//   it only replaces items: only the K replaced items are compared. Collect() wraps its input
//   array, so its check uses its input's delta the same way.
// - Each node keeps only its latest delta, holding the From array weakly, so deltas add no
//   retained arrays; a stage whose input moved on more than once since it last computed falls back
//   to a full recompute.
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
/// The From array is held weakly: whoever wants to use the delta holds it already (as its previous
/// input or cached value), and the delta does not keep it alive otherwise.
/// </remarks>
internal sealed class ArrayDelta<T>
{
    private readonly WeakReference<T[]> _from;

    public ArrayDelta(T[] from, T[] to, ItemEdit[] edits)
    {
        _from = new WeakReference<T[]>(from);
        FromLength = from.Length;
        To = to;
        Edits = edits;
    }

    /// <summary>
    /// The new array.
    /// </summary>
    public T[] To { get; }

    /// <summary>
    /// The length of the old array.
    /// </summary>
    public int FromLength { get; }

    /// <summary>
    /// The edits, sorted by position in both arrays.
    /// </summary>
    public ItemEdit[] Edits { get; }

    /// <summary>
    /// Whether this delta goes from exactly <paramref name="from"/> to exactly <paramref name="to"/>.
    /// </summary>
    public bool Describes(T[] from, T[] to) =>
        ReferenceEquals(to, To) && _from.TryGetTarget(out var target) && ReferenceEquals(target, from);
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
/// Whether two values are interchangeable: the same reference for reference types, equal by
/// <see cref="EqualityComparer{T}.Default"/> for primitive and enum types. Other value types are
/// never considered identical, since their equality may be looser than their contents.
/// </summary>
internal static class Identity<T>
{
    private static readonly bool s_byValue = typeof(T).IsPrimitive || typeof(T).IsEnum;

    public static bool Same(T x, T y)
    {
        if (!typeof(T).IsValueType)
            return ReferenceEquals(x, y);

        return s_byValue && EqualityComparer<T>.Default.Equals(x, y);
    }
}
