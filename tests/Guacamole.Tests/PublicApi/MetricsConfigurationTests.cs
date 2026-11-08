using System.Diagnostics.Metrics;
using Guacamole.Diagnostics;
using Xunit;

namespace Guacamole.Tests.PublicApi;

/// <summary>
/// <see cref="MetricsConfiguration"/> as documented in its XML docs and the migration guide: the
/// presets, <c>With</c>, <c>SetGlobal</c>/<c>Current</c>, and that <c>EnableComputationMetrics</c>
/// and the dimension switches control what providers record.
/// </summary>
/// <remarks>
/// The tests that change the global configuration run in the non-parallel collection, so they do not
/// affect other metrics tests.
/// </remarks>
[Collection(ConcurrencyTestCollection.Name)]
public class MetricsConfigurationTests
{
    /// <summary>Collects the tags of computation measurements for one tracking name.</summary>
    private sealed class ComputationRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly object _lock = new();
        private readonly string _trackingName;
        private readonly List<Dictionary<string, object?>> _measurements = new();

        public ComputationRecorder(string trackingName)
        {
            _trackingName = trackingName;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Guacamole.Core" && instrument.Name == "guacamole.computations.total")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                var dictionary = new Dictionary<string, object?>();
                foreach (var tag in tags)
                    dictionary[tag.Key] = tag.Value;

                // Without the tracking name dimension, recognise this test's provider by its stage
                if (Equals(dictionary.GetValueOrDefault("tracking.name"), _trackingName)
                    || Equals(dictionary.GetValueOrDefault("pipeline.stage"), _trackingName))
                {
                    lock (_lock)
                        _measurements.Add(dictionary);
                }
            });
            _listener.Start();
        }

        public IReadOnlyList<Dictionary<string, object?>> Measurements
        {
            get
            {
                lock (_lock)
                    return _measurements.ToArray();
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private static void WithGlobal(MetricsConfiguration configuration, Action action)
    {
        var previous = MetricsConfiguration.Current;
        MetricsConfiguration.SetGlobal(configuration);
        try
        {
            action();
        }
        finally
        {
            MetricsConfiguration.SetGlobal(previous);
        }
    }

    private static void ComputeOnce(string name)
    {
        using var context = new ApiContext();
        context.NumberSource.Set(1);
        var provider = context.Number.Select(static x => x + 1).WithTrackingName(name).WithPipelineStage(name);
        Assert.Equal(2, provider.Value);
    }

    [Fact]
    public void Current_DefaultsToProduction()
    {
        // No test leaves another configuration in place
        Assert.Equal(MetricsConfiguration.Production, MetricsConfiguration.Current);
    }

    [Fact]
    public void Presets_HaveTheDocumentedSettings()
    {
        Assert.True(MetricsConfiguration.Development is { EnableComputationMetrics: true, EnableCacheMetrics: true, EnableGraphMetrics: true, CollectTrackingNameDimensions: true, CollectPipelineStageDimensions: true });
        Assert.True(MetricsConfiguration.Production is { EnableComputationMetrics: true, EnableCacheMetrics: true, EnableGraphMetrics: false, CollectTrackingNameDimensions: true, CollectPipelineStageDimensions: false });
        Assert.True(MetricsConfiguration.Minimal is { EnableComputationMetrics: false, EnableCacheMetrics: true, EnableGraphMetrics: false });
        Assert.True(MetricsConfiguration.Disabled is { EnableComputationMetrics: false, EnableCacheMetrics: false, EnableGraphMetrics: false, CollectTrackingNameDimensions: false, CollectPipelineStageDimensions: false });
        Assert.True(new MetricsConfiguration() is { EnableComputationMetrics: true, EnableCacheMetrics: true, EnableGraphMetrics: true, CollectTrackingNameDimensions: true, CollectPipelineStageDimensions: true });
    }

    [Fact]
    public void With_OverridesOnlyTheGivenSettings_AndReturnsANewInstance()
    {
        var changed = MetricsConfiguration.Production.With(enableGraphMetrics: true);

        Assert.NotSame(MetricsConfiguration.Production, changed);
        Assert.True(changed.EnableGraphMetrics);
        Assert.Equal(MetricsConfiguration.Production with { EnableGraphMetrics = true }, changed);
        Assert.False(MetricsConfiguration.Production.EnableGraphMetrics);
        Assert.Equal(MetricsConfiguration.Production, MetricsConfiguration.Production.With());
    }

    [Fact]
    public void SetGlobal_RejectsNull_AndChangesCurrent()
    {
        Assert.Throws<ArgumentNullException>(() => MetricsConfiguration.SetGlobal(null!));

        WithGlobal(MetricsConfiguration.Minimal, () => Assert.Same(MetricsConfiguration.Minimal, MetricsConfiguration.Current));
    }

    [Fact]
    public void ComputationMetricsDisabled_RecordsNoComputations()
    {
        var name = $"metrics-off-{Guid.NewGuid():N}";
        using var recorder = new ComputationRecorder(name);

        WithGlobal(MetricsConfiguration.Development.With(enableComputationMetrics: false), () => ComputeOnce(name));

        Assert.Empty(recorder.Measurements);
    }

    [Fact]
    public void Dimensions_FollowTheConfiguration()
    {
        var name = $"metrics-dims-{Guid.NewGuid():N}";
        using var recorder = new ComputationRecorder(name);

        WithGlobal(MetricsConfiguration.Development, () => ComputeOnce(name));
        var development = Assert.Single(recorder.Measurements);
        Assert.Equal(name, development["tracking.name"]);
        Assert.Equal(name, development["pipeline.stage"]);

        WithGlobal(MetricsConfiguration.Development with { CollectTrackingNameDimensions = false }, () => ComputeOnce(name));
        var withoutName = recorder.Measurements[^1];
        Assert.Equal(2, recorder.Measurements.Count);
        Assert.False(withoutName.ContainsKey("tracking.name"));
        Assert.Equal(name, withoutName["pipeline.stage"]);
    }
}
