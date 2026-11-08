using System;
using System.Collections.Generic;
using System.Linq;
using Guacamole.Pipeline;
using Guacamole.Tests.Common;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #71: a change stops marking at nodes that an earlier change has already marked, together
/// with everything downstream of them, since a value could last have been stamped (see
/// <see cref="SharedGraphState.MarkSourceChanged"/>). These tests check that several changes between
/// two reads mark the downstream nodes once, and, step by step, the interleavings of a change that
/// stops early with a read that verifies and stamps values.
/// </summary>
public partial class DirtyMarkingTests
{
    [Fact]
    public void ChangesBetweenReads_MarkTheDownstreamNodesOnce()
    {
        using var context = new OracleContext();
        context.ASource.Set(0);
        var stages = Enumerable.Range(0, 50).Select(i => context.A.Select(a => a + i)).ToArray();
        var total = stages[0].Combine(stages[49]).Select(pair => pair.Item1 + pair.Item2);
        Assert.Equal(49, total.Value);
        foreach (var stage in stages)
            _ = stage.Value;

        long Dirty<T>(IncrementalValueProvider<T> provider) => context.Graph.Versions.GetState(provider.Node.Id).Dirty;
        var before = stages.Select(Dirty).ToArray();
        var totalBefore = Dirty(total);

        for (var i = 1; i <= 10; i++)
            context.ASource.Set(i);

        // The first change marked them; the next nine stopped at the source
        Assert.Equal(before.Select(d => d + 1), stages.Select(Dirty));
        Assert.Equal(totalBefore + 1, Dirty(total));
        Assert.Equal(10 + 10 + 49, total.Value);

        // The read stamped values, so the next change marks everything again
        context.ASource.Set(20);
        Assert.Equal(before.Select(d => d + 2), stages.Select(Dirty));
        Assert.Equal(totalBefore + 2, Dirty(total));
        Assert.Equal(20 + 20 + 49, total.Value);
        Assert.Equal(Enumerable.Range(20, 50), stages.Select(stage => stage.Value));
    }

    [Fact]
    public void AChangeOfAnotherSource_StopsWhereTheFirstChangeMarkedEverythingDownstream()
    {
        using var context = new OracleContext();
        context.ASource.Set(1);
        context.BSource.Set(2);
        var fromA = context.A.Select(a => a * 10);
        var fromB = context.B.Select(b => b * 100);
        var both = fromA.Combine(fromB).Select(pair => pair.Item1 + pair.Item2);
        var after = both.Select(x => x + 1);
        Assert.Equal(211, after.Value);

        long Dirty<T>(IncrementalValueProvider<T> provider) => context.Graph.Versions.GetState(provider.Node.Id).Dirty;
        var (bothBefore, afterBefore, fromBBefore) = (Dirty(both), Dirty(after), Dirty(fromB));

        context.ASource.Set(3);
        context.BSource.Set(4);

        Assert.Equal(fromBBefore + 1, Dirty(fromB));
        Assert.Equal(bothBefore + 1, Dirty(both));
        Assert.Equal(afterBefore + 1, Dirty(after));
        Assert.Equal(431, after.Value);
    }

    /// <summary>
    /// A source whose changes the tests apply by hand (not subscribed to a context), and a chain
    /// <c>x = S + 1</c>, <c>y = x * 10</c> whose lambdas can run an action in the middle of a read.
    /// </summary>
    private sealed class ManualChain
    {
        public ManualChain()
        {
            Source = new IncrementalValueSource<int>("S", () => Graph);
            Graph = Graph.AddNode(Source.GetNode());
            X = Source.AsProvider().Select(v =>
            {
                Run(ref DuringX);
                return v + 1;
            });
            Y = X.Select(v =>
            {
                Run(ref DuringY);
                return v * 10;
            });
            Set(1);
            Assert.Equal(20, Y.Value);
        }

        public ComputationGraph Graph { get; } = new();

        public SharedGraphState State => Graph.SharedState;

        public IncrementalValueSource<int> Source { get; }

        public IncrementalValueProvider<int> X { get; }

        public IncrementalValueProvider<int> Y { get; }

        /// <summary>Runs once, while x is computed.</summary>
        public Action? DuringX;

        /// <summary>Runs once, while y is computed.</summary>
        public Action? DuringY;

        public NodeState SourceState => State.Versions.GetState("S");

        public NodeState XState => State.Versions.GetState(X.Node.Id);

        public NodeState YState => State.Versions.GetState(Y.Node.Id);

        /// <summary>A complete change.</summary>
        public void Set(int value)
        {
            Source.Set(value);
            State.MarkSourceChanged("S");
        }

        private static void Run(ref Action? action)
        {
            var run = action;
            action = null;
            run?.Invoke();
        }
    }

    /// <summary>
    /// A change arrives while a read is recomputing a node, before the read's first stamp decision.
    /// The change has the same epoch as the change before it, so it stops at the source without
    /// marking anything; the read must then record no stamp.
    /// </summary>
    [Fact]
    public void ChangeBeforeTheReadsFirstStampDecision_StopsEarly_AndTheReadStampsNothing()
    {
        var chain = new ManualChain();
        chain.Set(2); // marks x and y, and covers them
        var (x, y) = (chain.XState.Dirty, chain.YState.Dirty);

        // While x is computed from 2, the source changes to 3
        chain.DuringX = () => chain.Set(3);
        Assert.Equal(30, chain.Y.Value);

        Assert.Equal(x, chain.XState.Dirty); // the change stopped early
        Assert.Equal(y, chain.YState.Dirty);
        Assert.Equal(40, chain.Y.Value);
        Assert.Equal(4, chain.X.Value);
    }

    /// <summary>
    /// A change arrives after a read has stamped a node it re-verified (x), while it computes the
    /// next one (y). The read's stamp decision moved the epoch on, so the change walks again, past
    /// the node just stamped.
    /// </summary>
    [Fact]
    public void ChangeAfterTheReadStampedANode_MarksItAgain()
    {
        var chain = new ManualChain();
        chain.Set(2);
        var (x, y) = (chain.XState.Dirty, chain.YState.Dirty);

        chain.DuringY = () => chain.Set(3);
        Assert.Equal(30, chain.Y.Value);

        Assert.Equal(x + 1, chain.XState.Dirty);
        Assert.Equal(y + 1, chain.YState.Dirty);
        Assert.Equal(40, chain.Y.Value);
    }

    /// <summary>
    /// A change that has marked some nodes but has not finished its walk records no epoch, so a
    /// change that starts meanwhile cannot stop at the nodes it marked, and a read after that change
    /// ended sees it, although the first change is still in progress.
    /// </summary>
    [Fact]
    public void ChangeDuringAnUnfinishedChange_DoesNotStopAtItsNodes()
    {
        var chain = new ManualChain();
        chain.Set(2);
        Assert.Equal(30, chain.Y.Value);

        // A change in progress: it has marked the source and x, but not y yet
        chain.Source.Set(5);
        chain.State.BeginSourceChange();
        chain.SourceState.IncrementVersion();
        chain.SourceState.MarkDirty();
        chain.XState.MarkDirty();
        var y = chain.YState.Dirty;

        // A complete change, meanwhile
        chain.Set(6);
        Assert.Equal(y + 1, chain.YState.Dirty);
        Assert.Equal(70, chain.Y.Value);

        chain.YState.MarkDirty();
        chain.State.AdvanceRevision();
        chain.State.EndSourceChange();
        Assert.Equal(70, chain.Y.Value);

        chain.Set(7);
        Assert.Equal(80, chain.Y.Value);
    }

    /// <summary>
    /// The same with a real change, paused after it has marked x and before it reaches y: a second
    /// change and a read run (on other threads) at that point. The first change has recorded no epoch
    /// on the source yet (it records one on a node only once everything downstream is marked), so the
    /// second change walks to y itself, and the read, which starts after the second change ended,
    /// returns its value although the first change is still in progress.
    /// </summary>
    [Fact]
    public void ChangeDuringAPausedChange_WalksPastTheNodesItHasMarked()
    {
        var chain = new ManualChain();
        chain.Set(2);
        Assert.Equal(30, chain.Y.Value); // stamps, so the next change walks
        int? readDuringFirstChange = null;

        chain.State.OnNodeMarked = state =>
        {
            if (state != chain.XState)
                return;

            chain.State.OnNodeMarked = null;
            var token = TestContext.Current.CancellationToken;
            System.Threading.Tasks.Task.Run(() => chain.Set(5), token).Wait(token);
            readDuringFirstChange = System.Threading.Tasks.Task.Run(() => chain.Y.Value, token).Result;
        };

        // The first change; the second has the same epoch (no read in between)
        chain.Source.Set(4);
        chain.State.MarkSourceChanged("S");

        Assert.Equal(60, readDuringFirstChange);
        Assert.Equal(60, chain.Y.Value);
    }

    /// <summary>
    /// A read whose snapshot of the change state and of the revision straddles the end of a change:
    /// the change ends between the two reads. A value verified before that change incremented its
    /// source's version must not count as verified for the read, or the read could stamp a value
    /// derived from it. (Found while proving #71; the snapshot used to read the revision first.)
    /// </summary>
    [Fact]
    public void ReadStartingAsAChangeEnds_DoesNotStampValuesVerifiedBeforeTheChange()
    {
        var chain = new ManualChain();
        chain.Set(2);
        Assert.Equal(30, chain.Y.Value);

        // A change starts; meanwhile another change finishes and advances the revision
        chain.Source.Set(3);
        chain.State.BeginSourceChange();
        chain.State.AdvanceRevision();

        // A read before the change reaches x: x is still clean, and is verified at this revision
        Assert.Equal(3, chain.X.Value);

        // The change increments the version and marks everything, then ends while a read of y is
        // taking its snapshot
        chain.SourceState.IncrementVersion();
        chain.SourceState.MarkDirty();
        chain.XState.MarkDirty();
        chain.YState.MarkDirty();
        chain.State.OnEvaluationSnapshot = () =>
        {
            chain.State.OnEvaluationSnapshot = null;
            chain.State.AdvanceRevision();
            chain.State.EndSourceChange();
        };
        _ = chain.Y.Value; // overlaps the change: may see either value

        Assert.Equal(40, chain.Y.Value);
        Assert.Equal(4, chain.X.Value);
    }

    /// <summary>
    /// Many changes to one source with a large fan-out between two reads cost one walk over the
    /// fan-out plus a constant per change: with as many changes as stages, the time per change is
    /// about the same at 64 and 2,048 stages (it was proportional to the fan-out, about 32x).
    /// </summary>
    [Fact]
    public void ManyChangesToASourceWithALargeFanOut_CostAConstantPerChange()
    {
        var contexts = new List<OracleContext>();
        try
        {
            ScalingAssert.PerOperationTimeScales(
                fanOut =>
                {
                    var context = new OracleContext();
                    contexts.Add(context);
                    context.ASource.Set(0);
                    var stages = Enumerable.Range(0, fanOut).Select(i => context.A.Select(a => a + i)).ToArray();
                    foreach (var stage in stages)
                        _ = stage.Value;

                    var next = 0;
                    return (() =>
                    {
                        for (var i = 0; i < fanOut; i++)
                            context.ASource.Set(++next);

                        Assert.Equal(next, stages[0].Value);
                    }, fanOut);
                },
                smallSize: 64,
                largeSize: 2_048,
                maxRatio: 8,
                "Source change with many dependents, repeated before a read");
        }
        finally
        {
            foreach (var context in contexts)
                context.Dispose();
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void ManyMutationsBetweenReads_MatchDirectComputation(int seed)
    {
        var random = new Random(seed);
        using var context = new OracleContext();
        var model = CreateInitialized(context);
        var checks = BuildPipelines(context);

        for (var step = 0; step < 300; step++)
        {
            // Bursts of changes, so that later changes of a burst stop early
            var mutations = random.Next(10);
            for (var i = 0; i < mutations; i++)
                Mutate(context, model, random);

            // Read one check at a time, so that only part of the graph is stamped between bursts
            var check = checks[random.Next(checks.Count)];
            Assert.True(
                Equals(check.Expected(model), check.Actual()),
                $"seed {seed}, step {step}, {check.Name}: expected {check.Expected(model)}, got {check.Actual()}");
        }

        foreach (var check in checks)
            Assert.Equal(check.Expected(model), check.Actual());
    }
}
