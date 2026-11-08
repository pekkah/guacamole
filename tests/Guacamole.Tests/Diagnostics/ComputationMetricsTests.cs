using System.Diagnostics.Metrics;
using Guacamole.Diagnostics;
using Xunit;

namespace Guacamole.Tests.Diagnostics;

/// <summary>
/// Providers record <see cref="GuacamoleMetrics.ComputationsTotal"/>,
/// <see cref="GuacamoleMetrics.ComputationDuration"/> and
/// <see cref="GuacamoleMetrics.ComputationErrors"/> when they compute a value
/// (not when they return a cached one).
/// </summary>
public partial class ComputationMetricsTests
{
    [ComputingContext]
    private partial class MetricsContext : ComputingContextBase
    {
        public partial IncrementalValueProvider<int> Number { get; }
    }

    /// <summary>
    /// Collects measurements of the computation instruments tagged with one tracking name, so
    /// tests running in parallel (which use other tracking names) do not interfere.
    /// </summary>
    /// <remarks>
    /// Instruments are matched by meter and instrument name, never by comparing with the
    /// <see cref="GuacamoleMetrics"/> fields: when the listener starts before
    /// <see cref="GuacamoleMetrics"/> is initialized, its instruments are published from inside its
    /// static constructor, where those fields are still <c>null</c> on the initializing thread, so a
    /// reference comparison would never enable them (and the first test to touch the metrics would
    /// record nothing).
    /// </remarks>
    private sealed class Recorder : IDisposable
    {
        private const string MeterName = "Guacamole.Core";
        private const string ComputationsName = "guacamole.computations.total";
        private const string ErrorsName = "guacamole.computation.errors.total";
        private const string DurationName = "guacamole.computation.duration";

        private readonly MeterListener _listener = new();
        private readonly string _trackingName;
        private readonly object _lock = new();

        public Recorder(string trackingName)
        {
            _trackingName = trackingName;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MeterName
                    && instrument.Name is ComputationsName or ErrorsName or DurationName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument, 1, tags));
            _listener.Start();
        }

        public int Computations { get; private set; }
        public int Errors { get; private set; }
        public int Durations { get; private set; }

        private void Record(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var matches = false;
            foreach (var tag in tags)
            {
                if (tag.Key == "tracking.name" && Equals(tag.Value, _trackingName))
                    matches = true;
            }

            if (!matches)
                return;

            lock (_lock)
            {
                if (instrument.Name == ComputationsName)
                    Computations += (int)value;
                else if (instrument.Name == ErrorsName)
                    Errors += (int)value;
                else
                    Durations += (int)value;
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public void GetValue_WhenComputing_RecordsComputationAndDuration()
    {
        var trackingName = "metrics-compute-" + Guid.NewGuid();
        using var recorder = new Recorder(trackingName);
        using var context = new MetricsContext();
        context.NumberSource.Set(2);
        var doubled = context.Number.Select(static x => x * 2).WithTrackingName(trackingName);

        Assert.Equal(4, doubled.Value);

        Assert.Equal(1, recorder.Computations);
        Assert.Equal(1, recorder.Durations);
        Assert.Equal(0, recorder.Errors);
    }

    [Fact]
    public void GetValue_WhenCached_DoesNotRecordAnotherComputation()
    {
        var trackingName = "metrics-cached-" + Guid.NewGuid();
        using var recorder = new Recorder(trackingName);
        using var context = new MetricsContext();
        context.NumberSource.Set(2);
        var doubled = context.Number.Select(static x => x * 2).WithTrackingName(trackingName);

        _ = doubled.Value;
        _ = doubled.Value;

        Assert.Equal(1, recorder.Computations);
    }

    [Fact]
    public void GetValue_WhenComputationFails_RecordsError()
    {
        var trackingName = "metrics-error-" + Guid.NewGuid();
        using var recorder = new Recorder(trackingName);
        using var context = new MetricsContext();
        context.NumberSource.Set(2);
        var failing = context.Number
            .Select<int, int>(static _ => throw new InvalidOperationException("boom"))
            .WithTrackingName(trackingName);

        Assert.True(failing.GetValue().IsError);

        Assert.Equal(1, recorder.Computations);
        Assert.Equal(1, recorder.Errors);
    }
}
