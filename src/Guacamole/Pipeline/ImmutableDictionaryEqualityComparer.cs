using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Compares immutable dictionaries entry by entry. <see cref="EqualityComparer{T}.Default"/>
/// compares <see cref="ImmutableDictionary{TKey, TValue}"/> instances by reference, so two
/// separately built dictionaries with equal entries never compare equal with it.
/// </summary>
/// <remarks>
/// Two dictionaries are equal when they are the same instance, or when they have equal key
/// comparers (by <see cref="object.Equals(object?)"/>, so <c>StringComparer.Ordinal</c> equals
/// itself but not <see cref="EqualityComparer{T}.Default"/>), the same count, and every key of
/// one is in the other (by the dictionaries' key comparer) with an equal value (by the value
/// comparer of this instance, not the dictionaries' own <c>ValueComparer</c>). Dictionaries with
/// different key comparers are never equal: whether they hold the same keys would depend on which
/// comparer is asked. Equality and hashing are O(N); the hash does not depend on the enumeration order.
/// </remarks>
/// <typeparam name="TKey">The key type</typeparam>
/// <typeparam name="TValue">The value type</typeparam>
internal sealed class ImmutableDictionaryEqualityComparer<TKey, TValue> : IEqualityComparer<ImmutableDictionary<TKey, TValue>>
    where TKey : notnull
{
    /// <summary>
    /// Compares values with their default comparer (<see cref="DefaultEqualityComparer{T}"/>).
    /// </summary>
    public static ImmutableDictionaryEqualityComparer<TKey, TValue> Default { get; } = new(DefaultEqualityComparer<TValue>.Instance);

    public ImmutableDictionaryEqualityComparer(IEqualityComparer<TValue> valueComparer)
    {
        ValueComparer = valueComparer ?? throw new ArgumentNullException(nameof(valueComparer));
    }

    /// <summary>
    /// The comparer used for the values.
    /// </summary>
    public IEqualityComparer<TValue> ValueComparer { get; }

    public bool Equals(ImmutableDictionary<TKey, TValue>? x, ImmutableDictionary<TKey, TValue>? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null || x.Count != y.Count || !Equals(x.KeyComparer, y.KeyComparer))
            return false;

        foreach (var (key, value) in x)
        {
            if (!y.TryGetValue(key, out var other) || !ValueComparer.Equals(value, other))
                return false;
        }

        return true;
    }

    public int GetHashCode(ImmutableDictionary<TKey, TValue> obj)
    {
        // A commutative sum of per-entry hashes, so the enumeration order does not matter. Keys are
        // hashed with the dictionary's key comparer, which equal dictionaries share.
        var keyComparer = obj.KeyComparer;
        var sum = 0;
        foreach (var (key, value) in obj)
        {
            sum = unchecked(sum + HashCode.Combine(keyComparer.GetHashCode(key), value is null ? 0 : ValueComparer.GetHashCode(value)));
        }

        return HashCode.Combine(obj.Count, sum);
    }
}

/// <summary>
/// Compares immutable sorted dictionaries entry by entry, in key order. Like
/// <see cref="ImmutableDictionaryEqualityComparer{TKey, TValue}"/>, two dictionaries are equal when
/// they are the same instance, or when they have equal key comparers, the same count, and pairwise
/// equal keys (by the key comparer) and values (by the value comparer of this instance). Equality
/// and hashing are O(N).
/// </summary>
/// <typeparam name="TKey">The key type</typeparam>
/// <typeparam name="TValue">The value type</typeparam>
internal sealed class ImmutableSortedDictionaryEqualityComparer<TKey, TValue> : IEqualityComparer<ImmutableSortedDictionary<TKey, TValue>>
    where TKey : notnull
{
    /// <summary>
    /// Compares values with their default comparer (<see cref="DefaultEqualityComparer{T}"/>).
    /// </summary>
    public static ImmutableSortedDictionaryEqualityComparer<TKey, TValue> Default { get; } = new(DefaultEqualityComparer<TValue>.Instance);

    public ImmutableSortedDictionaryEqualityComparer(IEqualityComparer<TValue> valueComparer)
    {
        ValueComparer = valueComparer ?? throw new ArgumentNullException(nameof(valueComparer));
    }

    /// <summary>
    /// The comparer used for the values.
    /// </summary>
    public IEqualityComparer<TValue> ValueComparer { get; }

    public bool Equals(ImmutableSortedDictionary<TKey, TValue>? x, ImmutableSortedDictionary<TKey, TValue>? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null || x.Count != y.Count || !Equals(x.KeyComparer, y.KeyComparer))
            return false;

        // Both enumerate in the order of the same key comparer, so equal dictionaries line up
        var keyComparer = x.KeyComparer;
        using var other = y.GetEnumerator();
        foreach (var (key, value) in x)
        {
            other.MoveNext();
            if (keyComparer.Compare(key, other.Current.Key) != 0 || !ValueComparer.Equals(value, other.Current.Value))
                return false;
        }

        return true;
    }

    public int GetHashCode(ImmutableSortedDictionary<TKey, TValue> obj)
    {
        // Values in key order: keys that an ordering comparer considers equal need not have equal
        // hash codes (for example with a case-insensitive comparer), so they are left out
        var hash = new HashCode();
        hash.Add(obj.Count);
        foreach (var value in obj.Values)
        {
            hash.Add(value is null ? 0 : ValueComparer.GetHashCode(value));
        }

        return hash.ToHashCode();
    }
}
