using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #60: a source change marks the nodes downstream of it dirty, and verification after a
/// change only looks into the marked nodes. These tests check the marking itself, that results
/// stay correct for every kind of stage (randomized against a direct computation), and that
/// changes racing with reads never leave a stale value behind.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class DirtyMarkingTests
{
    [ComputingContext]
    private partial class OracleContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> A { get; }
        public partial IncrementalValueProvider<int> B { get; }
        public partial IncrementalValueProvider<int> C { get; }
        public partial IncrementalValuesProvider<int> Items { get; }
    }

    [ComputingContext]
    private partial class TinyCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 2;

        public partial IncrementalValueProvider<int> A { get; }
        public partial IncrementalValueProvider<int> B { get; }
    }

    [Fact]
    public void SourceChange_MarksOnlyDownstreamNodes()
    {
        using var context = new OracleContext();
        context.ASource.Set(1);
        context.BSource.Set(2);
        var fromA = context.A.Select(a => a + 1);
        var fromB = context.B.Select(b => b + 1);
        var both = fromA.Combine(fromB).Select(pair => pair.Item1 * pair.Item2);
        Assert.Equal(6, both.Value);

        long Dirty<T>(IncrementalValueProvider<T> provider) =>
            context.Graph.Versions.GetState(provider.Node.Id).Dirty;

        var before = (A: Dirty(fromA), B: Dirty(fromB), Both: Dirty(both));
        context.ASource.Set(5);

        Assert.Equal(before.A + 1, Dirty(fromA));
        Assert.Equal(before.B, Dirty(fromB));
        Assert.Equal(before.Both + 1, Dirty(both));
        Assert.Equal(18, both.Value);
    }

    [Fact]
    public void UnchangedSubtree_IsNotRecomputedOrRewalked()
    {
        using var context = new OracleContext();
        context.ASource.Set(1);
        context.BSource.Set(2);
        var runsB = 0;
        var fromA = context.A.Select(a => a + 1);
        var fromB = context.B.Select(b => { runsB++; return b + 1; });
        var both = fromA.Combine(fromB).Select(pair => pair.Item1 * pair.Item2);
        Assert.Equal(6, both.Value);

        for (var i = 0; i < 10; i++)
        {
            context.ASource.Set(10 + i);
            Assert.Equal((11 + i) * 3, both.Value);
        }

        Assert.Equal(1, runsB);

        // The unchanged stage kept the stamp it got when it was first computed
        var entry = Assert.IsType<global::Guacamole.Pipeline.CachedNodeValue>(
            context.Graph.ValueCache.TryGet(fromB.Node.Id, out var cached) ? cached : null);
        Assert.Equal(context.Graph.Versions.GetState(fromB.Node.Id).Dirty, entry.Stamp);
    }

    [Fact]
    public void PathsOfDifferentLength_ToTheSameNode_SeeTheChange()
    {
        using var context = new OracleContext();
        context.ASource.Set(1);

        // A reaches `sum` directly and through a chain of five stages
        var chain = context.A;
        for (var i = 0; i < 5; i++)
            chain = chain.Select(x => x + 1);
        var sum = context.A.Combine(chain).Select(pair => pair.Item1 + pair.Item2);

        for (var a = 1; a <= 20; a++)
        {
            context.ASource.Set(a);
            Assert.Equal(a + a + 5, sum.Value);
        }
    }

    /// <summary>
    /// A read that overlaps a change which has marked a node but not yet the nodes between it and
    /// the source may compute a stale value; it must not stamp it, or the value would survive the
    /// change. (The race is too narrow for a stress test to hit reliably, so the change is applied
    /// step by step here.)
    /// </summary>
    [Fact]
    public void ReadDuringAPartiallyMarkedChange_DoesNotLeaveAStaleValue()
    {
        var graph = new global::Guacamole.Pipeline.ComputationGraph();
        var source = new IncrementalValueSource<int>("S", () => graph); // not subscribed: changes are marked by hand
        graph = graph.AddNode(source.GetNode());
        var state = graph.SharedState;

        var a = source.AsProvider();
        var chain = new List<IncrementalValueProvider<int>>();
        var last = a;
        for (var i = 0; i < 5; i++)
        {
            last = last.Select(x => x + 1);
            chain.Add(last);
        }

        var pair = a.Combine(last);
        var sum = pair.Select(p => p.Item1 + p.Item2);
        source.Set(1);
        state.MarkSourceChanged("S");
        Assert.Equal(1 + 6, sum.Value);

        // A change that has bumped the source and marked `pair` and `sum`, but not the chain yet
        source.Set(2);
        state.BeginSourceChange();
        var sourceState = state.Versions.GetState("S");
        sourceState.IncrementVersion();
        sourceState.MarkDirty();
        state.Versions.GetState(pair.Node.Id).MarkDirty();
        state.Versions.GetState(sum.Node.Id).MarkDirty();

        // Meanwhile a change of another source has finished and advanced the revision
        state.AdvanceRevision();

        // Sees the new source value through the direct edge and the old chain value: allowed while
        // the change is in progress
        Assert.Equal(2 + 6, sum.Value);

        foreach (var stage in chain)
            state.Versions.GetState(stage.Node.Id).MarkDirty();
        state.AdvanceRevision();
        state.EndSourceChange();

        Assert.Equal(2 + 7, sum.Value);
    }

    [Fact]
    public void ErrorUpstream_RecoversWhenTheSourceChanges()
    {
        using var context = new OracleContext();
        context.ASource.Set(1);
        var checkedValue = context.A.Select(a => a == 0 ? throw new InvalidOperationException("zero") : 100 / a);
        var described = checkedValue.Select(v => $"v={v}");

        Assert.Equal("v=100", described.Value);
        context.ASource.Set(0);
        Assert.True(described.GetValue().IsError);
        Assert.True(described.GetValue().IsError);
        context.ASource.Set(4);
        Assert.Equal("v=25", described.Value);
    }

    [Fact]
    public void EvictedValues_AreRecomputedCorrectly()
    {
        using var context = new TinyCacheContext();
        context.ASource.Set(1);
        context.BSource.Set(1);
        var a = context.A.Select(x => x * 2);
        var b = context.B.Select(x => x * 3);
        var total = a.Combine(b).Select(pair => pair.Item1 + pair.Item2).Select(x => x + 1);

        for (var i = 0; i < 30; i++)
        {
            if (i % 2 == 0)
                context.ASource.Set(i);
            else
                context.BSource.Set(i);

            var expectedA = i % 2 == 0 ? i : i - 1;
            var expectedB = i % 2 == 1 ? i : Math.Max(1, i - 1);
            Assert.Equal(expectedA * 2 + expectedB * 3 + 1, total.Value);
            Assert.Equal(expectedA * 2, a.Value);
        }
    }

    [Fact]
    public void DependentsOfALongLivedSource_StayBounded_WhenPipelinesAreDropped()
    {
        using var context = new OracleContext();
        context.ASource.Set(1);
        var state = context.Graph.Versions.GetState(context.A.Node.Id);

        for (var round = 0; round < 5; round++)
        {
            for (var i = 0; i < 200; i++)
            {
                var offset = round * 1000 + i;
                Assert.Equal(1 + offset, context.A.Select(x => x + offset).Value);
            }

            context.TrimUnusedNodes();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            context.TrimUnusedNodes();
        }

        // Collected dependents are pruned as the set grows: at most twice the live ones plus the
        // pipelines added since the last prune, never all 1,000 created
        Assert.True(state.DependentCount < 600, $"dependents: {state.DependentCount}");
        context.ASource.Set(2);
        Assert.Equal(2, context.A.Select(x => x).Value);
    }

    /// <summary>
    /// The model a pipeline is checked against: the sources' current contents.
    /// </summary>
    private sealed class Model
    {
        public int A;
        public int B;
        public int C;
        public readonly List<int> Items = new();
    }

    private sealed record Check(string Name, Func<object?> Actual, Func<Model, object?> Expected);

    /// <summary>
    /// A pipeline using every kind of stage, with equality cut-offs, "no value" and errors, and
    /// paths of different lengths to the same nodes; each check pairs a provider with the same
    /// computation done directly on the model.
    /// </summary>
    private static List<Check> BuildPipelines(OracleContext context)
    {
        static object? Read<T>(IncrementalValueProvider<T> provider)
        {
            var result = provider.GetValue();
            return result.IsError ? "error" : result.IsNone ? "none" : result.Value;
        }

        static object? ReadAll<T>(IncrementalValuesProvider<T> provider)
        {
            var result = provider.GetValues();
            return result.IsError ? "error" : string.Join(",", result.Value!);
        }

        var mod3 = context.A.Select(a => a % 3);
        var mod3TimesB = mod3.Combine(context.B).Select(p => p.Item1 * 100 + p.Item2);
        var aPlusC = context.A.Combine(context.C).Select(p => p.Item1 + p.Item2);
        var diamond = mod3TimesB.Combine(aPlusC).Select(p => p.Item1 - p.Item2);
        var evenA = context.A.Where(a => a % 2 == 0).Select(a => a * 2);
        var ratio = context.B.Select(b => b == 0 ? throw new InvalidOperationException() : 100 / b);
        var ratioPlusA = ratio.Combine(context.A).Select(p => p.Item1 + p.Item2);

        var buckets = context.Items.Select(i => i % 5);
        var bigBuckets = buckets.Where(b => b > 1);
        var bucketSum = bigBuckets.Collect().Select(arr => arr.Sum());
        var shifted = context.Items.Combine(context.B).Select(p => p.Left + p.Right);
        var shiftedTotal = shifted.Collect().Select(arr => arr.Sum());
        var expanded = context.Items.SelectMany(i => i % 3 == 0 ? new[] { i, -i } : []);
        var expandedCount = expanded.Collect().Select(arr => arr.Length);
        var everything = bucketSum.Combine(diamond).Select(p => p.Item1 * 1000 + p.Item2);
        var lengths = context.Items.Select(i => i.ToString()).Select(s => s.Length).Collect();

        static int Diamond(Model m) => (m.A % 3 * 100 + m.B) - (m.A + m.C);
        static int BucketSum(Model m) => m.Items.Select(i => i % 5).Where(b => b > 1).Sum();

        return
        [
            new("mod3TimesB", () => Read(mod3TimesB), m => m.A % 3 * 100 + m.B),
            new("aPlusC", () => Read(aPlusC), m => m.A + m.C),
            new("diamond", () => Read(diamond), m => Diamond(m)),
            new("evenA", () => Read(evenA), m => m.A % 2 == 0 ? m.A * 2 : (object)"none"),
            new("ratioPlusA", () => Read(ratioPlusA), m => m.B == 0 ? (object)"error" : 100 / m.B + m.A),
            new("buckets", () => ReadAll(buckets), m => string.Join(",", m.Items.Select(i => i % 5))),
            new("bucketSum", () => Read(bucketSum), m => BucketSum(m)),
            new("shiftedTotal", () => Read(shiftedTotal), m => m.Items.Sum(i => i + m.B)),
            new("expanded", () => ReadAll(expanded), m => string.Join(",", m.Items.Where(i => i % 3 == 0).SelectMany(i => new[] { i, -i }))),
            new("expandedCount", () => Read(expandedCount), m => m.Items.Count(i => i % 3 == 0) * 2),
            new("everything", () => Read(everything), m => BucketSum(m) * 1000 + Diamond(m)),
            new("lengths", () => Read(lengths) is ImmutableArray<int> arr ? string.Join(",", arr) : "?",
                m => string.Join(",", m.Items.Select(i => i.ToString().Length))),
        ];
    }

    private static void Mutate(OracleContext context, Model model, Random random)
    {
        switch (random.Next(7))
        {
            case 0:
                model.A = random.Next(10);
                context.ASource.Set(model.A);
                break;
            case 1:
                model.B = random.Next(4);
                context.BSource.Set(model.B);
                break;
            case 2:
                model.C = random.Next(10);
                context.CSource.Set(model.C);
                break;
            case 3:
                {
                    var item = random.Next(40);
                    if (!model.Items.Contains(item))
                        model.Items.Add(item);
                    context.ItemsSource.Push(item);
                    break;
                }
            case 4 when model.Items.Count > 0:
                {
                    var item = model.Items[random.Next(model.Items.Count)];
                    model.Items.Remove(item);
                    context.ItemsSource.Remove(item);
                    break;
                }
            case 5 when model.Items.Count > 0:
                {
                    var index = random.Next(model.Items.Count);
                    var replacement = random.Next(40);
                    if (model.Items.Contains(replacement))
                        break;
                    context.ItemsSource.Replace(model.Items[index], replacement);
                    model.Items[index] = replacement;
                    break;
                }
            default:
                {
                    // Several operations in one batch
                    using IBatchOperation<int> batch = context.ItemsSource.BeginBatch();
                    for (var i = 0; i < 3; i++)
                    {
                        var item = random.Next(40);
                        if (model.Items.Remove(item))
                        {
                            batch.Remove(item);
                        }
                        else
                        {
                            model.Items.Add(item);
                            batch.Push(item);
                        }
                    }

                    batch.Commit();
                    break;
                }
        }
    }

    private static Model CreateInitialized(OracleContext context)
    {
        var model = new Model { A = 1, B = 1, C = 1 };
        context.ASource.Set(model.A);
        context.BSource.Set(model.B);
        context.CSource.Set(model.C);
        return model;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomMutationsAndReads_MatchDirectComputation(int seed)
    {
        var random = new Random(seed);
        using var context = new OracleContext();
        var model = CreateInitialized(context);
        var checks = BuildPipelines(context);

        for (var step = 0; step < 400; step++)
        {
            var mutations = random.Next(3);
            for (var i = 0; i < mutations; i++)
                Mutate(context, model, random);

            // Read a random subset, so that some stages go unread across several changes
            foreach (var check in checks)
            {
                if (random.Next(3) == 0)
                    continue;

                Assert.True(
                    Equals(check.Expected(model), check.Actual()),
                    $"seed {seed}, step {step}, {check.Name}: expected {check.Expected(model)}, got {check.Actual()}");
            }
        }

        foreach (var check in checks)
            Assert.Equal(check.Expected(model), check.Actual());
    }

    private static Task Run(Action action) => Task.Run(action, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ConcurrentChangesAndReads_LeaveNoStaleValues()
    {
        for (var round = 0; round < 10; round++)
        {
            using var context = new OracleContext();
            var model = CreateInitialized(context);
            var checks = BuildPipelines(context);
            var values = new Model { A = model.A, B = model.B, C = model.C };

            using var stop = new CancellationTokenSource();
            var readers = Enumerable.Range(0, 3).Select(r => Task.Run(() =>
            {
                var random = new Random(round * 10 + r);
                while (!stop.IsCancellationRequested)
                {
                    // Reads racing with changes may see any mix of old and new inputs; they must
                    // not throw, and nothing stale may survive once the changes stop.
                    _ = checks[random.Next(checks.Count)].Actual();
                }
            })).ToArray();

            // One writer per source, so each source's final content is known
            var writers = new[]
            {
                Run(() =>
                {
                    var random = new Random(round);
                    for (var i = 0; i < 300; i++)
                    {
                        values.A = random.Next(10);
                        context.ASource.Set(values.A);
                    }
                }),
                Run(() =>
                {
                    var random = new Random(round + 100);
                    for (var i = 0; i < 300; i++)
                    {
                        values.B = random.Next(4);
                        context.BSource.Set(values.B);
                        values.C = random.Next(10);
                        context.CSource.Set(values.C);
                    }
                }),
                Run(() =>
                {
                    var random = new Random(round + 200);
                    var items = new Model();
                    for (var i = 0; i < 300; i++)
                    {
                        var item = random.Next(40);
                        if (items.Items.Remove(item))
                        {
                            context.ItemsSource.Remove(item);
                        }
                        else
                        {
                            items.Items.Add(item);
                            context.ItemsSource.Push(item);
                        }
                    }

                    lock (values.Items)
                        values.Items.AddRange(items.Items);
                }),
            };

            await Task.WhenAll(writers);
            await stop.CancelAsync();
            await Task.WhenAll(readers);

            foreach (var check in checks)
                Assert.True(Equals(check.Expected(values), check.Actual()), $"round {round}, {check.Name}");
        }
    }
}
