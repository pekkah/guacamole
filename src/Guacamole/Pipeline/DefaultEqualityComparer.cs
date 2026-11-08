using System.Collections.Immutable;

namespace Guacamole.Pipeline;

/// <summary>
/// The comparer a pipeline stage uses for its results when no comparer is passed to the operator.
/// It is <see cref="EqualityComparer{T}.Default"/>, except for types whose default equality is
/// by reference although they hold values that should be compared:
/// <list type="bullet">
/// <item><see cref="ImmutableArray{T}"/> is compared element-wise, in order
/// (<see cref="ImmutableArrayEqualityComparer{T}"/>), with the default comparer of its item type;</item>
/// <item>value tuples of two or three components (as produced by <c>Combine</c>) are compared
/// component-wise with each component's default comparer when at least one component needs
/// structural comparison, for example <c>(ImmutableArray&lt;int&gt;, string)</c>.</item>
/// </list>
/// These rules apply recursively (an immutable array of tuples, a tuple of tuples). Other types,
/// including lists, dictionaries and other collections, keep their own equality; pass a comparer
/// to the operator for them. The comparer is built once per type.
/// </summary>
/// <typeparam name="T">The compared type</typeparam>
internal static class DefaultEqualityComparer<T>
{
    /// <summary>
    /// The default comparer for <typeparamref name="T"/>.
    /// </summary>
    public static IEqualityComparer<T> Instance { get; } =
        (IEqualityComparer<T>?)DefaultEqualityComparer.CreateStructural(typeof(T)) ?? EqualityComparer<T>.Default;
}

/// <summary>
/// Builds the structural comparers of <see cref="DefaultEqualityComparer{T}"/>.
/// </summary>
internal static class DefaultEqualityComparer
{
    /// <summary>
    /// Returns a structural comparer for <paramref name="type"/>, or <see langword="null"/> when
    /// <see cref="EqualityComparer{T}.Default"/> already compares it by value (or the type is not
    /// one of the recognized shapes).
    /// </summary>
    internal static object? CreateStructural(Type type)
    {
        if (!type.IsGenericType || type.IsGenericTypeDefinition)
            return null;

        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();

        if (definition == typeof(ImmutableArray<>))
            return StaticProperty(typeof(ImmutableArrayEqualityComparer<>).MakeGenericType(arguments), nameof(ImmutableArrayEqualityComparer<object>.Default));

        Type? tupleComparer = null;
        if (definition == typeof(ValueTuple<,>))
            tupleComparer = typeof(ValueTupleEqualityComparer<,>);
        else if (definition == typeof(ValueTuple<,,>))
            tupleComparer = typeof(ValueTupleEqualityComparer<,,>);

        if (tupleComparer is null || !arguments.Any(NeedsStructural))
            return null;

        return Activator.CreateInstance(tupleComparer.MakeGenericType(arguments), arguments.Select(Get).ToArray());
    }

    private static bool NeedsStructural(Type type) => !ReferenceEquals(Get(type), DefaultOf(type));

    // DefaultEqualityComparer<type>.Instance
    private static object Get(Type type) =>
        StaticProperty(typeof(DefaultEqualityComparer<>).MakeGenericType(type), nameof(DefaultEqualityComparer<object>.Instance));

    // EqualityComparer<type>.Default
    private static object DefaultOf(Type type) =>
        StaticProperty(typeof(EqualityComparer<>).MakeGenericType(type), nameof(EqualityComparer<object>.Default));

    private static object StaticProperty(Type type, string name) => type.GetProperty(name)!.GetValue(null)!;
}

/// <summary>
/// Compares two-component value tuples component-wise with a comparer per component.
/// </summary>
internal sealed class ValueTupleEqualityComparer<T1, T2>(IEqualityComparer<T1> comparer1, IEqualityComparer<T2> comparer2)
    : IEqualityComparer<(T1, T2)>
{
    public bool Equals((T1, T2) x, (T1, T2) y) =>
        comparer1.Equals(x.Item1, y.Item1) && comparer2.Equals(x.Item2, y.Item2);

    public int GetHashCode((T1, T2) obj) =>
        HashCode.Combine(Hash(comparer1, obj.Item1), Hash(comparer2, obj.Item2));

    private static int Hash<T>(IEqualityComparer<T> comparer, T value) => value is null ? 0 : comparer.GetHashCode(value);
}

/// <summary>
/// Compares three-component value tuples component-wise with a comparer per component.
/// </summary>
internal sealed class ValueTupleEqualityComparer<T1, T2, T3>(IEqualityComparer<T1> comparer1, IEqualityComparer<T2> comparer2, IEqualityComparer<T3> comparer3)
    : IEqualityComparer<(T1, T2, T3)>
{
    public bool Equals((T1, T2, T3) x, (T1, T2, T3) y) =>
        comparer1.Equals(x.Item1, y.Item1) && comparer2.Equals(x.Item2, y.Item2) && comparer3.Equals(x.Item3, y.Item3);

    public int GetHashCode((T1, T2, T3) obj) =>
        HashCode.Combine(Hash(comparer1, obj.Item1), Hash(comparer2, obj.Item2), Hash(comparer3, obj.Item3));

    private static int Hash<T>(IEqualityComparer<T> comparer, T value) => value is null ? 0 : comparer.GetHashCode(value);
}
