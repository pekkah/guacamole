using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Guacamole.SourceGenerators.Models;

/// <summary>
/// An immutable array wrapper with value (sequence) equality, so it can be used in
/// incremental generator models and participate in pipeline caching.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly T[]? _array;

    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array.IsDefaultOrEmpty ? null : array.ToArray();
    }

    public EquatableArray(IEnumerable<T> items)
    {
        var array = items is T[] a ? (T[])a.Clone() : new List<T>(items).ToArray();
        _array = array.Length == 0 ? null : array;
    }

    public int Count => _array?.Length ?? 0;

    public bool IsEmpty => Count == 0;

    public T this[int index] => (_array ?? Array.Empty<T>())[index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = _array ?? Array.Empty<T>();
        var right = other._array ?? Array.Empty<T>();

        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array is null)
            return 0;

        unchecked
        {
            var hash = 17;
            foreach (var item in _array)
            {
                hash = (hash * 31) + (item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item));
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T>
        => new(items);
}
