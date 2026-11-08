using System;
using System.Collections.Generic;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// A result computed while a dependency read threw is kept only for the evaluation pass (#89).
/// A node that reads such a result during the same pass depends on that failure too and must not
/// cache it, or it keeps serving the failure after its inputs recover.
/// </summary>
public partial class UncachedResultPropagationTests
{
    [ComputingContext]
    private partial class ThreeSlotContext : ComputingContextBase
    {
        protected override int CacheCapacity => 3;

        public partial IncrementalValueProvider<string> S { get; }
    }

    /// <summary>A comparer that throws when comparing "1" with "2" (the stored value with the new one).</summary>
    private sealed class ThrowingComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) =>
            x == "1" && y == "2" ? throw new InvalidOperationException("comparer failed") : x == y;

        public int GetHashCode(string obj) => obj.GetHashCode(StringComparison.Ordinal);
    }

    private static string Describe<T>(Func<Result<T, ComputationError>> read)
    {
        try
        {
            var result = read();
            return result.IsSuccess ? "ok:" + result.ValueOrDefault : result.IsNone ? "none" : "error";
        }
        catch (Exception)
        {
            return "error";
        }
    }

    [Fact]
    public void ReaderOfAPassOnlyResult_StaysConsistentWithItsInput()
    {
        using var context = new ThreeSlotContext();
        var y = context.S.Select(v => v, new ThrowingComparer());
        var x = y.Select(v => v + "x");
        var z = x.Select(v => v + "z");
        var w1 = context.S.Select(v => v + "w1");
        var w2 = context.S.Select(v => v + "w2");
        var w3 = context.S.Select(v => v + "w3");

        context.SSource.Set("1");
        _ = z.GetValue();
        _ = y.GetValue();
        _ = z.GetValue();
        _ = w1.GetValue(); // evicts x

        // x is recomputed while verifying z; y's commit throws inside x's read, so x's result is pass-only
        context.SSource.Set("2");
        _ = Describe(z.GetValue);

        // Evict y's stale entry, reading z in between
        foreach (var w in new[] { w2, w3, w1, w2 })
        {
            _ = w.GetValue();
            _ = Describe(z.GetValue);
        }

        // Without a source change, z must agree with x: a cached failure would leave z stuck
        var xState = Describe(x.GetValue);
        var zState = Describe(z.GetValue);
        if (xState.StartsWith("ok:", StringComparison.Ordinal))
            Assert.Equal(xState + "z", zState);
        else
            Assert.Equal("error", zState);
    }
}
