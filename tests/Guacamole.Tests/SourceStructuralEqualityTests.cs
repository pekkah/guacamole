using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #64: value sources, source node handles and the per-item memo of collection operators use
/// the structural default comparer, so an equal but newly built <see cref="ImmutableArray{T}"/> is
/// not a change.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class SourceStructuralEqualityTests
{
    [ComputingContext]
    private partial class ArrayContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<ImmutableArray<string>> Tokens { get; }
        public partial IncrementalValueProvider<(ImmutableArray<string>, int)> Pair { get; }
        public partial IncrementalValuesProvider<ImmutableArray<string>> Rows { get; }
    }

    private static ImmutableArray<string> Build(params string[] items) => [.. items];

    [Fact]
    public void ValueSource_SetEqualNewArray_DoesNotNotifyOrBumpVersion()
    {
        using var context = new ArrayContext();
        var notifications = 0;
        var runs = 0;
        context.TokensSource.Changed += (_, _) => notifications++;
        var joined = context.Tokens.Select(t => { runs++; return string.Join("|", t); });

        context.TokensSource.Set(Build("a", "b"));
        Assert.Equal("a|b", joined.Value);
        var version = context.Graph.Versions.GetOutputVersion(context.Tokens.Node.Id);

        context.TokensSource.Set(Build("a", "b"));

        Assert.Equal(1, notifications);
        Assert.Equal("a|b", joined.Value);
        Assert.Equal(1, runs);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(context.Tokens.Node.Id));
    }

    [Fact]
    public void ValueSource_SetChangedArray_Notifies()
    {
        using var context = new ArrayContext();
        var notifications = 0;
        context.TokensSource.Changed += (_, _) => notifications++;

        context.TokensSource.Set(Build("a", "b"));
        context.TokensSource.Set(Build("a", "c"));

        Assert.Equal(2, notifications);
        Assert.Equal(Build("a", "c"), context.Tokens.Value, ImmutableArrayEqualityComparer<string>.Default);
    }

    [Fact]
    public void ValueSource_TupleWithArray_IsComparedComponentWise()
    {
        using var context = new ArrayContext();
        var notifications = 0;
        context.PairSource.Changed += (_, _) => notifications++;

        context.PairSource.Set((Build("a"), 1));
        context.PairSource.Set((Build("a"), 1));
        Assert.Equal(1, notifications);

        context.PairSource.Set((Build("a"), 2));
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void ValueSource_OutputHandler_NotCalledForEqualArray()
    {
        using var context = new ArrayContext();
        var calls = 0;
        context.RegisterOutput(context.Tokens, (_, _) => calls++);

        context.TokensSource.Set(Build("a"));
        context.TokensSource.Set(Build("a"));

        Assert.Equal(1, calls);
    }

    /// <summary>A node without a comparer of its own (no <see cref="IHasComparer{T}"/>).</summary>
    private sealed class TokensCopyNode(IComputationNode<ImmutableArray<string>> source)
        : ComputationNodeBase<ImmutableArray<string>>("TokensCopy-" + Guid.NewGuid().ToString("N"), [source.Id])
    {
        public override Result<ImmutableArray<string>, ComputationError> Compute(ComputationContext context)
        {
            var result = context.Read(source);
            return result.IsSuccess
                ? Result<ImmutableArray<string>, ComputationError>.Success([.. result.ValueOrDefault])
                : result;
        }
    }

    /// <summary>A collection node without a comparer of its own (no <see cref="IHasComparer{T}"/>).</summary>
    private sealed class RowsCopyNode(ICollectionComputationNode<ImmutableArray<string>> source)
        : CollectionComputationNodeBase<ImmutableArray<string>>("RowsCopy-" + Guid.NewGuid().ToString("N"), [source.Id])
    {
        public override Result<IEnumerable<ImmutableArray<string>>, ComputationError> Compute(ComputationContext context)
        {
            var result = context.Read(source);
            return result.IsSuccess
                ? Result<IEnumerable<ImmutableArray<string>>, ComputationError>.Success(result.ValueOrDefault!.Select(r => (ImmutableArray<string>)[.. r]).ToArray())
                : result;
        }
    }

    [Fact]
    public void ValueNodeHandle_WithoutNodeComparer_FallsBackToStructuralDefault()
    {
        using var context = new ArrayContext();
        var copy = new IncrementalValueProvider<ImmutableArray<string>>(
            new TokensCopyNode(context.Tokens.Node), context.Graph, new ProviderMetadata("copy", "test"));
        var runs = 0;
        var joined = copy.Select(t => { runs++; return string.Join("|", t); });
        context.TokensSource.Set(Build("a", "b"));
        Assert.Equal("a|b", joined.Value);
        var version = context.Graph.Versions.GetOutputVersion(copy.Node.Id);

        // The copy recomputes to a new, element-wise equal array: its version is kept
        context.Graph.MarkSourceChanged(context.Tokens.Node.Id);

        Assert.Equal("a|b", joined.Value);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(copy.Node.Id));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void CollectionNodeHandle_WithoutNodeComparer_FallsBackToStructuralDefault()
    {
        using var context = new ArrayContext();
        var copy = new IncrementalValuesProvider<ImmutableArray<string>>(
            new RowsCopyNode(context.Rows.Node), context.Graph, new ProviderMetadata("copy", "test"));
        var runs = 0;
        var counts = copy.Select(r => { runs++; return r.Length; });
        context.RowsSource.Push(Build("a"));
        context.RowsSource.Push(Build("b", "c"));
        Assert.Equal([1, 2], counts.Values);
        var version = context.Graph.Versions.GetOutputVersion(copy.Node.Id);

        // The copy recomputes to new, element-wise equal arrays: its version is kept
        context.Graph.MarkSourceChanged(context.Rows.Node.Id);

        Assert.Equal([1, 2], counts.Values);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(copy.Node.Id));
        Assert.Equal(2, runs);
    }

    [Fact]
    public void CollectionSource_ReplaceWithEqualArray_CutsOffAtCollect()
    {
        using var context = new ArrayContext();
        var a1 = Build("a");
        context.RowsSource.Push(a1);
        context.RowsSource.Push(Build("b"));
        var runs = 0;
        var collected = context.Rows.Collect();
        var counts = collected.Select(rows => { runs++; return rows.Length; });
        Assert.Equal(2, counts.Value);
        var version = context.Graph.Versions.GetOutputVersion(collected.Node.Id);

        // Replaces a row with an equal, newly built array
        context.RowsSource.Replace(a1, Build("a"));

        Assert.Equal(2, counts.Value);
        Assert.Equal(1, runs);
        Assert.Equal(version, context.Graph.Versions.GetOutputVersion(collected.Node.Id));
    }

    [Fact]
    public void CollectionSource_OutputHandler_NotCalledForEqualArrayItems()
    {
        using var context = new ArrayContext();
        var calls = 0;
        context.RegisterOutput(context.Rows, (_, _) => calls++);
        var a1 = Build("a");
        context.RowsSource.Push(a1);
        Assert.Equal(1, calls);

        context.RowsSource.Replace(a1, Build("a"));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void CollectionSelectAndWhere_OverArrayItems_ReuseMemoizedResults()
    {
        using var context = new ArrayContext();
        var selectRuns = 0;
        var whereRuns = 0;
        var selected = context.Rows.Select(r => { selectRuns++; return string.Join("|", r); });
        var filtered = context.Rows.Where(r => { whereRuns++; return r.Length > 0; });
        var a1 = Build("a", "b");
        context.RowsSource.Push(a1);
        context.RowsSource.Push(Build("c"));
        Assert.Equal(["a|b", "c"], selected.Values);
        Assert.Equal(2, filtered.Values.Count());

        // An equal, newly built row plus a new row: only the new row runs the lambdas
        using (IBatchOperation<ImmutableArray<string>> batch = context.RowsSource.BeginBatch())
        {
            batch.Replace(a1, Build("a", "b"));
            batch.Push(Build("d"));
            batch.Commit();
        }

        Assert.Equal(["a|b", "c", "d"], selected.Values);
        Assert.Equal(3, filtered.Values.Count());
        Assert.Equal(3, selectRuns);
        Assert.Equal(3, whereRuns);
    }

    [Fact]
    public void ItemMemo_MatchesArrayItemsStructurally()
    {
        var memo = new ItemMemo<ImmutableArray<string>, string>();
        var runs = 0;
        string Compute(ImmutableArray<string> r)
        {
            runs++;
            return string.Join("|", r);
        }

        var first = memo.Apply([Build("a"), Build("b"), Build("c")], Compute);

        // Equal new arrays, in line and moved
        var second = memo.Apply([Build("a"), Build("b"), Build("c")], Compute);
        var third = memo.Apply([Build("c"), Build("a"), Build("x"), Build("b")], Compute);

        Assert.Equal(4, runs); // a, b, c, then x only
        Assert.Same(first, second);
        Assert.Equal(["c", "a", "x", "b"], third);
        Assert.Same(first[2], third[0]);
        Assert.Equal(4, memo.Count);
    }
    // Issue #75: collection sources use the structural comparer for their set semantics

    private static T[] Items<T>(IIncrementalSource<T> source) => ((IncrementalSource<T>)source).GetItems();

    [Fact]
    public void CollectionSource_PushEqualNewArray_IsANoOp()
    {
        using var context = new ArrayContext();
        var notifications = 0;
        context.RowsSource.Changed += (_, _) => notifications++;

        context.RowsSource.Push(Build("a", "b"));
        context.RowsSource.Push(Build("a", "b"));
        context.RowsSource.PushRange([Build("a", "b"), Build("c")]);

        Assert.Equal(2, notifications);
        Assert.Equal(2, context.RowsSource.GetSnapshot().Count);
        Assert.Equal(["a|b", "c"], context.Rows.Values.Select(r => string.Join("|", r)));
    }

    [Fact]
    public void CollectionSource_ReplaceWithEqualNewArray_IsANoOp()
    {
        using var context = new ArrayContext();
        var a1 = Build("a");
        context.RowsSource.Push(a1);
        var snapshot = Items(context.RowsSource);
        var notifications = 0;
        context.RowsSource.Changed += (_, _) => notifications++;

        context.RowsSource.Replace(a1, Build("a"));

        Assert.Equal(0, notifications);
        Assert.Same(snapshot, Items(context.RowsSource));
    }

    [Fact]
    public void CollectionSource_RemoveAndReplace_FindItemsStructurally()
    {
        using var context = new ArrayContext();
        context.RowsSource.PushRange([Build("a"), Build("b")]);

        context.RowsSource.Replace(Build("a"), Build("x"));
        context.RowsSource.Remove(Build("b"));

        Assert.Equal(["x"], context.Rows.Values.Select(r => string.Join("|", r)));
        Assert.Throws<InvalidOperationException>(() => context.RowsSource.Replace(Build("y"), Build("z")));
    }

    [Fact]
    public void CollectionSource_BatchOfEqualArrays_DoesNotNotify()
    {
        using var context = new ArrayContext();
        var a1 = Build("a");
        context.RowsSource.Push(a1);
        var snapshot = Items(context.RowsSource);
        var notifications = 0;
        context.RowsSource.Changed += (_, _) => notifications++;

        using (IBatchOperation<ImmutableArray<string>> batch = context.RowsSource.BeginBatch())
        {
            batch.Push(Build("a"));
            batch.Replace(a1, Build("a"));
            batch.Commit();
        }

        Assert.Equal(0, notifications);
        Assert.Same(snapshot, Items(context.RowsSource));
    }

    [Fact]
    public void CollectionSource_BatchReplacingWithAnEqualArray_IsNotAChange()
    {
        using var context = new ArrayContext();
        var a1 = Build("a");
        context.RowsSource.PushRange([a1, Build("b")]);
        var snapshot = Items(context.RowsSource);
        var notifications = 0;
        context.RowsSource.Changed += (_, _) => notifications++;

        // Replaces a with x and x with an equal, newly built a: the items end up the same
        using (IBatchOperation<ImmutableArray<string>> batch = context.RowsSource.BeginBatch())
        {
            batch.Replace(a1, Build("x"));
            batch.Replace(Build("x"), Build("a"));
            batch.Commit();
        }

        Assert.Equal(0, notifications);
        Assert.Same(snapshot, Items(context.RowsSource));
    }

    [Fact]
    public void CollectionSource_CancellingBatch_KeepsTheSnapshotInstance()
    {
        using var context = new ArrayContext();
        context.RowsSource.PushRange([Build("a"), Build("b")]);
        var snapshot = Items(context.RowsSource);
        var notifications = 0;
        context.RowsSource.Changed += (_, _) => notifications++;

        using (IBatchOperation<ImmutableArray<string>> batch = context.RowsSource.BeginBatch())
        {
            batch.Push(Build("c"));
            batch.Remove(Build("c"));
            batch.Remove(Build("b"));
            batch.Push(Build("b")); // b was last: the order is unchanged
            batch.Commit();
        }

        Assert.Equal(0, notifications);
        Assert.Same(snapshot, Items(context.RowsSource));

        // The source keeps working after the cancelled batch
        context.RowsSource.Remove(Build("a"));
        Assert.Equal(["b"], context.Rows.Values.Select(r => string.Join("|", r)));
    }

    [Fact]
    public void CollectionSource_RandomBatchesOfNewArrays_MatchAStructuralModel()
    {
        var random = new Random(75);
        using var context = new ArrayContext();
        var model = new List<string>(); // rows joined with "|"
        var notifications = 0;
        context.RowsSource.Changed += (_, _) => notifications++;
        ImmutableArray<string> Row(int key) => [.. key.ToString().Select(c => c.ToString())]; // always a new array

        for (var round = 0; round < 2_000; round++)
        {
            var expected = new List<string>(model);
            var fails = false;
            IBatchOperation<ImmutableArray<string>> batch = context.RowsSource.BeginBatch();
            for (var i = random.Next(1, 6); i > 0; i--)
            {
                var item = random.Next(10, 18);
                var key = string.Join("|", Row(item));
                switch (random.Next(3))
                {
                    case 0:
                        batch.Push(Row(item));
                        if (!expected.Contains(key))
                            expected.Add(key);
                        break;
                    case 1:
                        batch.Remove(Row(item));
                        expected.Remove(key);
                        break;
                    default:
                        var replacement = random.Next(10, 18);
                        var replacementKey = string.Join("|", Row(replacement));
                        batch.Replace(Row(item), Row(replacement));
                        if (item == replacement)
                            break;
                        var index = expected.IndexOf(key);
                        if (index < 0 || expected.Contains(replacementKey))
                            fails = true;
                        else
                            expected[index] = replacementKey;
                        break;
                }
            }

            var before = notifications;
            var snapshot = Items(context.RowsSource);
            if (fails)
            {
                Assert.ThrowsAny<InvalidOperationException>(batch.Commit);
                expected = model;
            }
            else
            {
                batch.Commit();
            }

            var unchanged = expected.SequenceEqual(model);
            Assert.Equal(expected, context.Rows.Values.Select(r => string.Join("|", r)));
            Assert.Equal(unchanged ? before : before + 1, notifications);
            if (unchanged)
                Assert.Same(snapshot, Items(context.RowsSource));
            model = expected;
        }
    }
}
