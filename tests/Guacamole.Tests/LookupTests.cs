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
/// Issue #95: <c>Lookup</c> / <c>LookupOrDefault</c> read one key of a dictionary, and only a change
/// of that key's entry reaches them. A source change upstream of the dictionary marks the dictionary
/// node and stops there; the dictionary's commit marks the readers of the keys whose entries changed
/// (see <see cref="KeyIndex{TKey, TValue}"/> and <see cref="SharedGraphState.MarkKeysChanged"/>).
/// These tests cover the semantics, that an edit reruns and marks only the readers of the changed
/// keys, an oracle against direct lookups, the gates of nodes downstream of lookups, and step by step
/// interleavings of key changes with reads and source changes.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class LookupTests
{
    /// <summary>A declaration: <see cref="File"/> maps <see cref="Name"/> to <see cref="Value"/>.</summary>
    public sealed record Decl(int File, string Name, int Value);

    [ComputingContext]
    private partial class DeclContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Decl> Decls { get; }
        public partial IncrementalValueProvider<int> Switch { get; }
    }

    [ComputingContext]
    private partial class SmallCacheContext : ComputingContextBase
    {
        protected override int CacheCapacity => 16;

        public partial IncrementalValuesProvider<Decl> Decls { get; }
    }

    /// <summary>The smallest value wins when several declarations name the same key.</summary>
    private static IncrementalValueProvider<ImmutableDictionary<string, int>> MinTable(IncrementalValuesProvider<Decl> decls) =>
        decls.CollectToDictionary(d => d.Name, d => d.Value, Comparer<int>.Default);

    private static Dictionary<string, int> Fold(IEnumerable<Decl> decls) =>
        decls.GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.Min(d => d.Value));

    private static long Dirty<T>(DeclContext context, IncrementalValueProvider<T> provider) =>
        context.Graph.Versions.GetState(provider.Node.Id).Dirty;

    private static int Computations<TValue>(IncrementalValueProvider<TValue> lookup) =>
        ((LookupNode<string, TValue>)lookup.Node).Computations;

    [Fact]
    public void Lookup_ReturnsTheValue_OrNoValueWhileTheKeyIsAbsent()
    {
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var a = table.Lookup("a");
        var b = table.Lookup("b");
        var bOrDefault = table.LookupOrDefault("b");

        context.DeclsSource.Push(new Decl(0, "a", 5));
        Assert.Equal(5, a.Value);
        Assert.True(b.GetValue().IsNone);
        Assert.False(b.TryGetValue(out _));
        Assert.Equal(0, bOrDefault.Value);

        context.DeclsSource.Push(new Decl(1, "b", 7));
        Assert.Equal(7, b.Value);
        Assert.Equal(7, bOrDefault.Value);

        // A smaller value for a wins; removing it restores the other
        context.DeclsSource.Push(new Decl(2, "a", 3));
        Assert.Equal(3, a.Value);
        context.DeclsSource.Remove(new Decl(2, "a", 3));
        Assert.Equal(5, a.Value);
        context.DeclsSource.Remove(new Decl(0, "a", 5));
        Assert.True(a.GetValue().IsNone);
    }

    [Fact]
    public void LookupOrDefault_ComposesWithCombine_WhereLookupHasNoValue()
    {
        using var context = new DeclContext();
        var table = context.Decls.CollectToDictionary(d => d.Name, d => d.Name + d.Value, StringComparer.Ordinal);
        context.DeclsSource.Push(new Decl(0, "a", 1));

        var strict = table.Lookup("a").Combine(table.Lookup("b"));
        var lenient = table.LookupOrDefault("a").Combine(table.LookupOrDefault("b"));
        Assert.True(strict.GetValue().IsNone);
        Assert.Equal(("a1", (string?)null), lenient.Value);

        context.DeclsSource.Push(new Decl(1, "b", 2));
        Assert.Equal(("a1", "b2"), strict.Value);
        Assert.Equal(("a1", "b2"), lenient.Value);
    }

    [Fact]
    public void Lookup_UsesTheDictionarysKeyComparer()
    {
        using var context = new DeclContext();
        var table = context.Decls.CollectToDictionary(d => d.Name, d => d.Value, Comparer<int>.Default, StringComparer.OrdinalIgnoreCase);
        var lower = table.Lookup("name");
        var upper = table.Lookup("NAME");
        context.DeclsSource.Push(new Decl(0, "Name", 1));
        Assert.Equal(1, lower.Value);
        Assert.Equal(1, upper.Value);

        context.DeclsSource.Replace(new Decl(0, "Name", 1), new Decl(0, "nAmE", 2));
        Assert.Equal(2, lower.Value);
        Assert.Equal(2, upper.Value);
    }

    [Fact]
    public void Lookup_PropagatesTheDictionarysError_AndRecovers()
    {
        using var context = new DeclContext();

        // No conflict comparer: two values for one key make the dictionary an error
        var table = context.Decls.CollectToDictionary(d => d.Name, d => d.Value);
        var a = table.Lookup("a");
        var b = table.LookupOrDefault("b");
        var aPlusOne = a.Select(v => v + 1);
        context.DeclsSource.Push(new Decl(0, "a", 1));
        Assert.Equal(2, aPlusOne.Value);
        Assert.Equal(0, b.Value);

        context.DeclsSource.Push(new Decl(1, "b", 2));
        context.DeclsSource.Push(new Decl(2, "b", 3));
        Assert.True(aPlusOne.GetValue().IsError);
        Assert.Contains("key(s) map to different values", a.GetValue().ErrorValue!.Message);
        Assert.True(b.GetValue().IsError);

        context.DeclsSource.Remove(new Decl(2, "b", 3));
        Assert.Equal(2, aPlusOne.Value);
        Assert.Equal(2, b.Value);
    }

    [Fact]
    public void Lookup_RejectsANullKey_AndReusesTheNodeOfAnIdenticalCall()
    {
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        Assert.Throws<ArgumentNullException>(() => table.Lookup(null!));
        Assert.Equal(table.Lookup("a").Node, table.Lookup("a").Node);
        Assert.NotEqual(table.Lookup("a").Node, table.Lookup("b").Node);
        Assert.NotEqual(table.Lookup("a").Node, table.LookupOrDefault("a").Node);
    }

    [Fact]
    public void Lookup_IntoAnyDictionaryProvider_ComparesTheLookedUpKeys()
    {
        // A dictionary that CollectToDictionary did not produce: the key index compares keys itself
        using var context = new DeclContext();
        var table = context.Decls.Collect().Select(decls => decls.GroupBy(d => d.Name).ToImmutableDictionary(g => g.Key, g => g.Min(d => d.Value)));
        var readers = Enumerable.Range(0, 20).Select(i => table.Lookup($"k{i}").Select(v => v * 2)).ToArray();
        for (var i = 0; i < 20; i++)
            context.DeclsSource.Push(new Decl(i, $"k{i}", i));

        Assert.Equal(Enumerable.Range(0, 20).Select(i => i * 2), readers.Select(r => r.Value));
        var lookups = Enumerable.Range(0, 20).Select(i => table.Lookup($"k{i}")).ToArray();
        var before = lookups.Select(Computations).ToArray();

        context.DeclsSource.Replace(new Decl(3, "k3", 3), new Decl(3, "k3", 30));
        Assert.Equal(Enumerable.Range(0, 20).Select(i => i == 3 ? 60 : i * 2), readers.Select(r => r.Value));
        Assert.Equal(before.Select((c, i) => i == 3 ? c + 1 : c), lookups.Select(Computations));
    }

    [Fact]
    public void Lookup_IntoADictionarySource_DependsOnTheWholeDictionary()
    {
        // A source of dictionaries (applied by hand, like DirtyMarkingTests.ManualChain)
        var graph = new ComputationGraph();
        var source = new IncrementalValueSource<ImmutableDictionary<string, int>>("T", () => graph);
        graph = graph.AddNode(source.GetNode());
        var a = source.AsProvider().Lookup("a");

        void Set(ImmutableDictionary<string, int> value)
        {
            source.Set(value);
            graph.SharedState.MarkSourceChanged("T");
        }

        Set(ImmutableDictionary<string, int>.Empty.Add("a", 1));
        Assert.Equal(1, a.Value);
        Set(ImmutableDictionary<string, int>.Empty.Add("a", 2).Add("b", 3));
        Assert.Equal(2, a.Value);
        Set(ImmutableDictionary<string, int>.Empty);
        Assert.True(a.GetValue().IsNone);
    }

    /// <summary>
    /// The fan-out of the issue: N readers, each looking up its own key. An edit that changes one
    /// key reruns only that key's lookup and reader, and marks only the nodes downstream of that key
    /// (plus the table's own pipeline), whether every reader or only the affected one is read.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEditReachesOnlyTheReadersOfTheChangedKey(bool readAll)
    {
        const int n = 200;
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var calls = new int[n];
        var lookups = Enumerable.Range(0, n).Select(i => table.Lookup($"k{i}")).ToArray();
        var readers = Enumerable.Range(0, n).Select(i => lookups[i].Select(v =>
        {
            Interlocked.Increment(ref calls[i]);
            return v + 1;
        })).ToArray();
        context.DeclsSource.PushRange(Enumerable.Range(0, n).Select(i => new Decl(i, $"k{i}", i)));
        Assert.Equal(Enumerable.Range(1, n), readers.Select(r => r.Value));

        var marked = new HashSet<string>();
        context.Graph.SharedState.OnNodeMarked = state =>
        {
            lock (marked)
                marked.Add(state.Id);
        };

        var values = Enumerable.Range(0, n).ToArray();
        for (var edit = 0; edit < 20; edit++)
        {
            var k = (edit * 37) % n;
            var callsBefore = calls.ToArray();
            var computationsBefore = lookups.Select(Computations).ToArray();
            var dirtyBefore = readers.Select(r => Dirty(context, r)).ToArray();
            lock (marked)
                marked.Clear();

            var old = new Decl(k, $"k{k}", values[k]);
            values[k] += 1000;
            context.DeclsSource.Replace(old, old with { Value = values[k] });

            if (readAll)
                Assert.Equal(values.Select(v => v + 1), readers.Select(r => r.Value));
            else
                Assert.Equal(values[k] + 1, readers[k].Value);

            Assert.Equal(callsBefore.Select((c, i) => i == k ? c + 1 : c), calls);
            Assert.Equal(computationsBefore.Select((c, i) => i == k ? c + 1 : c), lookups.Select(Computations));
            Assert.Equal(dirtyBefore.Select((d, i) => i == k ? d + 1 : d), readers.Select(r => Dirty(context, r)));

            // The source change marks the table and stops; the table's commit marks k's lookup and reader
            lock (marked)
                Assert.Equal(new[] { table.Node.Id, lookups[k].Node.Id, readers[k].Node.Id }.Order(), marked.Order());
        }

        context.Graph.SharedState.OnNodeMarked = null;
    }

    /// <summary>
    /// The window between a source change and the dictionary's recomputation: a reader downstream
    /// of a lookup (several stages down) is read first, before anything else, so the read itself
    /// must bring the dictionary up to date (its gate) before it trusts the reader's stamp.
    /// </summary>
    [Fact]
    public void AReaderReadFirstAfterAnEdit_SeesTheChange()
    {
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var deep = table.Lookup("a").Select(v => v + 1).Select(v => v * 10).Combine(context.Switch).Select(p => p.Item1 + p.Item2);
        context.SwitchSource.Set(0);
        context.DeclsSource.Push(new Decl(0, "a", 1));
        Assert.Equal(20, deep.Value);

        for (var i = 2; i < 20; i++)
        {
            context.DeclsSource.Replace(new Decl(0, "a", i - 1), new Decl(0, "a", i));
            Assert.Equal((i + 1) * 10 + i - 2, deep.Value);

            // A pass that changes a key records no stamps; one that changes only another input of
            // the reader re-verifies it and stamps it, so the next edit finds a matching stamp
            context.SwitchSource.Set(i - 1);
            Assert.Equal((i + 1) * 10 + i - 1, deep.Value);
            Assert.True(context.Graph.ValueCache.TryGet(deep.Node.Id, out var cached));
            Assert.Equal(Dirty(context, deep), ((CachedNodeValue)cached!).Stamp);
        }
    }

    /// <summary>
    /// Gates reach nodes that already depend on a node when that node starts reading a lookup:
    /// <c>x = flag.Combine(lookup)</c> does not read the lookup while <c>flag</c> is an error, so x and
    /// its reader y first have no gate. When x starts reading the lookup, y must get x's gate, or a
    /// read of y after an edit would trust y's stamp without bringing the table up to date.
    /// </summary>
    [Fact]
    public void ANodeThatStartsReadingALookup_PassesItsGateToItsReaders()
    {
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var flag = context.Switch.Select(v => v >= 0 ? v : throw new InvalidOperationException("negative"));
        var x = flag.Combine(table.Lookup("a"));
        var y = x.Select(p => p.Item1 + p.Item2);
        context.DeclsSource.Push(new Decl(0, "a", 1));
        context.SwitchSource.Set(-1);
        Assert.True(y.GetValue().IsError);
        Assert.Empty(Handle(y).Gates);

        // The table is up to date, so the next pass changes no key and stamps x and y
        Assert.Single(table.Value);
        context.SwitchSource.Set(10);
        Assert.Equal(11, y.Value);
        Assert.Equal([Handle(table)], Handle(y).Gates);

        for (var i = 2; i < 6; i++)
        {
            context.DeclsSource.Replace(new Decl(0, "a", i - 1), new Decl(0, "a", i));
            Assert.Equal(10 + i, y.Value);

            // Re-stamp y in a pass that changes no key
            context.SwitchSource.Set(-1);
            Assert.True(y.GetValue().IsError);
            context.SwitchSource.Set(10);
            Assert.Equal(10 + i, y.Value);
        }

        NodeHandle Handle<T>(IncrementalValueProvider<T> provider) =>
            context.Graph.SharedState.Handles.TryGetValue(provider.Node, out var handle) ? handle : throw new InvalidOperationException("no handle");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void RandomEdits_ReadsOfRandomLookups_MatchDirectLookups(int seed, bool smallCache)
    {
        var random = new Random(seed);
        using var big = smallCache ? null : new DeclContext();
        using var small = smallCache ? new SmallCacheContext() : null;
        var decls = big?.Decls ?? small!.Decls;
        var source = big?.DeclsSource ?? small!.DeclsSource;

        const int keys = 30;
        string Key(int i) => $"k{i}";
        var model = new List<Decl>();

        // The table with a conflict comparer, one without (conflicts are errors), and a dictionary
        // that CollectToDictionary did not produce (keys compared one by one)
        var minTable = MinTable(decls);
        var strictTable = decls.CollectToDictionary(d => d.Name, d => d.Value);
        var otherTable = decls.Collect().Select(all => all.Where(d => d.Value % 3 != 0).GroupBy(d => d.Name).ToImmutableDictionary(g => g.Key, g => g.Max(d => d.Value)));

        var checks = new List<(string Name, Func<object?> Actual, Func<object?> Expected)>();
        object? Describe<T>(Result<T, ComputationError> result) =>
            result.IsError ? "error" : result.IsNone ? "none" : result.ValueOrDefault;

        for (var i = 0; i < keys; i++)
        {
            var key = Key(i);
            var next = Key((i + 1) % keys);
            var lookup = minTable.Lookup(key);
            var plus = lookup.Select(v => v + 1).Select(v => v * 2);
            var orDefault = minTable.LookupOrDefault(key);
            var pair = orDefault.Combine(minTable.LookupOrDefault(next)).Select(p => p.Item1 - p.Item2);
            var strict = strictTable.Lookup(key);
            var other = otherTable.LookupOrDefault(key);

            int? Min(string k) => model.Where(d => d.Name == k).Select(d => (int?)d.Value).Min();
            checks.Add(($"Lookup({key})", () => Describe(lookup.GetValue()), () => Min(key) is { } v ? v : "none"));
            checks.Add(($"Lookup({key}).Select.Select", () => Describe(plus.GetValue()), () => Min(key) is { } v ? (v + 1) * 2 : "none"));
            checks.Add(($"LookupOrDefault pair {key}", () => Describe(pair.GetValue()), () => (Min(key) ?? 0) - (Min(next) ?? 0)));
            checks.Add(($"strict Lookup({key})", () => Describe(strict.GetValue()), () =>
                model.GroupBy(d => d.Name).Any(g => g.Select(d => d.Value).Distinct().Count() > 1) ? "error"
                : Min(key) is { } v ? v : "none"));
            checks.Add(($"other LookupOrDefault({key})", () => Describe(other.GetValue()), () =>
                model.Where(d => d.Name == key && d.Value % 3 != 0).Select(d => (int?)d.Value).Max() ?? 0));
        }

        var nextFile = 0;
        for (var step = 0; step < 400; step++)
        {
            var edits = random.Next(1, 4);
            for (var e = 0; e < edits; e++)
            {
                var op = model.Count == 0 ? 0 : random.Next(3);
                if (op == 0)
                {
                    var decl = new Decl(nextFile++, Key(random.Next(keys + 5)), random.Next(20));
                    model.Add(decl);
                    source.Push(decl);
                }
                else if (op == 1)
                {
                    var old = model[random.Next(model.Count)];
                    model.Remove(old);
                    source.Remove(old);
                }
                else
                {
                    var old = model[random.Next(model.Count)];
                    var replacement = random.Next(2) == 0 ? old with { Value = random.Next(20) } : old with { Name = Key(random.Next(keys + 5)) };
                    model[model.IndexOf(old)] = replacement;
                    source.Replace(old, replacement);
                }
            }

            // A random subset of the checks, in random order
            foreach (var check in checks.OrderBy(_ => random.Next()).Take(random.Next(1, 12)))
                Assert.True(Equals(check.Expected(), check.Actual()), $"seed {seed}, step {step}, {check.Name}: expected {check.Expected()}, got {check.Actual()}");
        }

        foreach (var check in checks)
            Assert.True(Equals(check.Expected(), check.Actual()), $"seed {seed}, final, {check.Name}: expected {check.Expected()}, got {check.Actual()}");
    }

    [Fact]
    public async Task ConcurrentEditsAndReads_EndInAgreementWithDirectLookups()
    {
        const int keys = 40;
        var token = TestContext.Current.CancellationToken;
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var lookups = Enumerable.Range(0, keys).Select(i => table.LookupOrDefault($"k{i}").Select(v => v * 2)).ToArray();
        var model = Enumerable.Range(0, keys).Select(i => new Decl(i, $"k{i}", i)).ToArray();
        context.DeclsSource.PushRange(model);

        for (var round = 0; round < 6; round++)
        {
            using var stop = new CancellationTokenSource();
            var readers = Enumerable.Range(0, 3).Select(r => Task.Run(() =>
            {
                var random = new Random(round * 10 + r);
                while (!stop.IsCancellationRequested)
                {
                    var value = lookups[random.Next(keys)].GetValue();
                    Assert.True(value.IsSuccess, value.ErrorValue?.Message);
                }
            }, token)).ToArray();

            var writers = Enumerable.Range(0, 2).Select(w => Task.Run(() =>
            {
                var random = new Random(round * 100 + w);
                for (var i = 0; i < 300; i++)
                {
                    // Each writer edits its own half of the files
                    var file = random.Next(keys / 2) * 2 + w;
                    Decl old, replacement;
                    lock (model)
                    {
                        old = model[file];
                        replacement = old with { Value = random.Next(1000) };
                        model[file] = replacement;
                    }

                    context.DeclsSource.Replace(old, replacement);
                    _ = lookups[random.Next(keys)].GetValue();
                }
            }, token)).ToArray();

            await Task.WhenAll(writers);
            stop.Cancel();
            await Task.WhenAll(readers);

            for (var i = 0; i < keys; i++)
                Assert.Equal(model[i].Value * 2, lookups[i].Value);
        }
    }

    /// <summary>
    /// A pass that overlaps a key change records no stamp. Key j is looked up in one table, key k in
    /// another over the same declarations. After an edit of k's declaration (which marks both tables)
    /// and a change of another input of j's reader, a read of j's reader recomputes j's table (no
    /// tracked key changes there, so no key change) and, while it computes the reader, another thread
    /// reads k, whose table's commit marks k's readers: a key change during the first pass, which must
    /// then store the reader without a stamp.
    /// </summary>
    [Fact]
    public void AKeyChangeDuringARead_PreventsTheReadsStamps()
    {
        var token = TestContext.Current.CancellationToken;
        using var context = new DeclContext();
        var tableJ = MinTable(context.Decls);
        var tableK = context.Decls.CollectToDictionary(d => d.Name, d => -d.Value, Comparer<int>.Default);
        Action? during = null;
        var j = tableJ.Lookup("j").Combine(context.Switch).Select(p =>
        {
            var run = during;
            during = null;
            run?.Invoke();
            return p.Item1 + p.Item2;
        });
        var k = tableK.Lookup("k").Select(v => v);
        context.SwitchSource.Set(0);
        context.DeclsSource.PushRange([new Decl(0, "j", 1), new Decl(1, "k", 1)]);
        Assert.Equal(1, j.Value);
        Assert.Equal(-1, k.Value);

        context.DeclsSource.Replace(new Decl(1, "k", 1), new Decl(1, "k", 2));
        context.SwitchSource.Set(10);
        var stampEpoch = context.Graph.SharedState.StampEpoch;
        var kDirty = context.Graph.Versions.GetState(k.Node.Id).Dirty;
        during = () => Task.Run(() => Assert.Equal(-2, k.Value), token).Wait(token);
        Assert.Equal(11, j.Value);
        Assert.Null(during);
        Assert.Equal(kDirty + 1, context.Graph.Versions.GetState(k.Node.Id).Dirty); // the key change ran
        Assert.True(context.Graph.SharedState.StampEpoch > stampEpoch);
        Assert.True(context.Graph.ValueCache.TryGet(j.Node.Id, out var cached));
        Assert.Equal(CachedNodeValue.NoStamp, ((CachedNodeValue)cached!).Stamp);

        context.DeclsSource.Replace(new Decl(0, "j", 1), new Decl(0, "j", 3));
        context.DeclsSource.Replace(new Decl(1, "k", 2), new Decl(1, "k", 3));
        Assert.Equal(13, j.Value);
        Assert.Equal(-3, k.Value);
    }

    /// <summary>
    /// A key's version as of a dictionary result older than the key's latest change matches nothing
    /// (a reader that read such a result while a later commit changed its key is computed again),
    /// and otherwise is the version at which the key last changed.
    /// </summary>
    [Fact]
    public void AKeysVersionAsOfAnOlderResult_MatchesNothing()
    {
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var a = table.Lookup("a");
        var b = table.Lookup("b");
        context.DeclsSource.PushRange([new Decl(0, "a", 1), new Decl(1, "b", 1)]);
        Assert.Equal(1, a.Value);
        Assert.Equal(1, b.Value);

        var dictionary = (ValueNodeHandle<ImmutableDictionary<string, int>>)(context.Graph.SharedState.Handles.TryGetValue(table.Node, out var h) ? h : null)!;
        var keyA = KeyIndex<string, int>.GetKeyHandle(dictionary, "a", context.Graph);
        var keyB = KeyIndex<string, int>.GetKeyHandle(dictionary, "b", context.Graph);
        var before = dictionary.State.Version;
        Assert.Equal(keyA.VersionAt(before), keyA.VersionAt(before));

        context.DeclsSource.Replace(new Decl(0, "a", 1), new Decl(0, "a", 2));
        Assert.Equal(2, a.Value);
        var after = dictionary.State.Version;
        Assert.True(after > before);

        // a changed after `before`: no version as of `before` matches another
        Assert.True(keyA.VersionAt(before) < 0);
        Assert.NotEqual(keyA.VersionAt(before), keyA.VersionAt(before));
        Assert.Equal(after, keyA.VersionAt(after));

        // b did not change: its version is the same as of either result
        Assert.Equal(keyB.VersionAt(before), keyB.VersionAt(after));
        Assert.Equal(2, ((KeyIndex<string, int>)dictionary.KeyIndex!).KeyCount);
    }

    /// <summary>
    /// A key change in progress (paused after it marked the lookup of k, before it reached k's
    /// reader), with a source change on another thread at that point (a read could not run there: it
    /// would wait for the commit lock that the key change holds). The source change marks the table
    /// only; afterwards every reader agrees with the model.
    /// </summary>
    [Fact]
    public void ASourceChangeAndAReadDuringAKeyChange_LeaveEveryReaderCorrect()
    {
        var token = TestContext.Current.CancellationToken;
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var lookupK = table.Lookup("k");
        var k = lookupK.Select(v => v * 10);
        var m = table.Lookup("m").Select(v => v * 10);
        context.DeclsSource.PushRange([new Decl(0, "k", 1), new Decl(1, "m", 1)]);
        Assert.Equal(10, k.Value);
        Assert.Equal(10, m.Value);

        var lookupState = context.Graph.Versions.GetState(lookupK.Node.Id);
        var readerState = context.Graph.Versions.GetState(k.Node.Id);
        long? readerDirtyDuring = null;
        var paused = 0;
        context.Graph.SharedState.OnNodeMarked = state =>
        {
            if (state != lookupState || Interlocked.Exchange(ref paused, 1) != 0)
                return;

            context.Graph.SharedState.OnNodeMarked = null;
            readerDirtyDuring = readerState.Dirty;
            Task.Run(() => context.DeclsSource.Replace(new Decl(1, "m", 1), new Decl(1, "m", 2)), token).Wait(token);
        };

        // The key change runs inside this read (the table's commit marks k's readers)
        context.DeclsSource.Replace(new Decl(0, "k", 1), new Decl(0, "k", 2));
        var readerDirtyBefore = readerState.Dirty;
        Assert.Equal(20, k.Value);
        Assert.Equal(1, paused);
        Assert.Equal(readerDirtyBefore, readerDirtyDuring); // paused before the reader was marked
        Assert.True(readerState.Dirty > readerDirtyBefore);
        Assert.Equal(20, m.Value);
        Assert.Equal(20, k.Value);

        context.DeclsSource.Replace(new Decl(0, "k", 2), new Decl(0, "k", 3));
        context.DeclsSource.Replace(new Decl(1, "m", 2), new Decl(1, "m", 3));
        Assert.Equal(30, m.Value);
        Assert.Equal(30, k.Value);
    }

    /// <summary>
    /// A read whose snapshot straddles a key change: between the read's snapshot of the change state
    /// and of the revision, another thread edits a key and reads its reader (committing the table and
    /// marking the readers of the key). The straddling read may see either value but must not stamp
    /// one that hides the edit.
    /// </summary>
    [Fact]
    public void AReadStraddlingAKeyChange_StampsNothingStale()
    {
        var token = TestContext.Current.CancellationToken;
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var k = table.Lookup("k").Select(v => v + 100);
        context.DeclsSource.Push(new Decl(0, "k", 1));
        Assert.Equal(101, k.Value);

        context.DeclsSource.Replace(new Decl(0, "k", 1), new Decl(0, "k", 2));
        context.Graph.SharedState.OnEvaluationSnapshot = () =>
        {
            context.Graph.SharedState.OnEvaluationSnapshot = null;
            Task.Run(() => Assert.Equal(102, k.Value), token).Wait(token);
        };
        _ = k.Value;
        Assert.Equal(102, k.Value);

        context.DeclsSource.Replace(new Decl(0, "k", 2), new Decl(0, "k", 3));
        Assert.Equal(103, k.Value);
    }

    /// <summary>
    /// A reader verified while a source change upstream of the table is in progress (version
    /// incremented, dictionary not yet marked): the read may return the value from before the
    /// change, but once the change has ended the reader sees it.
    /// </summary>
    [Fact]
    public void AReadDuringAnUnfinishedSourceChange_SeesTheChangeOnceItEnds()
    {
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var k = table.Lookup("k").Select(v => v + 100);
        context.DeclsSource.Push(new Decl(0, "k", 1));
        Assert.Equal(101, k.Value);

        // A change in progress, paused before it marks anything downstream of the source
        var state = context.Graph.SharedState;
        state.BeginSourceChange();
        _ = k.Value; // overlaps the change: may see either value
        state.EndSourceChange();

        context.DeclsSource.Replace(new Decl(0, "k", 1), new Decl(0, "k", 2));
        state.BeginSourceChange();
        Assert.Equal(102, k.Value);
        state.EndSourceChange();
        Assert.Equal(102, k.Value);
    }

    /// <summary>
    /// Many lookups of keys that never change cost no work after an edit of another key, also when
    /// they are read in other passes than the one that recomputed the table.
    /// </summary>
    [Fact]
    public void UnchangedLookups_AreNotRecomputed_AndKeepTheirStamps()
    {
        using var context = new DeclContext();
        var table = MinTable(context.Decls);
        var lookups = Enumerable.Range(0, 50).Select(i => table.Lookup($"k{i}")).ToArray();
        context.DeclsSource.PushRange(Enumerable.Range(0, 50).Select(i => new Decl(i, $"k{i}", i)));
        Assert.Equal(Enumerable.Range(0, 50), lookups.Select(l => l.Value));
        var before = lookups.Select(Computations).ToArray();

        // Adding and removing keys nobody looks up
        for (var i = 0; i < 10; i++)
        {
            context.DeclsSource.Push(new Decl(100 + i, $"other{i}", i));
            Assert.Equal(Enumerable.Range(0, 50), lookups.Select(l => l.Value));
        }

        Assert.Equal(before, lookups.Select(Computations));
    }
}
