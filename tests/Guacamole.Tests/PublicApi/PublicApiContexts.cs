using Xunit;

namespace Guacamole.Tests.PublicApi;

// Contexts and helpers shared by the public API behaviour tests. The tests in this folder use the
// library the way an application does: through [ComputingContext] partial contexts, the generated
// source accessors, the provider operators and the public result types, never internals.
//
// Values are read through enumeration and Result helpers rather than specific collection or
// property types, so the tests keep compiling when those types are refined.

/// <summary>An immutable item with value equality.</summary>
public sealed record Item(string Name, int Value);

/// <summary>A context with one input of each kind and a few extra inputs for Combine/Merge.</summary>
[ComputingContext]
public partial class ApiContext : ComputingContextBase
{
    public partial IncrementalValuesProvider<Item> Items { get; }
    public partial IncrementalValuesProvider<Item> OtherItems { get; }
    public partial IncrementalValuesProvider<string> Words { get; }
    public partial IncrementalValueProvider<int> Number { get; }
    public partial IncrementalValueProvider<string> Text { get; }
}

/// <summary>A context with a derived (user-managed) property assigned in a chained constructor.</summary>
[ComputingContext]
public partial class DerivedPropertyContext : ComputingContextBase
{
    public partial IncrementalValuesProvider<Item> Items { get; }

    public IncrementalValueProvider<int> Total { get; private set; }

    public DerivedPropertyContext(int bonus) : this()
    {
        Total = Items.Collect().Select(items => items.Sum(i => i.Value) + bonus);
    }
}

/// <summary>Counts how often a lambda ran, thread-safely.</summary>
public sealed class CallCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);

    public T Pass<T>(T value)
    {
        Increment();
        return value;
    }
}

/// <summary>Result helpers that do not depend on the name of the Result's value property.</summary>
public static class ResultAssert
{
    public static T Success<T>(Result<T, ComputationError> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but was {result}");
        return result.Match(value => value, () => throw new InvalidOperationException("None"), error => throw new InvalidOperationException(error.Message));
    }

    public static ComputationError Error<T>(Result<T, ComputationError> result)
    {
        Assert.True(result.IsError, $"Expected an error but was {result}");
        Assert.False(result.IsSuccess);
        Assert.False(result.IsNone);
        return result.ErrorValue!;
    }

    public static void None<T>(Result<T, ComputationError> result)
    {
        Assert.True(result.IsNone, $"Expected None but was {result}");
        Assert.False(result.IsSuccess);
        Assert.False(result.IsError);
        Assert.Null(result.ErrorValue);
    }
}
