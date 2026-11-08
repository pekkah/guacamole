using BenchmarkDotNet.Attributes;
using Guacamole.Benchmarks.Infrastructure;

namespace Guacamole.Benchmarks;

/// <summary>
/// Several changes to one source with a large fan-out between two reads (issue #71).
/// <para>
/// One source feeds <see cref="FanOut"/> stages. <see cref="ManyChangesThenRead"/> changes the
/// source 100 times and then reads one of the stages (the time shown is per change). Each change
/// marks the stages downstream of the source dirty; once they are marked, further changes before
/// the next read have nothing new to mark, so the cost should be one walk over the fan-out plus a
/// small constant per change, not one walk per change. <see cref="OneChangeThenRead"/> is the same
/// with a single change.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SourceFanOutBenchmarks
{
    private const int Changes = 100;

    private BenchmarkContext _context = null!;
    private IIncrementalValueSource<int> _source = null!;
    private IncrementalValueProvider<int>[] _stages = null!;
    private int _next;

    /// <summary>Number of stages reading the source.</summary>
    [Params(100, 1000, 10000)]
    public int FanOut { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _context = new BenchmarkContext();
        _source = _context.AddValueSource<int>("Source");
        _source.Set(0);
        var provider = _source.AsProvider();
        _stages = new IncrementalValueProvider<int>[FanOut];
        for (var i = 0; i < FanOut; i++)
        {
            var offset = i;
            _stages[i] = provider.Select(x => x + offset);
        }

        // Read every stage once, so that each has recorded its edge to the source
        foreach (var stage in _stages)
            _ = stage.Value;
    }

    [GlobalCleanup]
    public void Cleanup() => _context.Dispose();

    /// <summary>The source changes 100 times, then one stage is read (time per change).</summary>
    [Benchmark(OperationsPerInvoke = Changes)]
    public int ManyChangesThenRead()
    {
        for (var i = 0; i < Changes; i++)
            _source.Set(++_next);

        return _stages[0].Value;
    }

    /// <summary>The source changes once, then one stage is read.</summary>
    [Benchmark]
    public int OneChangeThenRead()
    {
        _source.Set(++_next);
        return _stages[0].Value;
    }
}
