using System;
using System.Diagnostics;
using Xunit;

namespace Guacamole.Tests.Common;

/// <summary>
/// Complexity checks that do not depend on the speed of the machine (issue #68).
/// </summary>
/// <remarks>
/// Instead of comparing a wall-clock time with an absolute budget, which fails on a slow or busy CI
/// runner, a check measures the time per operation at a small and at a large size, after a warm-up,
/// and asserts that their ratio stays below a generous bound. The best of several repetitions is used
/// at each size, so a pause (GC, scheduling, other tests running in parallel) in one repetition does
/// not count, and a failing check is measured again a few times before it fails.
/// <para>
/// Choose the sizes so that the expected and the regressed complexity are far apart: for an O(1)
/// operation measured at sizes 32x apart, an O(n) regression gives a ratio of about 32, while the
/// bound is typically 8.
/// </para>
/// </remarks>
internal static class ScalingAssert
{
    private const int Repetitions = 7;
    private const int Attempts = 3;

    /// <summary>
    /// Asserts that the time per operation at <paramref name="largeSize"/> is at most
    /// <paramref name="maxRatio"/> times the time per operation at <paramref name="smallSize"/>.
    /// </summary>
    /// <param name="prepare">Builds the state for a size (not timed) and returns the timed action
    /// and the number of operations it performs.</param>
    /// <param name="smallSize">The small size</param>
    /// <param name="largeSize">The large size</param>
    /// <param name="maxRatio">The largest accepted ratio of the times per operation</param>
    /// <param name="what">Describes the operation, for the failure message</param>
    public static void PerOperationTimeScales(
        Func<int, (Action Run, int Operations)> prepare,
        int smallSize,
        int largeSize,
        double maxRatio,
        string what)
    {
        // Warm up both sizes (JIT, tiering, type initialization, first-use allocations)
        prepare(smallSize).Run();
        prepare(largeSize).Run();

        var ratio = double.NaN;
        double small = 0, large = 0;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            small = BestTicksPerOperation(prepare, smallSize);
            large = BestTicksPerOperation(prepare, largeSize);
            ratio = large / Math.Max(small, double.Epsilon);
            if (ratio <= maxRatio)
                return;
        }

        Assert.Fail(
            $"{what}: time per operation at size {largeSize} is {ratio:F1}x the time at size {smallSize} " +
            $"({ToNanoseconds(large):F0} ns vs {ToNanoseconds(small):F0} ns), expected at most {maxRatio}x.");
    }

    /// <summary>
    /// Returns the bytes allocated on the current thread by <paramref name="action"/>, after a warm-up run.
    /// </summary>
    public static long AllocatedBytes(Action action)
    {
        action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static double BestTicksPerOperation(Func<int, (Action Run, int Operations)> prepare, int size)
    {
        var best = double.MaxValue;
        for (var i = 0; i < Repetitions; i++)
        {
            var (run, operations) = prepare(size);
            var start = Stopwatch.GetTimestamp();
            run();
            var elapsed = Stopwatch.GetTimestamp() - start;
            best = Math.Min(best, (double)elapsed / Math.Max(operations, 1));
        }

        return best;
    }

    private static double ToNanoseconds(double ticks) => ticks * 1_000_000_000.0 / Stopwatch.Frequency;
}
