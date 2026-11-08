using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #104: a pass whose dictionary commit changes keys (<see cref="SharedGraphState.MarkKeysChanged"/>)
/// goes on recording stamps, accounting for its own key change (see
/// <see cref="GraphEvaluator.AccountForOwnKeyChange"/>). These tests check that it does, and, for each
/// rule that keeps this sound, a sequence of reads and changes that serves a stale value without it:
/// the key change's <c>Begin/EndSourceChange</c> guard, the epoch increment after an own key change,
/// the revision the pass takes after it, and the revision and the stamp of a node evaluation that
/// encloses it. Several of them use a graph whose sources are applied by hand, so that a source's
/// new value can be read before its change starts.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class LookupStampTests
{
    /// <summary>A declaration: <see cref="File"/> maps <see cref="Name"/> to <see cref="Value"/>.</summary>
    public sealed record Decl(int File, string Name, int Value);

    [ComputingContext]
    private partial class DeclContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Decl> Decls { get; }
        public partial IncrementalValuesProvider<Decl> MoreDecls { get; }
        public partial IncrementalValueProvider<int> Switch { get; }
        public partial IncrementalValueProvider<int> Other { get; }
    }

    private static IncrementalValueProvider<ImmutableDictionary<string, int>> MinTable(IncrementalValuesProvider<Decl> decls) =>
        decls.CollectToDictionary(d => d.Name, d => d.Value, Comparer<int>.Default);

    private static long Dirty(ComputationGraph graph, IComputationNode node) => graph.Versions.GetState(node.Id).Dirty;

    private static long Stamp(ComputationGraph graph, IComputationNode node) =>
        graph.ValueCache.TryGet(node.Id, out var cached) ? ((CachedNodeValue)cached!).Stamp : throw new InvalidOperationException("not cached");

    /// <summary>
    /// The pass that recomputes the table and changes a key stamps the table and what it verifies or
    /// computes after the key change: the lookup and reader of the changed key and a node downstream
    /// of both readers, which the key change marked before the pass computed them.
    /// </summary>
    [Fact]
    public void APassThatChangesAKey_StampsWhatItComputesAfterTheKeyChange()
    {
        using var context = new DeclContext();
        var graph = context.Graph;
        var table = MinTable(context.Decls);
        var lookupA = table.Lookup("a");
        var lookupB = table.Lookup("b");
        var a = lookupA.Select(v => v * 10);
        var b = lookupB.Select(v => v * 100);
        var both = a.Combine(b).Select(p => p.Item1 + p.Item2);
        context.DeclsSource.PushRange([new Decl(0, "a", 1), new Decl(1, "b", 2)]);
        Assert.Equal(210, both.Value);

        for (var i = 3; i < 8; i++)
        {
            var bDirty = Dirty(graph, b.Node);
            context.DeclsSource.Replace(new Decl(1, "b", i - 1), new Decl(1, "b", i));
            Assert.Equal(10 + i * 100, both.Value);

            // The key change ran in this read, and the read stamped everything it recomputed
            Assert.Equal(bDirty + 1, Dirty(graph, b.Node));
            foreach (var node in new IComputationNode[] { table.Node, lookupB.Node, b.Node, both.Node })
                Assert.Equal(Dirty(graph, node), Stamp(graph, node));
        }
    }

    /// <summary>
    /// The guard of a key change, observed by the pass that makes it. The key change of the reading
    /// pass is paused after it has marked k's lookup, and another thread changes m's declaration
    /// meanwhile. That source change reads the same stamp epoch as the edit of k before it (no stamp
    /// decision in between), so it stops at the declarations, which that edit covered, and does not
    /// mark the table: only the reading pass's stamp decision stands between the table it computed
    /// before m's change and a stamp that would hide the change. The pass must see m's change as a
    /// change that is not its own (the key change moved the change state too), so it stores the
    /// table without a stamp, and m's reader sees the change.
    /// </summary>
    [Fact]
    public void ASourceChangeDuringTheReadsOwnKeyChange_KeepsTheReadFromStampingTheTable()
    {
        var token = TestContext.Current.CancellationToken;
        using var context = new DeclContext();
        var graph = context.Graph;
        var table = MinTable(context.Decls);
        var lookupK = table.Lookup("k");
        var k = lookupK.Select(v => v * 10);
        var m = table.Lookup("m").Select(v => v * 10);
        context.DeclsSource.PushRange([new Decl(0, "k", 1), new Decl(1, "m", 1)]);
        Assert.Equal(10, k.Value);
        Assert.Equal(10, m.Value);

        var lookupState = graph.Versions.GetState(lookupK.Node.Id);
        var paused = 0;
        long? tableDirtyDuring = null;
        graph.SharedState.OnNodeMarked = state =>
        {
            if (state != lookupState || Interlocked.Exchange(ref paused, 1) != 0)
                return;

            graph.SharedState.OnNodeMarked = null;
            var tableDirty = Dirty(graph, table.Node);
            Task.Run(() => context.DeclsSource.Replace(new Decl(1, "m", 1), new Decl(1, "m", 2)), token).Wait(token);
            tableDirtyDuring = Dirty(graph, table.Node) - tableDirty;
        };

        context.DeclsSource.Replace(new Decl(0, "k", 1), new Decl(0, "k", 2));
        Assert.Equal(20, k.Value);
        Assert.Equal(1, paused);
        Assert.Equal(0, tableDirtyDuring); // m's change stopped early, before the table

        Assert.Equal(20, m.Value);
        Assert.Equal(2, table.Value["m"]);
    }

    /// <summary>
    /// The epoch increment after the pass's own key change. x reads k of one table and j of
    /// another, whose gates are refreshed in that order. The first read of each round recomputes the
    /// first table (a stamp decision, which advances the epoch) and then the second, whose key change
    /// marks k's lookup and x and records that epoch on them. The read then stamps them; unless it
    /// advances the epoch again first, the next edit's source change reads the same epoch, and so does
    /// the key change of the next read (which makes no stamp decision before it), which then stops at
    /// k's key state without marking x: x's stamp would hide the second edit. Without the key change's
    /// guard, an implementation that does not account for its own key change fails the same way.
    /// </summary>
    [Fact]
    public void AReadThatChangedAKey_DoesNotLetTheNextKeyChangeStopEarly()
    {
        using var context = new DeclContext();
        var graph = context.Graph;
        var first = MinTable(context.MoreDecls);
        var second = MinTable(context.Decls);
        var x = first.Lookup("j").Combine(second.Lookup("k")).Select(p => p.Item1 * 1000 + p.Item2);
        context.MoreDeclsSource.Push(new Decl(0, "j", 1));
        context.DeclsSource.Push(new Decl(0, "k", 1));
        Assert.Equal(1001, x.Value);

        var marked = new List<string>();
        for (var i = 2; i < 6; i++)
        {
            // A key of the first table that nobody looks up, and k
            context.MoreDeclsSource.Push(new Decl(i, $"other{i}", i));
            context.DeclsSource.Replace(new Decl(0, "k", 1), new Decl(0, "k", i));
            Assert.Equal(1000 + i, x.Value);

            context.DeclsSource.Replace(new Decl(0, "k", i), new Decl(0, "k", 1));
            graph.SharedState.OnNodeMarked = state => marked.Add(state.Id);
            Assert.Equal(1001, x.Value);
            graph.SharedState.OnNodeMarked = null;

            // The second read's key change walked to x
            Assert.Contains(x.Node.Id, marked);
            marked.Clear();
        }
    }

    /// <summary>
    /// A complete source change during a pass after its own key change. The read of <c>top</c>
    /// recomputes the table (key k changes), then <c>w = k + Switch</c>, then <c>trigger</c>, whose lambda
    /// changes Switch, then <c>z = w * 10</c>, which the source change marked. The pass verified w before
    /// the source change, so it must not stamp z.
    /// </summary>
    [Fact]
    public void ASourceChangeAfterTheReadsOwnKeyChange_StopsItsStamping()
    {
        using var context = new DeclContext();
        var graph = context.Graph;
        var table = MinTable(context.Decls);
        Action? during = null;
        var w = table.Lookup("k").Combine(context.Switch).Select(p => p.Item1 + p.Item2);
        var trigger = context.Other.Select(v =>
        {
            var run = during;
            during = null;
            run?.Invoke();
            return v;
        });
        var z = w.Select(v => v * 10);
        var top = table.Lookup("j").Combine(w).Combine(trigger).Combine(z);
        context.SwitchSource.Set(0);
        context.OtherSource.Set(0);
        context.DeclsSource.PushRange([new Decl(0, "j", 1), new Decl(1, "k", 1)]);
        Assert.Equal((((1, 1), 0), 10), top.Value);

        context.DeclsSource.Replace(new Decl(1, "k", 1), new Decl(1, "k", 2));
        context.OtherSource.Set(1);
        during = () => context.SwitchSource.Set(100);
        _ = top.Value; // overlaps the source change: may see either value
        Assert.Null(during);

        Assert.Equal(CachedNodeValue.NoStamp, Stamp(graph, z.Node));
        Assert.Equal(1020, z.Value);
        Assert.Equal((((1, 102), 1), 1020), top.Value);
    }

    /// <summary>
    /// A graph with sources applied by hand, so that a source's value can change before its change
    /// starts: <c>r</c> feeds the dictionary node <c>d = { "k": r }</c>, and <c>q</c> a node whose lambda
    /// can run an action in the middle of a read (used to evict d from the cache).
    /// </summary>
    private sealed class ManualGraph
    {
        public ManualGraph()
        {
            R = new IncrementalValueSource<int>("R", () => Graph);
            Q = new IncrementalValueSource<int>("Q", () => Graph);
            Graph = Graph.AddNode(R.GetNode()).AddNode(Q.GetNode());
            D = R.AsProvider().Select(v => ImmutableDictionary<string, int>.Empty.Add("k", v));
            Trigger = Q.AsProvider().Select(v =>
            {
                var run = During;
                During = null;
                run?.Invoke();
                return v;
            });
            Set(R, 1);
            Set(Q, 0);
        }

        public ComputationGraph Graph { get; } = new();

        public IncrementalValueSource<int> R { get; }

        public IncrementalValueSource<int> Q { get; }

        public IncrementalValueProvider<ImmutableDictionary<string, int>> D { get; }

        public IncrementalValueProvider<int> Trigger { get; }

        /// <summary>Runs once, while the trigger node is computed.</summary>
        public Action? During;

        /// <summary>A complete change.</summary>
        public void Set(IncrementalValueSource<int> source, int value)
        {
            source.Set(value);
            Graph.SharedState.MarkSourceChanged(source.SourceId);
        }

        public void EvictD() => Graph.ValueCache.Remove(D.Node.Id);
    }

    /// <summary>
    /// The revision a pass takes after its own key change. r's value changes, but its change has not
    /// started. The read of <c>top</c> serves <c>y = d["k"] * 10</c> by its stamp (marking it verified at
    /// the pass's revision), then the trigger evicts d, and z's gate recomputes d from r's new value:
    /// a key change of the pass's own, which marks y and z. Had the pass kept its revision, it would
    /// accept y as verified and stamp z from y's old value; once r's change has ended, d recomputes
    /// to the same dictionary, so no key change would reach z again.
    /// </summary>
    [Fact]
    public void AfterItsOwnKeyChange_APassDoesNotAcceptValuesVerifiedBefore()
    {
        var m = new ManualGraph();
        var y = m.D.Lookup("k").Select(v => v * 10);
        var z = y.Select(v => v + 1);
        var top = y.Combine(m.Trigger).Combine(z);
        Assert.Equal(((10, 0), 11), top.Value);

        m.R.Set(2); // r's change has not started
        m.Set(m.Q, 1);
        m.During = m.EvictD;
        _ = top.Value; // overlaps r's change: may see either value
        Assert.Null(m.During);

        m.Graph.SharedState.MarkSourceChanged("R");
        Assert.Equal(21, z.Value);
        Assert.Equal(((20, 1), 21), top.Value);
    }

    /// <summary>
    /// The revision at which a computation that encloses the pass's own key change commits its
    /// value. As above, but the key change runs while <c>zc = ((y, trigger), v)</c> is being computed:
    /// zc reads <c>yt = (y, trigger)</c>, which the pass computed from y's old value before the trigger
    /// evicted d, and then <c>v = d["none"]</c>, whose gate recomputes d. zc's value is marked verified at
    /// the revision its evaluation began with, so <c>z2</c>, verified after the key change, does not
    /// accept it, nor z (which encloses the key change too), and gets the new value. (v reads a key, so
    /// that r's change does not reach z2 through d.)
    /// </summary>
    [Fact]
    public void AComputationThatEnclosesAnOwnKeyChange_MarksItsValueAtItsStartRevision()
    {
        var m = new ManualGraph();
        var y = m.D.Lookup("k").Select(v => v * 10);
        var yt = y.Combine(m.Trigger);
        var v = m.D.LookupOrDefault("none");
        var z = yt.Combine(v).Select(p => p.Item1.Item1 + p.Item1.Item2 + p.Item2);
        var z2 = z.Select(x => x + 1000);
        var top = z.Combine(z2);
        Assert.Equal((10, 1010), top.Value);

        m.R.Set(2); // r's change has not started
        m.Set(m.Q, 1);
        m.During = m.EvictD;
        _ = top.Value; // overlaps r's change: may see either value
        Assert.Null(m.During);

        m.Graph.SharedState.MarkSourceChanged("R");
        Assert.Equal(1021, z2.Value);
        Assert.Equal((21, 1021), top.Value);
    }

    /// <summary>
    /// The same for a verification that encloses the pass's own key change: <c>x = (y', v)</c>, where
    /// <c>y' = (y, trigger).Item1</c> recomputes to y's old value (so its version stays) before v's gate
    /// recomputes d. x is then found up to date, and is marked verified at the revision its evaluation
    /// began with, so <c>x2</c>, verified after the key change, does not accept it.
    /// </summary>
    [Fact]
    public void AVerificationThatEnclosesAnOwnKeyChange_MarksTheValueAtItsStartRevision()
    {
        var m = new ManualGraph();
        var y = m.D.Lookup("k").Select(v => v * 10);
        var yOnly = y.Combine(m.Trigger).Select(p => p.Item1);
        var v = m.D.LookupOrDefault("none");
        var x = yOnly.Combine(v);
        var x2 = x.Select(p => p.Item1 + p.Item2 + 1000);
        var top = x.Combine(x2);
        Assert.Equal(((10, 0), 1010), top.Value);

        m.R.Set(2); // r's change has not started
        m.Set(m.Q, 1);
        m.During = m.EvictD;
        _ = top.Value; // overlaps r's change: may see either value
        Assert.Null(m.During);

        m.Graph.SharedState.MarkSourceChanged("R");
        Assert.Equal(1020, x2.Value);
        Assert.Equal(((20, 0), 1020), top.Value);
    }

    /// <summary>
    /// The stamp of a verification that encloses the pass's own key change. <c>x = (y, e)</c>, where
    /// <c>e</c> (a node of its own) reads q, evicts d and then reads a key of d, recomputing it: the key
    /// change runs inside e's computation, before the pass's first stamp decision, so it reads the
    /// same epoch as q's change, which marked e and x and covered them. The key change marks y but
    /// stops at x. x is then found up to date (e's value stays, and y was served by its stamp before
    /// the key change), with a dirty counter read before the key change that the key change did not
    /// move: x must not be stamped, or its stamp would hide k's new value.
    /// </summary>
    [Fact]
    public void AVerificationThatEnclosesAnOwnKeyChange_RecordsNoStamp()
    {
        var m = new ManualGraph();
        var y = m.D.Lookup("k").Select(v => v * 10);
        var evicting = new EvictingNode(m);
        var e = new IncrementalValueProvider<int>(evicting, m.D.Graph, default);
        var x = y.Combine(e);
        var x2 = x.Select(p => p.Item1 + p.Item2);
        Assert.Equal(17, x2.Value);

        m.R.Set(2); // r's change has not started
        m.Set(m.Q, 1);
        evicting.Evict = true;
        _ = x.Value; // overlaps r's change: may see either value
        Assert.False(evicting.Evict);

        m.Graph.SharedState.MarkSourceChanged("R");
        Assert.Equal((20, 7), x.Value);
        Assert.Equal(27, x2.Value);
    }

    /// <summary>
    /// The stamp of a computation that encloses the pass's own key change. <c>x = (yt, e)</c> with
    /// <c>yt = (y, trigger)</c>: after q's change (which marks and covers yt and x), a read that may not
    /// stamp (it overlaps a change) recomputes yt, so the next read finds x out of date at its first
    /// dependency without a stamp decision, and computes x: it reads yt, then e, whose computation
    /// evicts d and recomputes it, a key change of the pass's own that reads the same epoch as q's
    /// change and stops at yt. x's value holds y's old value, and its dirty counter, read before the
    /// key change, still matches: x must not be stamped.
    /// </summary>
    [Fact]
    public void AComputationThatEnclosesAnOwnKeyChange_RecordsNoStamp()
    {
        var m = new ManualGraph();
        var state = m.Graph.SharedState;
        var y = m.D.Lookup("k").Select(v => v * 10);
        var yt = y.Combine(m.Trigger);
        var evicting = new EvictingNode(m);
        var x = yt.Combine(new IncrementalValueProvider<int>(evicting, m.D.Graph, default));
        Assert.Equal(((10, 0), 7), x.Value);

        m.R.Set(2); // r's change has not started
        m.Set(m.Q, 1);
        state.BeginSourceChange();
        Assert.Equal((10, 1), yt.Value); // overlaps a change: no stamp decision
        state.EndSourceChange();

        evicting.Evict = true;
        _ = x.Value; // overlaps r's change: may see either value
        Assert.False(evicting.Evict);

        state.MarkSourceChanged("R");
        Assert.Equal(((20, 1), 7), x.Value);
    }

    /// <summary>
    /// How a pass's own key change combines with the change tracking of #99. The key change alone
    /// keeps the pass stamping and does not make it overlap a change (it keeps taking values by their
    /// revision, now the key change's). Any other change since the pass started stops its stamping,
    /// also one that changed nothing (a source opens a change before every mutation); and a change in
    /// progress when the pass's key change ends makes the pass overlap a change, as a pass starting
    /// then would.
    /// </summary>
    [Fact]
    public void AnOwnKeyChange_CountsOnlyItself_AndOverlapsAChangeInProgressWhenItEnds()
    {
        var m = new ManualGraph();
        var state = m.Graph.SharedState;

        var pass = new GraphEvaluator(m.Graph);
        var revision = state.MarkKeysChanged([]);
        pass.AccountForOwnKeyChange(revision);
        Assert.Equal(revision, pass.Revision);
        Assert.False(pass.OverlapsChange);
        Assert.True(pass.CanStamp(pass.OwnKeyChanges));

        // A mutation that changes nothing still opens and closes a change
        m.Q.Set(0);
        Assert.False(pass.CanStamp(pass.OwnKeyChanges));

        var overlapping = new GraphEvaluator(m.Graph);
        state.BeginSourceChange();
        try
        {
            overlapping.AccountForOwnKeyChange(state.MarkKeysChanged([]));
            Assert.True(overlapping.OverlapsChange);
            Assert.False(overlapping.CanStamp(overlapping.OwnKeyChanges));
        }
        finally
        {
            state.EndSourceChange();
        }
    }

    /// <summary>
    /// Reads q, then (once, when <see cref="Evict"/> is set) evicts d and reads d's key "none"; 7.
    /// </summary>
    private sealed class EvictingNode(ManualGraph m) : IComputationNode<int>
    {
        public bool Evict;

        public string Id { get; } = "Evicting";

        public Type OutputType => typeof(int);

        public ImmutableHashSet<string> Dependencies { get; } = [m.Q.SourceId, m.D.Node.Id];

        public Result<int, ComputationError> Compute(ComputationContext context)
        {
            _ = context.Read(m.Q.GetNode());
            if (Evict)
            {
                Evict = false;
                m.EvictD();
            }

            _ = context.ReadKey(m.D.Node, "none");
            return Result<int, ComputationError>.Success(7);
        }
    }
}
