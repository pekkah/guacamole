using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Guacamole.Pipeline;

namespace Guacamole;

/// <summary>
/// An immutable array with value equality: two instances are equal when they have the same
/// elements in the same order. Use it instead of <see cref="ImmutableArray{T}"/> for members of
/// records (and other types with compiler-generated equality) that flow through a pipeline.
/// </summary>
/// <remarks>
/// <para>
/// A record's compiler-generated <c>Equals</c> compares each member with
/// <see cref="EqualityComparer{T}.Default"/>, which compares an <see cref="ImmutableArray{T}"/>
/// (and arrays, lists, dictionaries and other collections) by reference. A record holding a newly
/// built array is therefore never equal to the previous result, even when the elements are the
/// same, and the equality cut-off never stops recomputation below that stage. An
/// <see cref="EquatableArray{T}"/> member makes the record equal whenever its elements are.
/// </para>
/// <para>
/// Elements are compared with the default comparer the pipeline uses for <typeparamref name="T"/>:
/// <see cref="EqualityComparer{T}.Default"/>, except that nested <see cref="ImmutableArray{T}"/>,
/// immutable dictionaries and value tuples containing them are compared structurally. The elements
/// themselves need value equality (records whose members have it, <see cref="IEquatable{T}"/>
/// implementations, primitives, strings, enums).
/// </para>
/// <para>
/// <c>default(EquatableArray&lt;T&gt;)</c> is an empty array: it has no elements, equals
/// <see cref="Empty"/> and enumerates nothing, so records with an unset member behave predictably.
/// Equality is O(N) in the number of elements (O(1) for the same underlying array) and the hash
/// code is computed from all elements on each call.
/// </para>
/// </remarks>
/// <typeparam name="T">The element type</typeparam>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _array;

    /// <summary>
    /// Wraps <paramref name="array"/> without copying it. A default (uninitialized)
    /// <see cref="ImmutableArray{T}"/> gives an empty array.
    /// </summary>
    /// <param name="array">The elements</param>
    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array.IsDefault ? ImmutableArray<T>.Empty : array;
    }

    /// <summary>
    /// An empty array (equal to <c>default(EquatableArray&lt;T&gt;)</c>).
    /// </summary>
    public static EquatableArray<T> Empty => default;

    /// <summary>
    /// The number of elements.
    /// </summary>
    public int Count => _array.IsDefault ? 0 : _array.Length;

    /// <summary>
    /// Whether the array has no elements.
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// The element at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The zero-based index</param>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside the array.</exception>
    public T this[int index] => AsImmutableArray()[index];

    /// <summary>
    /// Returns the elements as an <see cref="ImmutableArray{T}"/> (never a default one: an empty
    /// <see cref="EquatableArray{T}"/> gives <see cref="ImmutableArray{T}.Empty"/>). No copy is made.
    /// </summary>
    /// <returns>The wrapped array</returns>
    public ImmutableArray<T> AsImmutableArray() => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    /// <summary>
    /// Returns the elements as a read-only span.
    /// </summary>
    /// <returns>A span over the elements</returns>
    public ReadOnlySpan<T> AsSpan() => AsImmutableArray().AsSpan();

    /// <summary>
    /// Whether both arrays have equal elements in the same order.
    /// </summary>
    /// <param name="other">The array to compare with</param>
    /// <returns><see langword="true"/> when the elements are equal</returns>
    public bool Equals(EquatableArray<T> other) =>
        ImmutableArrayEqualityComparer<T>.Default.Equals(AsImmutableArray(), other.AsImmutableArray());

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <summary>
    /// A hash code computed from all elements, consistent with <see cref="Equals(EquatableArray{T})"/>.
    /// </summary>
    /// <returns>The hash code</returns>
    public override int GetHashCode() => ImmutableArrayEqualityComparer<T>.Default.GetHashCode(AsImmutableArray());

    /// <summary>
    /// Returns an enumerator over the elements (allocation-free in <c>foreach</c>).
    /// </summary>
    /// <returns>The enumerator</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)AsImmutableArray()).GetEnumerator();

    /// <summary>
    /// Returns the element count, for example <c>EquatableArray[3]</c>.
    /// </summary>
    /// <returns>A short description</returns>
    public override string ToString() => $"EquatableArray[{Count}]";

    /// <summary>
    /// Whether both arrays have equal elements in the same order.
    /// </summary>
    /// <param name="left">The first array</param>
    /// <param name="right">The second array</param>
    /// <returns><see langword="true"/> when the elements are equal</returns>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>
    /// Whether the arrays differ in length or in any element.
    /// </summary>
    /// <param name="left">The first array</param>
    /// <param name="right">The second array</param>
    /// <returns><see langword="true"/> when the elements are not equal</returns>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>
    /// Wraps <paramref name="array"/> without copying it.
    /// </summary>
    /// <param name="array">The elements</param>
    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);
}

/// <summary>
/// Creates <see cref="EquatableArray{T}"/> instances.
/// </summary>
public static class EquatableArray
{
    /// <summary>
    /// Creates an array with a copy of <paramref name="items"/>. Also used by collection
    /// expressions: <c>EquatableArray&lt;int&gt; numbers = [1, 2, 3];</c>
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    /// <param name="items">The elements</param>
    /// <returns>The array</returns>
    public static EquatableArray<T> Create<T>(params ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));

    /// <summary>
    /// Wraps <paramref name="array"/> without copying it.
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    /// <param name="array">The elements</param>
    /// <returns>The array</returns>
    public static EquatableArray<T> AsEquatableArray<T>(this ImmutableArray<T> array) => new(array);

    /// <summary>
    /// Creates an array with the elements of <paramref name="items"/>, in enumeration order.
    /// </summary>
    /// <typeparam name="T">The element type</typeparam>
    /// <param name="items">The elements</param>
    /// <returns>The array</returns>
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new(items.ToImmutableArray());
    }
}
