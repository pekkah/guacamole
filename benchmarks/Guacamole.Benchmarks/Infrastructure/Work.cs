namespace Guacamole.Benchmarks.Infrastructure;

/// <summary>
/// Deterministic, allocation-free CPU work standing in for a real per-node computation
/// (parsing a file, type-checking a declaration). Without it the benchmarks would mostly measure
/// how cheap an integer addition is, which says little about the cost of skipping work.
/// </summary>
public static class Work
{
    /// <summary>Number of xorshift rounds per call.</summary>
    public const int Iterations = 64;

    /// <summary>Returns a value derived from <paramref name="value"/> after <see cref="Iterations"/> rounds of mixing.</summary>
    public static long Mix(int value)
    {
        ulong h = (ulong)value + 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < Iterations; i++)
        {
            h ^= h << 13;
            h ^= h >> 7;
            h ^= h << 17;
        }

        // Keep results small so that sums over many results cannot overflow
        return (long)(h & 0xFFFF);
    }

    /// <summary><see cref="Mix(int)"/> of the item's value.</summary>
    public static long Mix(TestScenarios.Item item) => Mix(item.Value);
}
