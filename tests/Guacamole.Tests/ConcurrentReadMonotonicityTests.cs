using Guacamole.Pipeline;
using Xunit;

namespace Guacamole.Tests;

/// <summary>
/// A read that starts after another read on the same thread returned never returns an older value,
/// even while a source change is in progress on another thread and a slower read that started
/// before the change commits after the newer read (issue #99).
/// </summary>
public partial class ConcurrentReadMonotonicityTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public sealed record Item(int Key, int Value);

    [ComputingContext]
    private partial class ItemsContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Item> Items { get; }
    }

    /// <summary>
    /// A slow read starts after a change has ended and reads the source's contents (1), then pauses.
    /// A second change (1 to 2) pauses while marking: it has published the new contents and
    /// incremented the source's version, but not advanced the revision. A read on this thread then
    /// computes 2, the slow read commits 1 at the same revision, and the next read on this thread
    /// must not return 1 (it did, through the revision fast path, before the fix).
    /// </summary>
    [Fact]
    public void ReadAfterRead_DuringAChange_WithAnOlderPassCommittingLast_DoesNotGoBack()
    {
        var token = TestContext.Current.CancellationToken;
        using var context = new ItemsContext();
        var state = context.Graph.SharedState;
        var source = context.ItemsSource;
        source.Push(new Item(0, 0));

        var slowThread = -1;
        using var slowEntered = new ManualResetEventSlim();
        using var slowRelease = new ManualResetEventSlim();
        var value = context.Items.Select(item =>
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref slowThread))
            {
                slowEntered.Set();
                slowRelease.Wait(Timeout, token);
            }

            return item.Value;
        }).Collect().Select(values => values[0]);
        var other = context.Items.Collect().Select(values => values.Length);

        Assert.Equal(0, value.Value);
        source.Replace(new Item(0, 0), new Item(0, 1));
        Assert.Equal(1, other.Value); // a stamp decision, so that the next change walks the dependents

        // The slow read: reads 1, then pauses before committing
        var slowResult = -1;
        var slow = new Thread(() =>
        {
            Volatile.Write(ref slowThread, Environment.CurrentManagedThreadId);
            slowResult = value.Value;
        });
        slow.Start();
        Assert.True(slowEntered.Wait(Timeout, token));

        // The second change, paused after marking the first node
        using var marking = new ManualResetEventSlim();
        using var finishMarking = new ManualResetEventSlim();
        state.OnNodeMarked = _ =>
        {
            state.OnNodeMarked = null;
            marking.Set();
            finishMarking.Wait(Timeout, token);
        };
        var writer = new Thread(() => source.Replace(new Item(0, 1), new Item(0, 2)));
        writer.Start();
        Assert.True(marking.Wait(Timeout, token));

        int first, second;
        try
        {
            first = value.Value;
            slowRelease.Set();
            Assert.True(slow.Join(Timeout));
            second = value.Value;
        }
        finally
        {
            slowRelease.Set();
            finishMarking.Set();
            writer.Join(Timeout);
        }

        Assert.Equal(1, slowResult);
        Assert.Equal(2, first);
        Assert.Equal(2, second);
        Assert.Equal(2, value.Value);
    }

    /// <summary>
    /// A source starts its change before it publishes its new contents, so no read can see the new
    /// contents while the graph shows no change in progress. Observed from the item comparison that
    /// <c>Replace</c> makes under the source's lock, before it modifies the source.
    /// </summary>
    [Fact]
    public void SourceMutation_StartsTheChangeBeforeModifyingTheSource()
    {
        using var context = new ObservedContext();
        var state = context.Graph.SharedState;
        context.ItemsSource.Push(new Observed(1));
        Assert.True(NoChangeInProgress(state));

        bool? inProgress = null;
        Observed.OnEquals = () => inProgress ??= !NoChangeInProgress(state);
        try
        {
            context.ItemsSource.Replace(new Observed(1), new Observed(2));
        }
        finally
        {
            Observed.OnEquals = null;
        }

        Assert.True(inProgress);
        Assert.True(NoChangeInProgress(state)); // ended once the change was marked
        Assert.Equal([2], context.Items.Values.Select(item => item.Value));
    }

    /// <summary>
    /// Starting a change that turns out to change nothing (or throws) ends it.
    /// </summary>
    [Fact]
    public void SourceMutation_ThatChangesNothingOrThrows_EndsTheChange()
    {
        using var context = new ItemsContext();
        var state = context.Graph.SharedState;
        context.ItemsSource.Push(new Item(0, 0));

        context.ItemsSource.Push(new Item(0, 0)); // already present
        context.ItemsSource.Replace(new Item(0, 0), new Item(0, 0)); // equal
        context.ItemsSource.Remove(new Item(9, 9)); // absent
        Assert.Throws<InvalidOperationException>(() => context.ItemsSource.Replace(new Item(9, 9), new Item(9, 10)));

        Assert.True(NoChangeInProgress(state));
    }

    private static bool NoChangeInProgress(SharedGraphState state) =>
        (state.ChangeState & SharedGraphState.ActiveChangesMask) == 0;

    public sealed class Observed(int value) : IEquatable<Observed>
    {
        [ThreadStatic]
        public static Action? OnEquals;

        public int Value { get; } = value;

        public bool Equals(Observed? other)
        {
            OnEquals?.Invoke();
            return other is not null && other.Value == Value;
        }

        public override bool Equals(object? obj) => Equals(obj as Observed);

        public override int GetHashCode() => Value;
    }

    [ComputingContext]
    private partial class ObservedContext : ComputingContextBase
    {
        public partial IncrementalValuesProvider<Observed> Items { get; }
    }
}
