namespace Guacamole.Benchmarks.Infrastructure;

/// <summary>
/// A hand-written <see cref="ComputingContextBase"/> for benchmarks that need a variable number
/// of sources (a generated <c>[ComputingContext]</c> has one source per property). It uses only
/// the public API: sources are created with the protected <c>CreateSource</c>/<c>CreateValueSource</c>
/// helpers that generated constructors call.
/// </summary>
public sealed class BenchmarkContext : ComputingContextBase
{
    /// <summary>
    /// Value cache capacity. Large enough that the biggest benchmark graph (about 16,000 stages)
    /// never evicts, so the benchmarks measure recomputation rather than cache misses.
    /// </summary>
    public const int Capacity = 1 << 17;

    /// <inheritdoc />
    protected override int CacheCapacity => Capacity;

    /// <summary>Creates a single-value source owned by this context.</summary>
    public IIncrementalValueSource<T> AddValueSource<T>(string sourceId) => CreateValueSource<T>(sourceId);

    /// <summary>Creates a collection source owned by this context.</summary>
    public IIncrementalSource<T> AddSource<T>(string sourceId) => CreateSource<T>(sourceId);
}
