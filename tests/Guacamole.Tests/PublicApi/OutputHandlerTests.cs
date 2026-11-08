using System.Collections.Immutable;
using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// <c>RegisterOutput</c> as documented in its XML docs, README "Output Handlers" and quickstart
/// "Output Handlers": when handlers run (not at registration; synchronously after any effective
/// source change; only when the value changed by the provider's comparer), the arguments they get,
/// and how their exceptions surface.
/// </summary>
public class OutputHandlerTests
{
    [Fact]
    public void RegisterOutput_RejectsANullHandler()
    {
        using var context = new ApiContext();

        Assert.Throws<ArgumentNullException>(() => context.RegisterOutput(context.Number, null!));
        Assert.Throws<ArgumentNullException>(() => context.RegisterOutput(context.Items, null!));
    }

    [Fact]
    public void Handler_IsNotRunAtRegistration_EvenWhenTheProviderHasAValue()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(1);
        context.ItemsSource.Push(new Item("a", 1));
        var calls = 0;

        context.RegisterOutput(context.Number, (_, _) => calls++);
        context.RegisterOutput(context.Items, (_, _) => calls++);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Handler_RunsSynchronously_OnTheThreadThatChangedTheSource_WithTheContext()
    {
        using var context = new ApiContext();
        ComputingContextBase? receivedContext = null;
        var threadId = -1;
        context.RegisterOutput(context.Number, (_, ctx) =>
        {
            receivedContext = ctx;
            threadId = Environment.CurrentManagedThreadId;
        });

        context.NumberSource.Set(1);

        Assert.Same(context, receivedContext); // already set when Set returned
        Assert.Equal(Environment.CurrentManagedThreadId, threadId);
    }

    [Fact]
    public void Handler_RunsAfterAChangeOfAnySource_ButOnlyWhenItsValueChanged()
    {
        using var context = new ApiContext();
        var delivered = new List<int>();
        context.RegisterOutput(context.Number.Select(static x => x * 2), (v, _) => delivered.Add(v));

        // Its own source never set: provider fails, handler skipped
        context.TextSource.Set("unrelated");
        Assert.Empty(delivered);

        context.NumberSource.Set(1);
        Assert.Equal([2], delivered);

        // Another source changes: the handler's provider is re-read, but its value is unchanged
        context.TextSource.Set("other");
        context.ItemsSource.Push(new Item("a", 1));
        Assert.Equal([2], delivered);

        context.NumberSource.Set(2);
        Assert.Equal([2, 4], delivered);
    }

    [Fact]
    public void Handler_ComparesWithTheLastDeliveredValue()
    {
        using var context = new ApiContext();
        var delivered = new List<int>();
        context.RegisterOutput(context.Number.Select(static x => x / 10), (v, _) => delivered.Add(v));

        context.NumberSource.Set(11); // 1
        context.NumberSource.Set(12); // 1: unchanged
        context.NumberSource.Set(25); // 2
        context.NumberSource.Set(13); // 1: differs from the last delivered (2)

        Assert.Equal([1, 2, 1], delivered);
    }

    [Fact]
    public void Handler_OnAProviderWithAComparer_UsesThatComparer()
    {
        using var context = new ApiContext();
        var delivered = new List<string>();
        context.RegisterOutput(context.Text.Select(static s => s, StringComparer.OrdinalIgnoreCase), (v, _) => delivered.Add(v));

        context.TextSource.Set("abc");
        context.TextSource.Set("ABC");
        context.TextSource.Set("abd");

        Assert.Equal(["abc", "abd"], delivered);
    }

    [Fact]
    public void CollectionHandler_RunsOnlyWhenTheItemsChange_ElementWise()
    {
        using var context = new ApiContext();
        var deliveries = new List<int[]>();
        context.RegisterOutput(context.Items.Select(static i => i.Value), (v, _) => deliveries.Add(v.ToArray()));

        context.ItemsSource.Push(new Item("a", 1));
        context.ItemsSource.Replace(new Item("a", 1), new Item("b", 1)); // values unchanged
        context.ItemsSource.Push(new Item("c", 2));

        Assert.Equal(2, deliveries.Count);
        Assert.Equal([1], deliveries[0]);
        Assert.Equal([1, 2], deliveries[1]);
    }

    [Fact]
    public void CollectedHandler_DoesNotRunForAReCollectWithEqualItems()
    {
        using var context = new ApiContext();
        var deliveries = 0;
        context.RegisterOutput(context.Items.Select(static i => i.Value).Collect(), (_, _) => deliveries++);

        context.ItemsSource.Push(new Item("a", 1));
        context.ItemsSource.Replace(new Item("a", 1), new Item("b", 1));

        Assert.Equal(1, deliveries);
    }

    [Fact]
    public void Handler_OnASelectReturningAList_RunsOnEveryRecomputation()
    {
        // README: "other ImmutableArray<T>, list or dictionary values returned by a Select compare by reference"
        using var context = new ApiContext();
        var deliveries = 0;
        context.RegisterOutput(context.Number.Select(static x => new List<int> { x % 2 }), (_, _) => deliveries++);

        context.NumberSource.Set(1);
        context.NumberSource.Set(3);

        Assert.Equal(2, deliveries);
    }

    [Fact]
    public void Handler_OnASelectReturningAnImmutableArray_ComparesElementWise()
    {
        using var context = new ApiContext();
        var deliveries = 0;
        context.RegisterOutput(context.Number.Select(static x => ImmutableArray.Create(x % 2)), (_, _) => deliveries++);

        context.NumberSource.Set(1);
        context.NumberSource.Set(3);

        Assert.Equal(1, deliveries);
    }

    [Fact]
    public void Handler_IsCalledForANullValue()
    {
        // A null computed by a Select is a value (not an error and not "no value"), so it is delivered
        using var context = new ApiContext();
        var delivered = new List<string?>();
        context.RegisterOutput(context.Number.Select(static x => x > 0 ? x.ToString() : null), (v, _) => delivered.Add(v));

        context.NumberSource.Set(1);
        context.NumberSource.Set(-1);
        context.NumberSource.Set(-2);
        context.NumberSource.Set(2);

        Assert.Equal(["1", null, "2"], delivered);
    }

    [Fact]
    public void HandlerExceptions_AreRethrownTogether_FromEverySourceOperation_AfterAllHandlersRan()
    {
        using var context = new ApiContext();
        var ran = new List<string>();
        context.RegisterOutput(context.Items, (_, _) =>
        {
            ran.Add("first");
            throw new InvalidOperationException("first");
        });
        context.RegisterOutput(context.Items, (_, _) => ran.Add("second"));
        context.RegisterOutput(context.Items, (_, _) =>
        {
            ran.Add("third");
            throw new FormatException("third");
        });

        var ex = Assert.Throws<AggregateException>(() => context.ItemsSource.Push(new Item("a", 1)));

        Assert.Equal(["first", "second", "third"], ran);
        Assert.Collection(
            ex.InnerExceptions,
            e => Assert.Equal("first", Assert.IsType<InvalidOperationException>(e).Message),
            e => Assert.Equal("third", Assert.IsType<FormatException>(e).Message));

        // The change itself was applied
        Assert.Single(context.ItemsSource.GetSnapshot());

        Assert.Throws<AggregateException>(() => context.ItemsSource.PushRange([new Item("b", 2)]));
        Assert.Throws<AggregateException>(() => context.ItemsSource.Replace(new Item("b", 2), new Item("c", 3)));
        Assert.Throws<AggregateException>(() => context.ItemsSource.Remove(new Item("c", 3)));
        Assert.Throws<AggregateException>(context.ItemsSource.Clear);
        var batch = context.ItemsSource.BeginBatch();
        batch.Push(new Item("d", 4));
        Assert.Throws<AggregateException>(batch.Commit);
    }

    [Fact]
    public void HandlerException_FromSet_IsAnAggregateException()
    {
        using var context = new ApiContext();
        context.RegisterOutput(context.Number, (_, _) => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<AggregateException>(() => context.NumberSource.Set(1));

        Assert.Equal("boom", Assert.Single(ex.InnerExceptions).Message);
    }

    [Fact]
    public void HandlersRegisteredForTheSameProvider_AllRun()
    {
        using var context = new ApiContext();
        var calls = new List<string>();
        context.RegisterOutput(context.Number, (_, _) => calls.Add("a"));
        context.RegisterOutput(context.Number, (_, _) => calls.Add("b"));

        context.NumberSource.Set(1);

        Assert.Equal(2, calls.Count);
        Assert.Contains("a", calls);
        Assert.Contains("b", calls);
    }

    [Fact]
    public void HandlerRegisteredLater_GetsTheCurrentValueOnTheNextChange()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(1);
        var delivered = new List<int>();
        context.RegisterOutput(context.Number.Select(static x => x > 0), (_, _) => delivered.Add(0));

        // Unrelated change: the handler has never delivered, so it gets the current value
        context.TextSource.Set("t");

        Assert.Single(delivered);
    }
}
