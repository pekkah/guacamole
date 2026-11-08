using System;
using System.Collections.Generic;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #89: failed results and <c>null</c> values are cached with the versions of the inputs
/// they were computed from, like any other value. A failing stage runs once per change of its
/// inputs, an error that recomputes to an equal error keeps everything downstream cached, and
/// <c>null</c> is a value that takes part in change detection.
/// </summary>
public partial class ErrorAndNullCachingTests
{
    [ComputingContext]
    private partial class CachingContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> A { get; }
        public partial IncrementalValueProvider<int> B { get; }
    }

    [ComputingContext]
    private partial class TinyCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 2;

        public partial IncrementalValueProvider<int> A { get; }
        public partial IncrementalValueProvider<int> B { get; }
    }

    private static int ThrowIfNegative(int value) => value < 0
        ? throw new InvalidOperationException(value < -100 ? "far below zero" : "below zero")
        : value;

    private static long VersionOf<T>(ComputingContextBase context, IncrementalValueProvider<T> provider) =>
        context.Graph.Versions.GetOutputVersion(provider.Node.Id);

    [Fact]
    public void FailingStage_RunsOncePerInputChange_NotPerRead()
    {
        using var context = new CachingContext();
        context.ASource.Set(-1);
        context.BSource.Set(0);
        var runs = 0;
        var failing = context.A.Select(a => { runs++; return ThrowIfNegative(a); });
        var downstream = failing.Select(x => x * 2);
        var delivered = new List<int>();
        using var output = context.RegisterOutput(downstream, (value, _) => delivered.Add(value));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("below zero", downstream.GetValue().ErrorValue!.Message);
            Assert.Equal("below zero", failing.GetValue().ErrorValue!.Message);
        }

        Assert.Equal(1, runs);

        // Changes of a source the failing stage does not read run the output handlers, which read
        // the failing pipeline again; the cached error is still valid
        for (var i = 1; i <= 5; i++)
            context.BSource.Set(i);

        Assert.Equal(1, runs);
        Assert.Empty(delivered);

        context.ASource.Set(-2);
        Assert.True(downstream.GetValue().IsError);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Recovery_PropagatesDownstream_AndToOutputHandlers()
    {
        using var context = new CachingContext();
        context.ASource.Set(3);
        var failing = context.A.Select(ThrowIfNegative);
        var downstream = failing.Select(x => x * 2).Combine(context.B).Select(p => p.Item1 + p.Item2);
        context.BSource.Set(0);
        var delivered = new List<int>();
        using var output = context.RegisterOutput(downstream, (value, _) => delivered.Add(value));
        Assert.Equal(6, downstream.Value);

        context.ASource.Set(-1);
        Assert.Equal("below zero", downstream.GetValue().ErrorValue!.Message);
        Assert.Throws<InvalidOperationException>(() => downstream.Value);

        context.ASource.Set(4);
        Assert.Equal(8, downstream.Value);
        Assert.Equal(4, failing.Value);

        // The handler is skipped while the result is an error and runs once it recovers
        Assert.Equal([8], delivered);
    }

    [Fact]
    public void ErrorRecomputedToAnEqualError_KeepsItsVersion_SoDownstreamStaysCached()
    {
        using var context = new CachingContext();
        context.ASource.Set(-1);
        var failing = context.A.Select(ThrowIfNegative);
        var downstream = failing.Select(x => x + 1);
        Assert.True(downstream.GetValue().IsError);
        var failingVersion = VersionOf(context, failing);
        var downstreamVersion = VersionOf(context, downstream);

        // Another input that fails with the same message: an equal error, no change downstream
        context.ASource.Set(-2);
        Assert.Equal("below zero", downstream.GetValue().ErrorValue!.Message);
        Assert.Equal(failingVersion, VersionOf(context, failing));
        Assert.Equal(downstreamVersion, VersionOf(context, downstream));

        // A different error is a change
        context.ASource.Set(-200);
        Assert.Equal("far below zero", downstream.GetValue().ErrorValue!.Message);
        Assert.NotEqual(failingVersion, VersionOf(context, failing));
        Assert.NotEqual(downstreamVersion, VersionOf(context, downstream));

        // So is a value after an error
        var errorVersion = VersionOf(context, failing);
        context.ASource.Set(5);
        Assert.Equal(6, downstream.Value);
        Assert.NotEqual(errorVersion, VersionOf(context, failing));
    }

    [Fact]
    public void EqualErrors_WithDifferentExceptionTypes_CountAsEqual()
    {
        // Documented: errors are compared by ComputationError value equality, which does not record
        // the exception type, so these two failures are indistinguishable to every consumer
        using var context = new CachingContext();
        context.ASource.Set(1);
        var failing = context.A.Select<int, int>(a =>
        {
            if (a == 1)
                throw new InvalidOperationException("failed");
            throw new ArgumentException("failed");
        });
        Assert.True(failing.GetValue().IsError);
        var version = VersionOf(context, failing);

        context.ASource.Set(2);
        Assert.Equal("failed", failing.GetValue().ErrorValue!.Message);
        Assert.Equal(version, VersionOf(context, failing));
    }

    [Fact]
    public void NullValues_AreCached_AndTakePartInChangeDetection()
    {
        using var context = new CachingContext();
        context.ASource.Set(0);
        context.BSource.Set(0);
        var runs = 0;
        var downstreamRuns = 0;
        var maybeNull = context.A.Select(a => { runs++; return a % 2 == 0 ? null : $"odd {a}"; });
        var described = maybeNull.Select(s => { downstreamRuns++; return s ?? "<null>"; });
        var delivered = new List<string?>();
        using var output = context.RegisterOutput(maybeNull, (value, _) => delivered.Add(value));

        for (var i = 0; i < 3; i++)
        {
            Assert.Null(maybeNull.Value);
            Assert.True(maybeNull.TryGetValue(out var value));
            Assert.Null(value);
            Assert.Equal("<null>", described.Value);
        }

        Assert.Equal(1, runs);
        Assert.Equal(1, downstreamRuns);

        // Unrelated changes (output handlers read the provider again): still cached
        context.BSource.Set(1);
        context.BSource.Set(2);
        Assert.Equal(1, runs);

        // null again: the stage reruns, but null equals null, so downstream stays cached
        context.ASource.Set(2);
        Assert.Equal("<null>", described.Value);
        Assert.Equal(2, runs);
        Assert.Equal(1, downstreamRuns);

        // A value after null is a change, and null after a value too
        context.ASource.Set(3);
        Assert.Equal("odd 3", described.Value);
        context.ASource.Set(4);
        Assert.Equal("<null>", described.Value);
        Assert.Equal(3, downstreamRuns);

        // The handler gets a null value like any other value: first at the first source change after
        // registration, then only when the value changes (not for the second null)
        Assert.Equal([null, "odd 3", null], delivered);
    }

    [Fact]
    public void NullNullableValueType_IsCached()
    {
        using var context = new CachingContext();
        context.ASource.Set(0);
        var runs = 0;
        var nullable = context.A.Select(a => { runs++; return a == 0 ? (int?)null : a; });

        Assert.Null(nullable.Value);
        Assert.Null(nullable.GetValue().ValueOrDefault);
        Assert.True(nullable.GetValue().IsSuccess);
        Assert.Equal(1, runs);

        context.ASource.Set(7);
        Assert.Equal(7, nullable.Value);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void EvictedErrors_AreRecomputedCorrectly()
    {
        using var context = new TinyCacheContext();
        context.ASource.Set(-1);
        context.BSource.Set(1);
        var failing = context.A.Select(ThrowIfNegative);
        var other = context.B.Select(b => b * 3);
        var total = failing.Combine(other).Select(p => p.Item1 + p.Item2);

        for (var i = 0; i < 30; i++)
        {
            // Alternate between failing and succeeding inputs while other stages evict the entries
            context.ASource.Set(i % 3 == 0 ? -i - 1 : i);
            context.BSource.Set(i);
            var result = total.GetValue();
            if (i % 3 == 0)
                Assert.Equal("below zero", result.ErrorValue!.Message);
            else
                Assert.Equal(i + i * 3, result.ValueOrDefault);

            Assert.Equal(i * 3, other.Value);
        }
    }
}
