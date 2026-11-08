using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;

namespace Guacamole.Benchmarks;

/// <summary>
/// Re-evaluation after one source change, on graphs of growing size (spec 006 T031/T032).
/// <para>
/// <see cref="ReductionTreeScenario"/> reduces <see cref="Leaves"/> sources to one total, so one
/// change recomputes 1 + 2 * log2(N) stages and only verifies the others. Compare
/// <see cref="OneSourceChanged"/> across sizes: the recomputed path grows logarithmically, while
/// <see cref="DirectRecompute"/> (the same total computed directly, without Guacamole) grows
/// linearly. Verification of the unchanged stages is still a walk over the graph, so the
/// incremental cost is not independent of N; the point is that no per-leaf work reruns.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class IncrementalUpdateBenchmarks
{
    private ReductionTreeScenario _scenario = null!;
    private int[] _values = null!;
    private int _next;

    /// <summary>Number of sources (leaves of the reduction tree).</summary>
    [Params(16, 256, 4096)]
    public int Leaves { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scenario = ReductionTreeScenario.Build(Leaves, Work.Mix);
        _values = Enumerable.Range(0, Leaves).ToArray();
        _next = Leaves;
        _ = _scenario.Root.Value; // warm the cache
    }

    [GlobalCleanup]
    public void Cleanup() => _scenario.Dispose();

    /// <summary>Baseline: no source changed, the cached total is returned.</summary>
    [Benchmark]
    public long CachedRead() => _scenario.Root.Value;

    /// <summary>One source (a different one each time) gets a new value, then the total is read.</summary>
    [Benchmark]
    public long OneSourceChanged()
    {
        int value = _next++;
        _scenario.Sources[value % Leaves].Set(value);
        return _scenario.Root.Value;
    }

    /// <summary>Non-incremental baseline: apply the same change to a plain array and recompute the total from scratch.</summary>
    [Benchmark(Baseline = true)]
    public long DirectRecompute()
    {
        int value = _next++;
        _values[value % Leaves] = value;

        long total = 0;
        foreach (var v in _values)
            total += Work.Mix(v);
        return total;
    }
}
