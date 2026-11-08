namespace Guacamole.Benchmarks.Infrastructure.TestScenarios;

/// <summary>
/// N single-value sources, each mapped by a per-leaf computation, reduced pairwise by a balanced
/// binary tree of <c>Combine</c> + <c>Select</c> stages to one total:
/// <code>
/// Leaf0 -> Select(work) \
///                        Combine -> Select(sum) \
/// Leaf1 -> Select(work) /                        Combine -> Select(sum) -> ... -> Root
/// ...
/// </code>
/// Every stage feeds the root, so changing one source invalidates exactly one path of
/// 1 + 2 * log2(N) stages (the leaf computation and one Combine + Select per tree level); the
/// other stages are only verified. The leaf computation is a parameter so tests can count calls.
/// </summary>
public sealed class ReductionTreeScenario : IDisposable
{
    private ReductionTreeScenario(
        BenchmarkContext context,
        IIncrementalValueSource<int>[] sources,
        IncrementalValueProvider<long> root,
        BenchmarkScenario scenario)
    {
        Context = context;
        Sources = sources;
        Root = root;
        Scenario = scenario;
    }

    /// <summary>The context that owns the sources.</summary>
    public BenchmarkContext Context { get; }

    /// <summary>The leaf sources; source <c>i</c> initially holds <c>i</c>.</summary>
    public IIncrementalValueSource<int>[] Sources { get; }

    /// <summary>Sum of the leaf computation over all sources.</summary>
    public IncrementalValueProvider<long> Root { get; }

    /// <summary>Shape of the graph.</summary>
    public BenchmarkScenario Scenario { get; }

    /// <summary>Builds the tree.</summary>
    /// <param name="leaves">Number of sources; must be a power of two.</param>
    /// <param name="leafWork">The per-leaf computation.</param>
    public static ReductionTreeScenario Build(int leaves, Func<int, long> leafWork)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(leaves, 1);
        ArgumentNullException.ThrowIfNull(leafWork);
        if (!int.IsPow2(leaves))
            throw new ArgumentException("The number of leaves must be a power of two.", nameof(leaves));

        var context = new BenchmarkContext();
        var sources = new IIncrementalValueSource<int>[leaves];
        var level = new List<IncrementalValueProvider<long>>(leaves);
        for (int i = 0; i < leaves; i++)
        {
            sources[i] = context.AddValueSource<int>($"Leaf{i}");
            sources[i].Set(i);
            level.Add(sources[i].AsProvider().Select(leafWork));
        }

        int nodeCount = 2 * leaves; // sources + leaf computations
        int depth = 1;
        while (level.Count > 1)
        {
            var next = new List<IncrementalValueProvider<long>>(level.Count / 2);
            for (int i = 0; i < level.Count; i += 2)
                next.Add(level[i].Combine(level[i + 1]).Select(static pair => pair.Item1 + pair.Item2));

            nodeCount += 2 * next.Count;
            depth++;
            level = next;
        }

        var scenario = new BenchmarkScenario(
            Name: $"ReductionTree_{leaves}Leaves",
            NodeCount: nodeCount,
            DepthLevels: depth,
            GraphStructure: GraphStructure.Tree,
            Description: $"{leaves} value sources reduced to one total by a balanced binary tree");

        return new ReductionTreeScenario(context, sources, level[0], scenario);
    }

    /// <inheritdoc />
    public void Dispose() => Context.Dispose();
}
