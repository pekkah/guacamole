using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// <see cref="IIncrementalSource{T}"/> and <see cref="IIncrementalValueSource{T}"/> as documented in
/// their XML docs, README "Push-Based Sources", quickstart "Pushing Changes" and the migration
/// guide's source API table.
/// </summary>
public class SourceTests
{
    private static List<SourceChangedEventArgs> RecordChanges(ApiContext context)
    {
        var changes = new List<SourceChangedEventArgs>();
        context.ItemsSource.Changed += (_, e) => changes.Add(e);
        return changes;
    }

    [Fact]
    public void Push_AppendsInInsertionOrder_AndIgnoresEqualItems()
    {
        using var context = new ApiContext();
        var changes = RecordChanges(context);

        context.ItemsSource.Push(new Item("b", 2));
        context.ItemsSource.Push(new Item("a", 1));
        context.ItemsSource.Push(new Item("b", 2)); // equal: no-op

        Assert.Equal([new Item("b", 2), new Item("a", 1)], context.ItemsSource.GetSnapshot());
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void Changed_CarriesTheSourceIdAndAUtcTimestamp()
    {
        using var context = new ApiContext();
        var changes = RecordChanges(context);
        var before = DateTimeOffset.UtcNow;

        context.ItemsSource.Push(new Item("a", 1));

        var change = Assert.Single(changes);
        Assert.Equal("ApiContext.Items", change.SourceId);
        Assert.Equal(TimeSpan.Zero, change.Timestamp.Offset);
        Assert.InRange(change.Timestamp, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Remove_OfMissingItem_IsANoOp_WithoutNotification()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var changes = RecordChanges(context);

        context.ItemsSource.Remove(new Item("missing", 0));
        context.ItemsSource.Remove(new Item("a", 1));

        Assert.Empty(context.ItemsSource.GetSnapshot());
        Assert.Single(changes);
    }

    [Fact]
    public void Replace_TakesTheOldItemsPosition()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2), new Item("c", 3)]);

        context.ItemsSource.Replace(new Item("b", 2), new Item("B", 20));

        Assert.Equal([new Item("a", 1), new Item("B", 20), new Item("c", 3)], context.Items.Values);
    }

    [Fact]
    public void Replace_OfMissingItem_Throws_AndChangesNothing()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var changes = RecordChanges(context);

        Assert.Throws<InvalidOperationException>(() => context.ItemsSource.Replace(new Item("missing", 0), new Item("x", 9)));

        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
        Assert.Empty(changes);
    }

    [Fact]
    public void Replace_WithAnItemThatIsAlreadyPresent_Throws_AndChangesNothing()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2)]);
        var changes = RecordChanges(context);

        Assert.Throws<InvalidOperationException>(() => context.ItemsSource.Replace(new Item("a", 1), new Item("b", 2)));

        Assert.Equal([new Item("a", 1), new Item("b", 2)], context.ItemsSource.GetSnapshot());
        Assert.Empty(changes);
    }

    [Fact]
    public void Replace_WithAnEqualItem_IsANoOp_EvenWhenTheItemIsMissing()
    {
        // Documented exception to "throws if oldItem is missing" (IIncrementalSource.Replace XML docs):
        // replacing an item with an equal one changes nothing and is not checked.
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var changes = RecordChanges(context);

        context.ItemsSource.Replace(new Item("missing", 0), new Item("missing", 0));
        using (var batch = context.ItemsSource.BeginBatch())
        {
            batch.Replace(new Item("missing", 0), new Item("missing", 0));
            batch.Commit();
        }

        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
        Assert.Empty(changes);
    }

    [Fact]
    public void Replace_OfPresentItem_WithAnEqualItem_IsANoOp_WithoutNotification()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var changes = RecordChanges(context);

        context.ItemsSource.Replace(new Item("a", 1), new Item("a", 1));

        Assert.Equal([new Item("a", 1)], context.ItemsSource.GetSnapshot());
        Assert.Empty(changes);
    }

    [Fact]
    public void Clear_RemovesEverything_WithOneNotification_AndIsANoOpWhenEmpty()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2)]);
        var changes = RecordChanges(context);

        context.ItemsSource.Clear();
        context.ItemsSource.Clear();

        Assert.Empty(context.ItemsSource.GetSnapshot());
        Assert.Empty(context.Items.Values);
        Assert.Single(changes);
    }

    [Fact]
    public void RemoveThenPush_MovesTheItemToTheEnd()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", 2)]);

        context.ItemsSource.Remove(new Item("a", 1));
        context.ItemsSource.Push(new Item("a", 1));

        Assert.Equal([new Item("b", 2), new Item("a", 1)], context.Items.Values);
    }

    [Fact]
    public void GetSnapshot_IsAStableReadOnlyCopy()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));

        var snapshot = context.ItemsSource.GetSnapshot();
        context.ItemsSource.Push(new Item("b", 2));

        Assert.Equal([new Item("a", 1)], snapshot);
        Assert.False(snapshot is Item[]);
        if (snapshot is ICollection<Item> collection)
        {
            Assert.True(collection.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => collection.Add(new Item("x", 0)));
        }
    }

    [Fact]
    public void CollectionSourceOperations_RejectNullItems()
    {
        using var context = new ApiContext();
        context.WordsSource.Push("a");
        var source = context.WordsSource;

        Assert.Throws<ArgumentNullException>(() => source.Push(null!));
        Assert.Throws<ArgumentNullException>(() => source.Remove(null!));
        Assert.Throws<ArgumentNullException>(() => source.Replace(null!, "b"));
        Assert.Throws<ArgumentNullException>(() => source.Replace("a", null!));
        Assert.Throws<ArgumentNullException>(() => source.PushRange(null!));
        Assert.Throws<ArgumentNullException>(() => source.PushRange(["b", null!]));
        Assert.Throws<ArgumentNullException>(() => IncrementalSourceExtensions.PushRange<string>(null!, ["b"]));
        Assert.Throws<ArgumentNullException>(() => IncrementalSourceExtensions.BeginBatch<string>(null!));

        Assert.Equal(["a"], source.GetSnapshot());
    }

    [Fact]
    public void PushRange_AppendsNewItemsInOrder_WithOneNotification_AndNoneWhenNothingIsNew()
    {
        using var context = new ApiContext();
        context.ItemsSource.Push(new Item("a", 1));
        var changes = RecordChanges(context);

        context.ItemsSource.PushRange([new Item("c", 3), new Item("a", 1), new Item("b", 2), new Item("c", 3)]);
        context.ItemsSource.PushRange([new Item("a", 1)]);
        context.ItemsSource.PushRange([]);

        Assert.Equal([new Item("a", 1), new Item("c", 3), new Item("b", 2)], context.ItemsSource.GetSnapshot());
        Assert.Single(changes);
    }

    [Fact]
    public void Set_OfEqualValue_IsANoOp_WithoutNotification()
    {
        using var context = new ApiContext();
        var changes = 0;
        context.TextSource.Changed += (_, _) => changes++;

        context.TextSource.Set("a");
        context.TextSource.Set(new string('a', 1)); // equal, different instance
        context.TextSource.Set("b");

        Assert.Equal(2, changes);
        Assert.Equal("b", context.TextSource.GetValue());
    }

    [Fact]
    public void Set_OfDefaultValueTypeValue_IsAChangeTheFirstTime()
    {
        using var context = new ApiContext();
        var changes = 0;
        context.NumberSource.Changed += (_, _) => changes++;

        context.NumberSource.Set(0);
        context.NumberSource.Set(0);

        Assert.Equal(1, changes);
        Assert.Equal(0, context.Number.Value);
    }

    [Fact]
    public void Set_Null_Throws_AndKeepsTheCurrentValue()
    {
        using var context = new ApiContext();
        context.TextSource.Set("a");

        Assert.Throws<ArgumentNullException>(() => context.TextSource.Set(null!));

        Assert.Equal("a", context.Text.Value);
    }

    [Fact]
    public void ValueSourceGetValue_IsDefaultBeforeSet()
    {
        using var context = new ApiContext();

        Assert.Null(context.TextSource.GetValue());
        Assert.Equal(0, context.NumberSource.GetValue());
    }

    [Fact]
    public void SourceChanges_InvalidateProvidersBuiltBeforeTheChange()
    {
        using var context = new ApiContext();
        var count = context.Items.Select(static i => i.Value).Collect().Select(static values => values.Length);
        Assert.Equal(0, count.Value);

        context.ItemsSource.Push(new Item("a", 1));
        Assert.Equal(1, count.Value);

        context.ItemsSource.Clear();
        Assert.Equal(0, count.Value);
    }
}
