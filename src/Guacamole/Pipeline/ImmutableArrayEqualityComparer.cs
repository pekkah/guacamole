using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// Compares immutable arrays element-wise, in order, with an item comparer.
/// <see cref="EqualityComparer{T}.Default"/> compares <see cref="ImmutableArray{T}"/> values by
/// their underlying array reference, so two separately built arrays with equal items never compare
/// equal with it.
/// </summary>
/// <typeparam name="T">The item type</typeparam>
internal sealed class ImmutableArrayEqualityComparer<T> : IEqualityComparer<ImmutableArray<T>>
{
    /// <summary>
    /// Compares items with their default comparer (<see cref="DefaultEqualityComparer{T}"/>:
    /// <see cref="EqualityComparer{T}.Default"/>, or element-wise for nested immutable arrays).
    /// </summary>
    public static ImmutableArrayEqualityComparer<T> Default { get; } = new(DefaultEqualityComparer<T>.Instance);

    public ImmutableArrayEqualityComparer(IEqualityComparer<T> itemComparer)
    {
        ItemComparer = itemComparer ?? throw new ArgumentNullException(nameof(itemComparer));
    }

    /// <summary>
    /// The comparer used for the items.
    /// </summary>
    public IEqualityComparer<T> ItemComparer { get; }

    public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y)
    {
        if (x.IsDefault || y.IsDefault)
            return x.IsDefault && y.IsDefault;

        if (x.Length != y.Length)
            return false;

        if (x == y) // same underlying array
            return true;

        for (var i = 0; i < x.Length; i++)
        {
            if (!ItemComparer.Equals(x[i], y[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(ImmutableArray<T> obj)
    {
        if (obj.IsDefault)
            return 0;

        var hash = new HashCode();
        foreach (var item in obj)
        {
            hash.Add(item is null ? 0 : ItemComparer.GetHashCode(item));
        }

        return hash.ToHashCode();
    }
}
