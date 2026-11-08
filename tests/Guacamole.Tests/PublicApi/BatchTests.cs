using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// <see cref="IBatchOperation{T}"/> as documented in its XML docs, quickstart "Batches" and the
/// performance guide's "Batching": buffered until Commit, all or nothing, a single notification,
/// rollback, auto-commit on Dispose and the behaviour after Commit or Dispose.
/// </summary>
public class BatchTests
{
    [Fact]
    public void Operations_AreBufferedUntilCommit()
    {
        using var context = new ApiContext();
        using var batch = context.ItemsSource.BeginBatch();

        batch.Push(new Item("a", 1));
        Assert.Empty(context.ItemsSource.GetSnapshot());
        Assert.Empty(context.Items.Values);

        batch.Commit();
        Assert.Equal([new Item("a", 1)], context.Items.Values);
    }

    [Fact]
    public void Commit_RunsOutputHandlersOnce_ForTheWholeBatch()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2)]);
        var deliveries = new List<Item[]>();
        context.RegisterOutput(context.Items, (items, _) => deliveries.Add(items.ToArray()));

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Replace(new Item("a", 1), new Item("A", 10));
            batch.Remove(new Item("b", 2));
            batch.Push(new Item("c", 3));
            batch.Commit();
        }

        var delivered = Assert.Single(deliveries);
        Assert.Equal([new Item("A", 10), new Item("c", 3)], delivered);
    }

    [Fact]
    public void Commit_IsAllOrNothing_AndDoesNotRunOutputHandlersOnFailure()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var handlerCalls = 0;
        context.RegisterOutput(context.Items, (_, _) => handlerCalls++);
        var changes = 0;
        context.ItemsSource.Changed += (_, _) => changes++;

        var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("b", 2));
        batch.Replace(new Item("missing", 0), new Item("c", 3));

        Assert.Throws<InvalidOperationException>(batch.Commit);
        batch.Dispose(); // does not re-apply

        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
        Assert.Equal(0, changes);
        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public void Commit_WithANullItem_ThrowsArgumentNullException_AndAppliesNothing()
    {
        using var context = new ApiContext();
        var batch = context.WordsSource.BeginBatch();
        batch.Push("a");
        batch.Push(null!);

        Assert.Throws<ArgumentNullException>(batch.Commit);

        Assert.Empty(context.WordsSource.GetSnapshot());
    }

    [Fact]
    public void AfterCommit_AddingOperationsOrCommittingAgain_Throws()
    {
        using var context = new ApiContext();
        using var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("a", 1));
        batch.Commit();

        Assert.Throws<InvalidOperationException>(() => batch.Push(new Item("b", 2)));
        Assert.Throws<InvalidOperationException>(() => batch.Remove(new Item("a", 1)));
        Assert.Throws<InvalidOperationException>(() => batch.Replace(new Item("a", 1), new Item("b", 2)));
        Assert.Throws<InvalidOperationException>(batch.Commit);
        batch.Rollback(); // documented: does nothing after Commit

        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void AfterDispose_EveryOperationThrowsObjectDisposedException()
    {
        using var context = new ApiContext();
        var batch = context.ItemsSource.BeginBatch();
        batch.Dispose();
        batch.Dispose(); // idempotent

        Assert.Throws<ObjectDisposedException>(() => batch.Push(new Item("a", 1)));
        Assert.Throws<ObjectDisposedException>(() => batch.Remove(new Item("a", 1)));
        Assert.Throws<ObjectDisposedException>(() => batch.Replace(new Item("a", 1), new Item("b", 2)));
        Assert.Throws<ObjectDisposedException>(batch.Commit);
        Assert.Throws<ObjectDisposedException>(batch.Rollback);
    }

    [Fact]
    public void Dispose_WithoutCommitOrRollback_Commits()
    {
        using var context = new ApiContext();

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(new Item("a", 1));
        }

        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void Rollback_DiscardsPendingOperations_AndDisposeThenAppliesNothing()
    {
        using var context = new ApiContext();
        var changes = 0;
        context.ItemsSource.Changed += (_, _) => changes++;

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(new Item("a", 1));
            batch.Push(new Item("b", 2));
            batch.Rollback();
        }

        Assert.Empty(context.ItemsSource.GetSnapshot());
        Assert.Equal(0, changes);
    }

    [Fact]
    public void EmptyBatch_AndBatchWithoutEffect_RaiseNoNotification()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var changes = 0;
        context.ItemsSource.Changed += (_, _) => changes++;

        using (var empty = context.ItemsSource.BeginBatch())
        {
            empty.Commit();
        }

        using (var noEffect = context.ItemsSource.BeginBatch())
        {
            noEffect.Push(new Item("a", 1));         // already present
            noEffect.Remove(new Item("missing", 0)); // not present
            noEffect.Push(new Item("b", 2));
            noEffect.Remove(new Item("b", 2));       // cancels out
            noEffect.Commit();
        }

        Assert.Equal(0, changes);
        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void Operations_SeeTheEffectsOfEarlierOperationsInTheSameBatch()
    {
        using var context = new ApiContext();

        using var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("a", 1));
        batch.Replace(new Item("a", 1), new Item("b", 2)); // valid only after the Push
        batch.Commit();

        Assert.Equal([new Item("b", 2)], context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void Dispose_AfterTheContextWasDisposed_DiscardsPendingOperationsWithoutThrowing()
    {
        // #96: a using block that ends after the context was disposed must not throw from Dispose
        var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var changes = 0;
        context.ItemsSource.Changed += (_, _) => changes++;
        var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("b", 2));
        batch.Remove(new Item("a", 1));

        context.Dispose();
        batch.Dispose();
        batch.Dispose(); // idempotent

        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
        Assert.Equal(0, changes);
        Assert.Throws<ObjectDisposedException>(() => batch.Push(new Item("c", 3)));
        Assert.Throws<ObjectDisposedException>(batch.Commit);
    }

    [Fact]
    public void UsingBlock_EndingAfterTheContextWasDisposed_DoesNotThrow()
    {
        var context = new ApiContext();

        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Push(new Item("a", 1));
            context.Dispose();
            batch.Push(new Item("b", 2)); // only buffered: the batch does not check the context
        }

        Assert.Empty(context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void Commit_AfterTheContextWasDisposed_Throws_AndDisposeThenDoesNothing()
    {
        var context = new ApiContext();
        var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("a", 1));

        context.Dispose();

        Assert.Throws<ObjectDisposedException>(batch.Commit);
        batch.Dispose();
        Assert.Empty(context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void Rollback_AfterTheContextWasDisposed_DoesNotThrow()
    {
        var context = new ApiContext();
        using var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("a", 1));

        context.Dispose();

        batch.Rollback();
        Assert.Empty(context.ItemsSource.GetSnapshot());
    }

    [Fact]
    public void BeginBatch_AfterTheContextWasDisposed_ThrowsObjectDisposedException()
    {
        // Consistent with the source's own change methods (#88): the batch could never be committed
        var context = new ApiContext();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.ItemsSource.BeginBatch());
    }

    [Fact]
    public void Dispose_OfAnEmptyBatch_AfterTheContextWasDisposed_DoesNotThrow()
    {
        var context = new ApiContext();
        var batch = context.ItemsSource.BeginBatch();

        context.Dispose();

        batch.Dispose();
    }
}
