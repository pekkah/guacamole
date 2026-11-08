using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// How exceptions thrown by user lambdas surface (README "Errors are values", the provider XML
/// docs): <c>GetValue()</c>/<c>GetValues()</c> return a failed Result, <c>.Value</c>/<c>.Values</c> and
/// <c>TryGetValue</c> throw <see cref="InvalidOperationException"/>, errors flow downstream without
/// running downstream lambdas, output handlers are skipped, and a pipeline recovers once the input
/// changes.
/// </summary>
public class ErrorSurfacingTests
{
    private static int Fail(int value) => value < 0
        ? throw new ArgumentException("negative input", new FormatException("inner cause"))
        : value;

    [Fact]
    public void FailingSelector_IsAnErrorResult_WithTheExceptionMessageAndInnerError()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(-1);

        var error = ResultAssert.Error(context.Number.Select(Fail).GetValue());

        Assert.Equal("negative input", error.Message);
        Assert.NotNull(error.InnerError);
        Assert.Equal("inner cause", error.InnerError.Message);
    }

    [Fact]
    public void Value_AndTryGetValue_ThrowInvalidOperationException_ForAnError()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(-1);
        var failing = context.Number.Select(Fail);

        var fromValue = Assert.Throws<InvalidOperationException>(() => failing.Value);
        var fromTry = Assert.Throws<InvalidOperationException>(() => failing.TryGetValue(out _));

        Assert.Contains("negative input", fromValue.Message);
        Assert.Contains("negative input", fromTry.Message);
    }

    [Fact]
    public void Values_ThrowsInvalidOperationException_AndGetValuesIsAnError_ForACollectionError()
    {
        using var context = new ApiContext();
        context.ItemsSource.PushRange([new Item("a", 1), new Item("b", -2)]);
        var failing = context.Items.Select(static i => Fail(i.Value));

        Assert.Equal("negative input", ResultAssert.Error(failing.GetValues()).Message);
        var ex = Assert.Throws<InvalidOperationException>(() => failing.Values);
        Assert.Contains("negative input", ex.Message);
    }

    [Fact]
    public void Errors_FlowThroughEveryDownstreamOperator_WithoutRunningDownstreamLambdas()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(-1);
        context.TextSource.Set("t");
        context.ItemsSource.Push(new Item("a", 1));
        var failing = context.Number.Select(Fail);
        var calls = new CallCounter();

        Assert.True(failing.Select(x => calls.Pass(x)).GetValue().IsError);
        Assert.True(failing.Where(x => calls.Pass(x) > 0).GetValue().IsError);
        Assert.True(failing.Combine(context.Text).GetValue().IsError);
        Assert.True(failing.SelectMany(x => new[] { calls.Pass(x) }).GetValues().IsError);
        Assert.True(context.Items.Combine(failing).GetValues().IsError);

        var failingItems = context.Items.Select(static i => Fail(-i.Value));
        Assert.True(failingItems.Select(i => calls.Pass(i)).GetValues().IsError);
        Assert.True(failingItems.Where(i => calls.Pass(i) > 0).GetValues().IsError);
        Assert.True(failingItems.SelectMany(i => new[] { calls.Pass(i) }).GetValues().IsError);
        Assert.True(failingItems.Collect().GetValue().IsError);
        Assert.True(failingItems.Merge(context.Items.Select(static i => i.Value)).GetValues().IsError);
        Assert.True(context.Items.Select(static i => i.Value).Merge(failingItems).GetValues().IsError);

        Assert.Equal(0, calls.Count);
    }

    [Fact]
    public void FailingPredicate_AndFailingSelectMany_AreErrors()
    {
        using var context = new ApiContext();
        context.NumberSource.Set(-1);
        context.ItemsSource.Push(new Item("a", -1));

        Assert.True(context.Number.Where(static x => Fail(x) > 0).GetValue().IsError);
        Assert.True(context.Number.SelectMany(static x => new[] { Fail(x) }).GetValues().IsError);
        Assert.True(context.Items.Where(static i => Fail(i.Value) > 0).GetValues().IsError);
        Assert.True(context.Items.SelectMany(static i => new[] { Fail(i.Value) }).GetValues().IsError);
        Assert.True(context.Items.SelectMany<Item, int>(static _ => null!).GetValues().IsError);
    }

    [Fact]
    public void ReadingAFailedProvider_DoesNotThrowFromSourceOperations()
    {
        using var context = new ApiContext();
        var failing = context.Number.Select(Fail);
        context.RegisterOutput(failing, (_, _) => { });

        // The handler's provider fails, but the Set call does not throw
        context.NumberSource.Set(-1);

        Assert.True(failing.GetValue().IsError);
    }

    [Fact]
    public void OutputHandler_IsSkippedWhileItsProviderFails_AndCalledOnRecovery()
    {
        using var context = new ApiContext();
        var failing = context.Number.Select(Fail);
        var delivered = new List<int>();
        context.RegisterOutput(failing, (v, _) => delivered.Add(v));

        context.NumberSource.Set(-1);
        Assert.Empty(delivered);

        context.NumberSource.Set(5);
        Assert.Equal([5], delivered);
    }

    [Fact]
    public void CollectionOutputHandler_IsSkippedWhileItsProviderFails()
    {
        using var context = new ApiContext();
        var failing = context.Items.Select(static i => Fail(i.Value));
        var deliveries = new List<int[]>();
        context.RegisterOutput(failing, (v, _) => deliveries.Add(v.ToArray()));

        context.ItemsSource.Push(new Item("a", -1));
        Assert.Empty(deliveries);

        context.ItemsSource.Replace(new Item("a", -1), new Item("a", 1));
        Assert.Equal([1], Assert.Single(deliveries));
    }

    [Fact]
    public void Pipeline_RecoversFromAnError_WhenTheInputChanges()
    {
        using var context = new ApiContext();
        var doubled = context.Number.Select(Fail).Select(static x => x * 2);

        context.NumberSource.Set(-1);
        Assert.True(doubled.GetValue().IsError);

        context.NumberSource.Set(4);
        Assert.Equal(8, doubled.Value);
    }

    [Fact]
    public void ErrorResult_IsNotCached_TheFailingLambdaRunsOnEveryRead()
    {
        // README "Errors are values": failed results are not cached, so a failing stage reruns when
        // it is read again (and counts as changed for the stages downstream of it)
        using var context = new ApiContext();
        var calls = new CallCounter();
        var failing = context.Number.Select(x =>
        {
            calls.Increment();
            return Fail(x);
        });
        context.NumberSource.Set(-1);

        Assert.True(failing.GetValue().IsError);
        Assert.True(failing.GetValue().IsError);
        Assert.Equal(2, calls.Count);

        // Once it succeeds, its value is cached again
        context.NumberSource.Set(1);
        Assert.Equal(1, failing.Value);
        Assert.Equal(1, failing.Value);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public void ComputationError_FromException_KeepsTheChainAndTheDiagnosticId()
    {
        var error = ComputationError.FromException(
            new InvalidOperationException("outer", new ArgumentException("middle", new FormatException("inner"))),
            "GUAC-X");

        Assert.Equal("outer", error.Message);
        Assert.Equal("GUAC-X", error.DiagnosticId);
        Assert.Equal("middle", error.InnerError!.Message);
        Assert.Equal("inner", error.InnerError.InnerError!.Message);
        Assert.Null(error.InnerError.InnerError.InnerError);
    }

    [Fact]
    public void ComputationError_FromNullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ComputationError.FromException(null!));
    }

    [Fact]
    public void ComputationError_HasValueEquality()
    {
        Assert.Equal(new ComputationError("m", DiagnosticId: "d"), new ComputationError("m", DiagnosticId: "d"));
        Assert.NotEqual(new ComputationError("m"), new ComputationError("n"));
    }
}
