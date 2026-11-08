using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// Issue #83: <see cref="ComputingContextBase.RegisterOutput{T}(IncrementalValueProvider{T}, Action{T, ComputingContextBase})"/>
/// and its collection overload return a registration that unregisters just that handler when disposed.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public partial class OutputRegistrationDisposalTests
{
    [ComputingContext]
    private partial class DisposalContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }

        public partial IncrementalValuesProvider<string> Items { get; }
    }

    [Fact]
    public void Dispose_ValueRegistration_StopsOnlyThatHandler()
    {
        using var context = new DisposalContext();
        var first = new List<int>();
        var second = new List<int>();
        var firstRegistration = context.RegisterOutput(context.Number, (value, _) => first.Add(value));
        context.RegisterOutput(context.Number, (value, _) => second.Add(value));

        context.NumberSource.Set(1);
        firstRegistration.Dispose();
        context.NumberSource.Set(2);

        Assert.Equal([1], first);
        Assert.Equal([1, 2], second);
    }

    [Fact]
    public void Dispose_CollectionRegistration_StopsOnlyThatHandler()
    {
        using var context = new DisposalContext();
        var first = new List<int>();
        var second = new List<int>();
        var firstRegistration = context.RegisterOutput(context.Items, (values, _) => first.Add(values.Count));
        context.RegisterOutput(context.Items, (values, _) => second.Add(values.Count));

        context.ItemsSource.Push("a");
        firstRegistration.Dispose();
        context.ItemsSource.Push("b");

        Assert.Equal([1], first);
        Assert.Equal([1, 2], second);
    }

    [Fact]
    public void Dispose_SameHandlerRegisteredTwice_RemovesOnlyOneRegistration()
    {
        using var context = new DisposalContext();
        var calls = new List<int>();
        Action<int, ComputingContextBase> handler = (value, _) => calls.Add(value);
        var registration = context.RegisterOutput(context.Number, handler);
        context.RegisterOutput(context.Number, handler);

        context.NumberSource.Set(1);
        registration.Dispose();
        context.NumberSource.Set(2);

        Assert.Equal([1, 1, 2], calls);
    }

    [Fact]
    public void Dispose_Twice_IsNoOp()
    {
        using var context = new DisposalContext();
        var calls = new List<int>();
        var registration = context.RegisterOutput(context.Number, (value, _) => calls.Add(value));
        context.RegisterOutput(context.Number, (value, _) => calls.Add(-value));

        registration.Dispose();
        registration.Dispose();
        context.NumberSource.Set(1);

        Assert.Equal([-1], calls);
    }

    [Fact]
    public void Dispose_AfterContextDisposed_IsNoOp()
    {
        var context = new DisposalContext();
        var registration = context.RegisterOutput(context.Number, (_, _) => { });
        var collectionRegistration = context.RegisterOutput(context.Items, (_, _) => { });

        context.Dispose();

        registration.Dispose();
        collectionRegistration.Dispose();
    }

    [Fact]
    public void DisposingContext_UnregistersAllHandlers()
    {
        var context = new DisposalContext();
        var calls = 0;
        var registration = context.RegisterOutput(context.Number, (_, _) => calls++);
        context.RegisterOutput(context.Items, (_, _) => calls++);
        var source = context.NumberSource;
        var items = context.ItemsSource;

        context.Dispose();
        source.Set(1);
        items.Push("a");

        Assert.Equal(0, calls);
        Assert.Throws<ObjectDisposedException>(() => context.RegisterOutput(context.Number, (_, _) => { }));
        registration.Dispose();
    }

    [Fact]
    public void Dispose_FromInsideHandler_StopsLaterCalls()
    {
        using var context = new DisposalContext();
        var calls = new List<int>();
        IDisposable? registration = null;
        registration = context.RegisterOutput(context.Number, (value, _) =>
        {
            calls.Add(value);
            registration!.Dispose();
        });

        context.NumberSource.Set(1);
        context.NumberSource.Set(2);

        Assert.Equal([1], calls);
    }

    [Fact]
    public void Dispose_OfOneHandlerFromAnother_SkipsItInTheSameRound()
    {
        using var context = new DisposalContext();
        var calls = new List<string>();
        IDisposable? second = null;
        context.RegisterOutput(context.Number, (_, _) =>
        {
            calls.Add("first");
            second!.Dispose();
        });
        second = context.RegisterOutput(context.Number, (_, _) => calls.Add("second"));

        context.NumberSource.Set(1);

        Assert.Equal(["first"], calls);
    }

    [Fact]
    public async Task RegisterAndDispose_Concurrently_IsThreadSafe()
    {
        using var context = new DisposalContext();
        context.NumberSource.Set(0);
        var keptCalls = 0;
        var removedCalls = 0;
        context.RegisterOutput(context.Number, (_, _) => Interlocked.Increment(ref keptCalls));

        // Register and dispose handlers on several threads while another thread keeps changing the source
        using var cts = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            var i = 1;
            while (!cts.IsCancellationRequested)
            {
                context.NumberSource.Set(i++);
                context.ItemsSource.Push($"item{i}");
            }
        }, TestContext.Current.CancellationToken);

        Parallel.For(0, 8, _ =>
        {
            for (var n = 0; n < 500; n++)
            {
                var registration = context.RegisterOutput(context.Number, (_, _) => Interlocked.Increment(ref removedCalls));
                var collectionRegistration = context.RegisterOutput(context.Items, (_, _) => Interlocked.Increment(ref removedCalls));
                registration.Dispose();
                collectionRegistration.Dispose();
                registration.Dispose();
            }
        });

        cts.Cancel();
        await writer;

        // Only the kept handler remains registered
        var removedBefore = Volatile.Read(ref removedCalls);
        var keptBefore = Volatile.Read(ref keptCalls);
        context.NumberSource.Set(-1);
        context.ItemsSource.Push("last");

        Assert.Equal(removedBefore, Volatile.Read(ref removedCalls));
        Assert.Equal(keptBefore + 1, Volatile.Read(ref keptCalls));
    }
}
