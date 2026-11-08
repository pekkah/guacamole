using Guacamole.Benchmarks.Infrastructure;
using Guacamole.Benchmarks.Infrastructure.TestScenarios;
using Xunit;

namespace Guacamole.Benchmarks.Tests;

/// <summary>
/// Checks that the incremental benchmarks measure what they claim: cached reads run no work, a
/// change reruns only the affected computations, and the incremental and direct variants compute
/// the same result. Counting calls is deterministic, unlike timing assertions (spec 009 T011).
/// </summary>
public class IncrementalScenarioTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(1024)]
    public void ReductionTree_WarmReadRunsNothing_OneChangeRerunsOneLeaf(int leaves)
    {
        int calls = 0;
        using var scenario = ReductionTreeScenario.Build(leaves, value => { calls++; return Work.Mix(value); });
        var values = Enumerable.Range(0, leaves).ToArray();

        Assert.Equal(Direct(values), scenario.Root.Value);
        Assert.Equal(leaves, calls);
        Assert.Equal(2 + 4 * (leaves - 1), scenario.Scenario.NodeCount);

        calls = 0;
        Assert.Equal(Direct(values), scenario.Root.Value);
        Assert.Equal(0, calls);

        scenario.Sources[leaves / 3].Set(leaves * 10);
        values[leaves / 3] = leaves * 10;
        Assert.Equal(Direct(values), scenario.Root.Value);
        Assert.Equal(1, calls);

        static long Direct(int[] values) => values.Sum(Work.Mix);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void Collection_KChanges_RerunPerItemWorkKTimes(int changes)
    {
        const int itemCount = 1_000;
        int calls = 0;
        using var scenario = CollectionScenario.Build(CollectionScenario.CreateItems(itemCount), item => { calls++; return Work.Mix(item); });

        Assert.Equal(Direct(scenario.Items), scenario.Total.Value);
        Assert.Equal(itemCount, calls);

        calls = 0;
        Assert.Equal(Direct(scenario.Items), scenario.Total.Value);
        Assert.Equal(0, calls);

        scenario.ReplaceItems(start: itemCount - 5, changes); // wraps around the end
        Assert.Equal(Direct(scenario.Items), scenario.Total.Value);
        Assert.Equal(changes, calls);

        static long Direct(Item[] items) => items.Sum(Work.Mix);
    }

    [Fact]
    public void IncrementalUpdateBenchmarks_IncrementalAndDirectAgree()
    {
        var incremental = new IncrementalUpdateBenchmarks { Leaves = 16 };
        var direct = new IncrementalUpdateBenchmarks { Leaves = 16 };
        incremental.Setup();
        direct.Setup();
        try
        {
            Assert.Equal(direct.CachedRead(), incremental.CachedRead());
            for (int i = 0; i < 40; i++)
                Assert.Equal(direct.DirectRecompute(), incremental.OneSourceChanged());
        }
        finally
        {
            incremental.Cleanup();
            direct.Cleanup();
        }
    }

    [Fact]
    public void CollectionChangeBenchmarks_IncrementalAndDirectAgree()
    {
        var incremental = new CollectionChangeBenchmarks { Items = 1_000, Changes = 10 };
        var direct = new CollectionChangeBenchmarks { Items = 1_000, Changes = 10 };
        incremental.Setup();
        direct.Setup();
        try
        {
            for (int i = 0; i < 150; i++) // wraps around the items more than once
                Assert.Equal(direct.DirectRecompute(), incremental.Incremental());
        }
        finally
        {
            incremental.Cleanup();
            direct.Cleanup();
        }
    }

    [Fact]
    public void CollectionPipelineBenchmarks_IncrementalAndDirectAgree()
    {
        var incremental = new CollectionPipelineBenchmarks { Items = 1_000, Changes = 10 };
        var direct = new CollectionPipelineBenchmarks { Items = 1_000, Changes = 10 };
        incremental.Setup();
        direct.Setup();
        try
        {
            for (int i = 0; i < 150; i++) // wraps around the items more than once
                Assert.Equal(direct.DirectRecompute(), incremental.Incremental());
        }
        finally
        {
            incremental.Cleanup();
            direct.Cleanup();
        }
    }

    [Fact]
    public void MemoryOverheadBenchmarks_BothVariantsComputeTheSameTotal()
    {
        var benchmarks = new MemoryOverheadBenchmarks { Items = 1_000 };
        benchmarks.Setup();

        Assert.Equal(benchmarks.Direct(), benchmarks.BuildAndRead());
    }
}
