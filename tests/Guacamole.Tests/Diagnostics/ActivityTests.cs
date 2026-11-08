using System.Diagnostics;
using Guacamole.Diagnostics;
using Guacamole.Pipeline;
using Guacamole.Tests.Pipeline;
using Xunit;

namespace Guacamole.Tests.Diagnostics;

/// <summary>
/// Issue #78: <see cref="GuacamoleMetrics.StartActivity"/> records its tags when something listens
/// to <see cref="GuacamoleMetrics.ActivitySource"/>, and allocates nothing when nothing does.
/// </summary>
/// <remarks>
/// No other test listens to the activity source, and the tests of one class do not run in
/// parallel with each other, so "nothing listens" holds for the allocation test.
/// </remarks>
public class ActivityTests
{
    [Fact]
    public void StartActivity_WithoutListeners_ReturnsNullAndAllocatesNothing()
    {
        Assert.False(GuacamoleMetrics.ActivitySource.HasListeners());
        const string Id = "no-listener-node";
        const string Type = "TestNode";

        // Warm up, so the measurement does not include one-time initialization
        Assert.Null(GuacamoleMetrics.StartActivity("graph.add_node", ("node.id", Id), ("node.type", Type)));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            using var activity = GuacamoleMetrics.StartActivity("graph.add_node", ("node.id", Id), ("node.type", Type));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AddNode_WithAListener_RecordsAnActivityWithTheNodeTags()
    {
        const string Id = "activity-test-node";
        var recorded = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GuacamoleMetrics.ActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("node.id") as string == Id)
                {
                    lock (recorded)
                        recorded.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        new ComputationGraph().AddNode(new TestComputationNode(Id, typeof(int)));

        var activity = Assert.Single(recorded);
        Assert.Equal("graph.add_node", activity.OperationName);
        Assert.Equal(nameof(TestComputationNode), activity.GetTagItem("node.type"));
    }
}
